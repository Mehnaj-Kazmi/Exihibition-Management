using System.Security.Cryptography;
using System.Text;
using Exb.Core.Text;
using Exb.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Exb.Data.Services;

public enum LoginCodeOutcome
{
    /// <summary>A code was generated and queued to the visitor's registered address.</summary>
    Sent,

    /// <summary>No active visitor holds that email. The API does not say so out loud.</summary>
    UnknownEmail,

    /// <summary>Too many codes requested for this address in a short window.</summary>
    RateLimited,
}

public enum VerifyOutcome
{
    Success,

    /// <summary>Wrong code, or a code for a different address.</summary>
    Incorrect,

    /// <summary>Right code, but it has expired or has already been used.</summary>
    Expired,

    /// <summary>Guessed at too many times; the visitor must request a fresh one.</summary>
    TooManyAttempts,
}

/// <summary>
/// The outcome of asking for a code. <c>DevelopmentCode</c> carries the code
/// itself, and is populated only when mail is not actually being delivered —
/// without it a tester on the default "outbox" provider could never sign in,
/// because the email they are waiting for is sitting in a database table.
/// </summary>
public sealed record LoginCodeRequestResult(
    LoginCodeOutcome Outcome,
    int ExpiresInSeconds,
    string? DevelopmentCode = null);

public sealed record MobileIdentity(
    int VisitorId,
    string FullName,
    string Email,
    string RegistrationCode,
    string? Company,
    string? JobTitle,
    string? Country,
    bool ConsentEmail,
    bool ConsentTracking,
    bool HasBadge);

public sealed record VerifyResult(
    VerifyOutcome Outcome,
    string? Token = null,
    DateTime? ExpiresUtc = null,
    MobileIdentity? Identity = null);

