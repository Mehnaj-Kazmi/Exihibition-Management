using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Exb.Mobile.Shared.Models;

namespace Exb.Mobile.Shared.Services;

public sealed class ApiException(string message, int? statusCode = null, bool isNetwork = false) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public bool IsNetwork { get; } = isNetwork;
    public bool IsUnauthorised => StatusCode == 401;
}

/// <summary>
/// A thin, hand-written REST client for /api/v1 — mirrors the original Flutter
/// app's ApiClient deliberately (see the porting notes): no heavy HTTP
/// framework, one JSON shape per endpoint, and error messages meant to be
/// shown to the visitor directly rather than a generic "something went wrong."
/// </summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private string? _token;

    public event Action? Unauthorised;

    public ApiClient(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public string? Token
    {
        get => _token;
        set => _token = value;
    }

    public bool HasToken => !string.IsNullOrEmpty(_token);

    private HttpRequestMessage Build(HttpMethod method, string path, object? body = null, Dictionary<string, string?>? query = null)
    {
        string url = "api/v1" + path;
        if (query is { Count: > 0 })
        {
            var pairs = query.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
            string qs = string.Join("&", pairs);
            if (qs.Length > 0) url += "?" + qs;
        }

        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.ParseAdd("application/json");
        if (!string.IsNullOrEmpty(_token))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    private async Task<T> Send<T>(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException("The exhibition system is not responding. The network in the hall may be busy — try again in a moment.", isNetwork: true);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException($"Cannot reach the exhibition system. Check that you are on the venue wifi and try again. ({ex.Message})", isNetwork: true);
        }

        string raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized) Unauthorised?.Invoke();

            string? serverMessage = null;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                    serverMessage = err.GetString();
            }
            catch (JsonException) { /* malformed body — fall through to the status-keyed message */ }

            throw new ApiException(serverMessage ?? FallbackMessage((int)response.StatusCode), (int)response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(raw)) return default!;
        try
        {
            return JsonSerializer.Deserialize<T>(raw, Json)!;
        }
        catch (JsonException)
        {
            return default!;
        }
    }

    private static string FallbackMessage(int status) => status switch
    {
        400 => "That request was not understood by the exhibition system.",
        401 => "Please sign in again.",
        404 => "That is no longer available.",
        429 => "Too many attempts. Wait a moment and try again.",
        >= 500 => "The exhibition system had a problem. Try again shortly.",
        _ => $"Something went wrong (error {status}).",
    };

    // --- auth -----------------------------------------------------------

    public Task<LoginCodeRequest> RequestLoginCode(string email, CancellationToken ct = default)
        => Send<LoginCodeRequest>(Build(HttpMethod.Post, "/auth/request-code", new { email }), ct);

    public async Task<(string Token, Visitor Visitor)> VerifyLoginCode(
        string email, string code, string? platform, string? deviceName, string? appVersion, CancellationToken ct = default)
    {
        var result = await Send<VerifyResponse>(Build(HttpMethod.Post, "/auth/verify",
            new { email, code, platform, deviceName, appVersion }), ct);

        if (string.IsNullOrEmpty(result.Token))
            throw new ApiException("Sign-in did not return a valid session. Try again.");

        _token = result.Token;
        return (result.Token, result.Visitor);
    }

    public async Task Logout(CancellationToken ct = default)
    {
        try { await Send<object>(Build(HttpMethod.Post, "/auth/logout"), ct); }
        catch (ApiException) { /* best-effort — the token is cleared locally regardless */ }
        finally { _token = null; }
    }

    // --- visitor's own profile -------------------------------------------

    public Task<Visitor> Me(CancellationToken ct = default)
        => Send<Visitor>(Build(HttpMethod.Get, "/me"), ct);

    public Task<Visitor> UpdateConsent(bool? email = null, bool? tracking = null, CancellationToken ct = default)
        => Send<Visitor>(Build(HttpMethod.Patch, "/me/consent", new { consentEmail = email, consentTracking = tracking }), ct);

    // --- exhibition bootstrap / directory ---------------------------------

    public Task<Exhibition> GetExhibition(CancellationToken ct = default)
        => Send<Exhibition>(Build(HttpMethod.Get, "/exhibition"), ct);

    public Task<List<Hall>> Halls(CancellationToken ct = default)
        => Send<List<Hall>>(Build(HttpMethod.Get, "/halls"), ct);

    public Task<HallDetail> Hall(int id, int page = 1, int pageSize = 50, CancellationToken ct = default)
        => Send<HallDetail>(Build(HttpMethod.Get, $"/halls/{id}", query: new()
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
        }), ct);

    public Task<List<Category>> Categories(CancellationToken ct = default)
        => Send<List<Category>>(Build(HttpMethod.Get, "/categories"), ct);

    public Task<Paged<Exhibitor>> Exhibitors(
        string? query = null, int? categoryId = null, int? subCategoryId = null, int? hallId = null, string? country = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
        => Send<Paged<Exhibitor>>(Build(HttpMethod.Get, "/exhibitors", query: new()
        {
            ["q"] = query,
            ["categoryId"] = categoryId?.ToString(),
            ["subCategoryId"] = subCategoryId?.ToString(),
            ["hallId"] = hallId?.ToString(),
            ["country"] = country,
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
        }), ct);

    public Task<ExhibitorDetail> Exhibitor(int id, CancellationToken ct = default)
        => Send<ExhibitorDetail>(Build(HttpMethod.Get, $"/exhibitors/{id}"), ct);

    public Task<SearchResults> SearchEverything(string query, CancellationToken ct = default)
        => Send<SearchResults>(Build(HttpMethod.Get, "/search", query: new() { ["q"] = query }), ct);

    // --- programme ----------------------------------------------------------

    public Task<Paged<Session>> Sessions(
        string? query = null, DateOnly? date = null, string? kind = null, int? hallId = null,
        int? categoryId = null, int? subCategoryId = null, bool bookmarkedOnly = false,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
        => Send<Paged<Session>>(Build(HttpMethod.Get, "/sessions", query: new()
        {
            ["q"] = query,
            ["date"] = date?.ToString("yyyy-MM-dd"),
            ["kind"] = kind,
            ["hallId"] = hallId?.ToString(),
            ["categoryId"] = categoryId?.ToString(),
            ["subCategoryId"] = subCategoryId?.ToString(),
            ["bookmarked"] = bookmarkedOnly ? "true" : null,
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
        }), ct);

    public Task<SessionDetail> Session(int id, CancellationToken ct = default)
        => Send<SessionDetail>(Build(HttpMethod.Get, $"/sessions/{id}"), ct);

    public Task SetBookmarked(int sessionId, bool bookmarked, CancellationToken ct = default)
        => Send<object>(Build(bookmarked ? HttpMethod.Post : HttpMethod.Delete, $"/sessions/{sessionId}/bookmark"), ct);

    // --- scanning and the e-catalogue pack -----------------------------------

    public Task<ScanResult> Scan(string scanned, CancellationToken ct = default)
        => Send<ScanResult>(Build(HttpMethod.Post, "/me/scan", new { token = scanned }), ct);

    public async Task<List<ScannedStand>> MyCatalogues(CancellationToken ct = default)
    {
        var wire = await Send<CataloguesResponse>(Build(HttpMethod.Get, "/me/catalogues"), ct);
        return wire.Items;
    }

    public async Task<int> RequestCatalogue(int kioskId, CancellationToken ct = default)
    {
        var wire = await Send<ScanResult>(Build(HttpMethod.Post, "/me/catalogues", new { kioskId }), ct);
        return wire.TodayCount;
    }

    public Task SetCatalogueIncluded(int kioskId, bool included, CancellationToken ct = default)
        => Send<object>(Build(HttpMethod.Patch, $"/me/catalogues/{kioskId}", new { included }), ct);

    public async Task<VisitorDay> MyDay(CancellationToken ct = default)
    {
        var wire = await Send<VisitorDayWire>(Build(HttpMethod.Get, "/me/day"), ct);
        if (!wire.TrackingConsent)
            return new VisitorDay { TrackingConsent = false, Message = wire.Message };

        var day = wire.Day ?? new VisitorDayPayload();
        return new VisitorDay
        {
            TrackingConsent = true,
            TotalDwellText = day.TotalDwellText,
            StandsWithInterest = day.StandsWithInterest,
            Visited = day.Visited,
            Categories = day.Categories,
            Missed = day.Missed,
        };
    }

    // --- private wire-only shapes (unwrapped into public models above) ------

    private sealed class VerifyResponse
    {
        public string Token { get; set; } = "";
        public Visitor Visitor { get; set; } = new();
    }

    private sealed class CataloguesResponse
    {
        public List<ScannedStand> Items { get; set; } = [];
    }

    private sealed class VisitorDayWire
    {
        public bool TrackingConsent { get; set; }
        public string? Message { get; set; }
        public VisitorDayPayload? Day { get; set; }
    }

    private sealed class VisitorDayPayload
    {
        public string? TotalDwellText { get; set; }
        public int? StandsWithInterest { get; set; }
        public List<VisitedStand> Visited { get; set; } = [];
        public List<CategoryInterest> Categories { get; set; } = [];
        public List<MissedStand> Missed { get; set; } = [];
    }
}
