using Exb.Mobile.Shared.Models;

namespace Exb.Mobile.Shared.Services;

public enum AuthStage { Starting, SignedOut, SignedIn }

/// <summary>
/// App-wide session state — the C# equivalent of the Flutter app's
/// ChangeNotifier-based AppState/AppScope: a scoped DI service the whole
/// component tree observes via <see cref="Changed"/>. See the porting notes
/// for why the 401 vs. other-error distinction in <see cref="RestoreAsync"/>
/// matters (a dead venue wifi at opening time must not silently and
/// permanently sign everyone out).
/// </summary>
public sealed class AppState
{
    private const string TokenKey = "exb.token";
    private const string BaseUrlKey = "exb.baseUrl";
    private const string EmailKey = "exb.email";

    private readonly IPlatformServices _platform;
    private readonly Func<string, ApiClient> _clientFactory;

    public AppState(IPlatformServices platform, Func<string, ApiClient> clientFactory, string defaultBaseUrl)
    {
        _platform = platform;
        _clientFactory = clientFactory;
        BaseUrl = defaultBaseUrl;
        Api = _clientFactory(BaseUrl);
        Api.Unauthorised += OnServerRejectedToken;
    }

    public event Action? Changed;
    private void Notify() => Changed?.Invoke();

    public ApiClient Api { get; private set; }
    public AuthStage Stage { get; private set; } = AuthStage.Starting;
    public Visitor? Visitor { get; private set; }
    public Exhibition? Exhibition { get; private set; }
    public string BaseUrl { get; private set; }
    public string? LastEmail { get; private set; }
    public string? SignedOutBecause { get; private set; }
    public int CataloguesToday { get; private set; }

    public IReadOnlyList<Hall> Halls => Exhibition?.Halls ?? [];
    public IReadOnlyList<Category> Categories => Exhibition?.Categories ?? [];
    public IReadOnlyList<string> Countries => Exhibition?.Countries ?? [];
    public IReadOnlyList<DateOnly> ProgrammeDates => Exhibition?.ProgrammeDates ?? [];

    public IReadOnlyList<Category> SubCategoriesOf(int? categoryId)
    {
        if (categoryId is null) return [];
        return Categories.FirstOrDefault(c => c.Id == categoryId)?.Children ?? [];
    }

    /// <summary>
    /// Restores the saved session at launch. Whatever happens, this must end
    /// with the app on either the login screen or the home shell: the splash
    /// it runs behind has no controls at all, so any escape from here strands
    /// the visitor on a spinner with nothing to tap.
    /// </summary>
    public async Task RestoreAsync()
    {
        try
        {
            BaseUrl = await _platform.GetPreference(BaseUrlKey) ?? BaseUrl;
            LastEmail = await _platform.GetPreference(EmailKey);
            RebuildClient();

            string? token = await _platform.GetSecureToken();
            if (string.IsNullOrEmpty(token))
            {
                Stage = AuthStage.SignedOut;
                Notify();
                return;
            }

            Api.Token = token;
            try
            {
                Visitor = await Api.Me();
                await LoadExhibitionAsync();
                Stage = AuthStage.SignedIn;
            }
            catch (ApiException ex)
            {
                if (ex.IsUnauthorised)
                {
                    await _platform.SetSecureToken(null);
                }
                else
                {
                    // Keep the token — a network hiccup at launch should not
                    // permanently sign the visitor out; they can retry.
                    SignedOutBecause = ex.Message;
                }
                Stage = AuthStage.SignedOut;
            }
        }
        catch (Exception ex)
        {
            // Anything the API client did not turn into an ApiException — a
            // malformed stored address, unreadable secure storage, a response
            // that would not deserialise — used to escape this method and
            // leave the splash spinner on screen for good. Land on the login
            // screen instead and say what went wrong.
            SignedOutBecause = $"The app could not start its last session ({ex.GetType().Name}). Sign in again, or check the address below.";
            Stage = AuthStage.SignedOut;
        }
        Notify();
    }

    public async Task SetBaseUrlAsync(string url)
    {
        BaseUrl = url;
        await _platform.SetPreference(BaseUrlKey, url);
        await _platform.SetSecureToken(null);
        RebuildClient();
        Visitor = null;
        Exhibition = null;
        Stage = AuthStage.SignedOut;
        Notify();
    }

    private void RebuildClient()
    {
        Api.Unauthorised -= OnServerRejectedToken;
        Api = _clientFactory(BaseUrl);
        Api.Unauthorised += OnServerRejectedToken;
    }

    public async Task<LoginCodeRequest> RequestCodeAsync(string email)
    {
        string trimmed = email.Trim();
        var result = await Api.RequestLoginCode(trimmed);
        LastEmail = trimmed;
        await _platform.SetPreference(EmailKey, trimmed);
        return result;
    }

    public async Task VerifyCodeAsync(string email, string code)
    {
        var (token, visitor) = await Api.VerifyLoginCode(
            email, code, _platform.PlatformName, _platform.DeviceLabel, AppConfig.AppVersion);

        await _platform.SetSecureToken(token);
        Visitor = visitor;
        SignedOutBecause = null;
        await LoadExhibitionAsync();
        Stage = AuthStage.SignedIn;
        Notify();
    }

    public async Task SignOutAsync()
    {
        // Sign out locally first and tell the server afterwards. Waiting on
        // the network here means that when the venue wifi is slow — exactly
        // when somebody is most likely to be giving up and signing out — the
        // button appears to do nothing for the length of the HTTP timeout.
        // Revoking the token server-side is worth attempting but is not worth
        // making the visitor wait for, and the local token is gone either way.
        var client = Api;

        await _platform.SetSecureToken(null);
        Visitor = null;
        Exhibition = null;
        CataloguesToday = 0;
        SignedOutBecause = null;
        Stage = AuthStage.SignedOut;
        Notify();

        _ = Task.Run(async () =>
        {
            try { await client.Logout(); } catch { /* best-effort revocation */ }
        });
    }

    private void OnServerRejectedToken()
    {
        if (Stage != AuthStage.SignedIn) return;
        Stage = AuthStage.SignedOut;
        Visitor = null;
        Exhibition = null;
        SignedOutBecause = "Your session has ended. Sign in with your registered email address to continue.";
        _ = _platform.SetSecureToken(null);
        Notify();
    }

    private async Task LoadExhibitionAsync()
    {
        Exhibition = await Api.GetExhibition();
        await RefreshCatalogueCountAsync();
    }

    public async Task RefreshAsync()
    {
        await LoadExhibitionAsync();
        Notify();
    }

    public async Task RefreshCatalogueCountAsync()
    {
        try { CataloguesToday = (await Api.MyCatalogues()).Count; }
        catch (ApiException) { /* a badge count failing shouldn't interrupt anyone */ }
    }

    public void SetCatalogueCount(int count)
    {
        CataloguesToday = count;
        Notify();
    }

    public async Task UpdateConsentAsync(bool? email = null, bool? tracking = null)
    {
        Visitor = await Api.UpdateConsent(email, tracking);
        Notify();
    }
}

public static class AppConfig
{
    public const string AppVersion = "1.0.0";
}