/// <summary>
/// Signing a visitor in to the mobile app from the address they registered with.
///
/// Visitors have never had passwords in this system — they have a badge and an
/// email — so the app proves the address instead: a six-digit code, emailed
/// through the same outbox as everything else, exchanged once for a long-lived
/// device token.
///
/// Two things here are deliberate and worth not undoing. Requesting a code for
/// an address that is not registered reports success anyway, because an endpoint
/// that answers "no such visitor" turns the attendee list into something anyone
/// can enumerate. And both the code and the session token are stored only as
/// hashes, so neither the database nor an admin screen can hand out someone
/// else's session.
/// </summary>
public sealed class MobileAuthService(
    IDbContextFactory<ExhibitionDbContext> factory,
    MailQueue mail,
    SettingsStore settings,
    ILogger<MobileAuthService> logger)
{
    /// <summary>Long enough to fetch a phone from a bag, short enough to be a one-time code.</summary>
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Covers a multi-day show and the setup day before it.</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private const int MaxAttemptsPerCode = 5;
    private const int MaxCodesPerHour = 5;

    public async Task<LoginCodeRequestResult> RequestCodeAsync(
        string email, string? fromIp, CancellationToken ct = default)
    {
        int lifetimeSeconds = (int)CodeLifetime.TotalSeconds;
        string normalised = (email ?? "").Trim().ToLowerInvariant();
        if (normalised.Length == 0) return new LoginCodeRequestResult(LoginCodeOutcome.UnknownEmail, lifetimeSeconds);

        await using var db = await factory.CreateDbContextAsync(ct);

        var visitor = await db.Visitors
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.IsActive && v.Email == normalised, ct);

        if (visitor is null)
        {
            logger.LogInformation("Mobile login requested for an address with no active visitor.");
            return new LoginCodeRequestResult(LoginCodeOutcome.UnknownEmail, lifetimeSeconds);
        }

        var now = DateTime.UtcNow;
        int recent = await db.VisitorLoginCodes
            .CountAsync(c => c.VisitorId == visitor.Id && c.CreatedUtc > now.AddHours(-1), ct);

        if (recent >= MaxCodesPerHour)
        {
            logger.LogWarning("Mobile login codes rate-limited for visitor {VisitorId}.", visitor.Id);
            return new LoginCodeRequestResult(LoginCodeOutcome.RateLimited, lifetimeSeconds);
        }

        // Any code still outstanding is retired first: two live codes for one
        // person doubles the guessing surface for no benefit to the visitor.
        var outstanding = await db.VisitorLoginCodes
            .Where(c => c.VisitorId == visitor.Id && c.ConsumedUtc == null && c.ExpiresUtc > now)
            .ToListAsync(ct);
        foreach (var stale in outstanding) stale.ExpiresUtc = now;

        string code = NewCode();

        var row = new VisitorLoginCode
        {
            VisitorId = visitor.Id,
            EmailSentTo = normalised,
            CodeHash = Hash(code),
            CreatedUtc = now,
            ExpiresUtc = now.Add(CodeLifetime),
            RequestedFromIp = fromIp,
        };

        db.VisitorLoginCodes.Add(row);
        await db.SaveChangesAsync(ct);

        var app = settings.Current;
        long mailId = await mail.QueueAsync(
            normalised,
            visitor.FullName,
            $"{code} is your {app.Exhibition.Name} sign-in code",
            BuildHtml(code, visitor.FullName, app.Exhibition.Name),
            BuildText(code, app.Exhibition.Name),
            kind: "mobile-login",
            ct: ct);

        row.OutboxEmailId = mailId;
        db.VisitorLoginCodes.Update(row);
        await db.SaveChangesAsync(ct);

        // With the provider on its default the email never leaves the building,
        // so the code goes back in the response instead — otherwise the app
        // cannot be signed in to at all before SMTP is configured.
        bool mailIsLive = string.Equals(app.Mail.Provider, "smtp", StringComparison.OrdinalIgnoreCase);

        return new LoginCodeRequestResult(
            LoginCodeOutcome.Sent,
            lifetimeSeconds,
            mailIsLive ? null : code);
    }

    public async Task<VerifyResult> VerifyAsync(
        string email, string code, string? platform, string? deviceName, string? appVersion,
        CancellationToken ct = default)
    {
        string normalised = (email ?? "").Trim().ToLowerInvariant();
        string entered = new((code ?? "").Where(char.IsDigit).ToArray());

        if (normalised.Length == 0 || entered.Length == 0) return new VerifyResult(VerifyOutcome.Incorrect);

        await using var db = await factory.CreateDbContextAsync(ct);

        var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.IsActive && v.Email == normalised, ct);
        if (visitor is null) return new VerifyResult(VerifyOutcome.Incorrect);

        var now = DateTime.UtcNow;

        var candidate = await db.VisitorLoginCodes
            .Where(c => c.VisitorId == visitor.Id && c.ConsumedUtc == null)
            .OrderByDescending(c => c.CreatedUtc)
            .FirstOrDefaultAsync(ct);

        if (candidate is null) return new VerifyResult(VerifyOutcome.Expired);
        if (candidate.Attempts >= MaxAttemptsPerCode) return new VerifyResult(VerifyOutcome.TooManyAttempts);
        if (candidate.ExpiresUtc <= now) return new VerifyResult(VerifyOutcome.Expired);

        if (!FixedTimeEquals(candidate.CodeHash, Hash(entered)))
        {
            candidate.Attempts++;
            await db.SaveChangesAsync(ct);

            return candidate.Attempts >= MaxAttemptsPerCode
                ? new VerifyResult(VerifyOutcome.TooManyAttempts)
                : new VerifyResult(VerifyOutcome.Incorrect);
        }

        candidate.ConsumedUtc = now;

        string token = Tokens.New(40);
        var session = new MobileSession
        {
            VisitorId = visitor.Id,
            TokenHash = Hash(token),
            Platform = Trim(platform, 32),
            DeviceName = Trim(deviceName, 120),
            AppVersion = Trim(appVersion, 40),
            CreatedUtc = now,
            LastSeenUtc = now,
            ExpiresUtc = now.Add(SessionLifetime),
        };

        db.MobileSessions.Add(session);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Mobile sign-in for visitor {VisitorId} from {Platform}.", visitor.Id, platform ?? "?");

        return new VerifyResult(VerifyOutcome.Success, token, session.ExpiresUtc, Describe(visitor));
    }

    /// <summary>
    /// Resolve a bearer token to a visitor. Called on every authenticated
    /// request, so it touches one indexed row and only writes back the
    /// last-seen stamp when it has moved by more than a few minutes.
    /// </summary>
    public async Task<MobileIdentity?> ResolveAsync(string? bearerToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken)) return null;

        await using var db = await factory.CreateDbContextAsync(ct);

        string hash = Hash(bearerToken.Trim());
        var session = await db.MobileSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        var now = DateTime.UtcNow;
        if (session is null || !session.IsUsable(now)) return null;

        var visitor = await db.Visitors.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == session.VisitorId && v.IsActive, ct);
        if (visitor is null) return null;

        if ((now - session.LastSeenUtc).TotalMinutes >= 5)
        {
            session.LastSeenUtc = now;
            await db.SaveChangesAsync(ct);
        }

        return Describe(visitor);
    }

    /// <summary>Sign this device out. Other devices keep their sessions.</summary>
    public async Task<bool> RevokeAsync(string? bearerToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken)) return false;

        await using var db = await factory.CreateDbContextAsync(ct);

        string hash = Hash(bearerToken.Trim());
        var session = await db.MobileSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null || session.RevokedUtc is not null) return false;

        session.RevokedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The two consents the visitor is allowed to change from their own phone.</summary>
    public async Task<MobileIdentity?> UpdateConsentAsync(
        int visitorId, bool? consentEmail, bool? consentTracking, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId && v.IsActive, ct);
        if (visitor is null) return null;

        if (consentEmail is { } email) visitor.ConsentEmail = email;
        if (consentTracking is { } tracking) visitor.ConsentTracking = tracking;
        visitor.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Describe(visitor);
    }

    /// <summary>Clear out spent and expired codes. Run from the same nightly pass as everything else.</summary>
    public async Task<int> PurgeExpiredCodesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-2);
        return await db.VisitorLoginCodes
            .Where(c => c.ExpiresUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static MobileIdentity Describe(Visitor v) => new(
        v.Id, v.FullName, v.Email, v.RegistrationCode, v.Company, v.JobTitle, v.Country,
        v.ConsentEmail, v.ConsentTracking, v.BadgeEpc.Length > 0);

    /// <summary>
    /// Six digits from the cryptographic RNG, rejection-sampled so that every
    /// code from 000000 to 999999 is equally likely — a plain modulo would make
    /// the low codes very slightly more common, which is exactly the kind of
    /// detail that makes a guessing attack cheaper than it looks.
    /// </summary>
    private static string NewCode()
    {
        const int range = 1_000_000;
        const uint limit = uint.MaxValue - (uint.MaxValue % range);

        Span<byte> bytes = stackalloc byte[4];
        uint value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BitConverter.ToUInt32(bytes);
        }
        while (value >= limit);

        return (value % range).ToString("D6");
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string a, string b)
        => a.Length == b.Length
           && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    private static string? Trim(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null
         : value.Length <= max ? value.Trim()
         : value.Trim()[..max];

    private static string BuildHtml(string code, string name, string exhibition) => $"""
        <div style="font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:15px;color:#222">
          <p>Hello {Html.Escape(name)},</p>
          <p>Your sign-in code for the <b>{Html.Escape(exhibition)}</b> app is:</p>
          <p style="font-size:30px;font-weight:700;letter-spacing:6px;margin:20px 0">{code}</p>
          <p>It expires in 15 minutes and can be used once.</p>
          <p style="color:#666;font-size:13px">
            If you did not ask to sign in, you can ignore this message — nobody can
            get into your account without this code.
          </p>
        </div>
        """;

    private static string BuildText(string code, string exhibition) => $"""
        Your sign-in code for the {exhibition} app is {code}

        It expires in 15 minutes and can be used once.
        If you did not ask to sign in, ignore this message.
        """;
}
