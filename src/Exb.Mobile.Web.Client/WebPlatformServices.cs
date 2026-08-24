using Exb.Mobile.Shared.Services;
using Microsoft.JSInterop;

namespace Exb.Mobile.Web.Client;

/// <summary>
/// Browser-hosted implementation: localStorage for preferences (there is no
/// browser equivalent of SecureStorage's OS keychain, so the token lives
/// alongside everything else here — acceptable for the preview/PWA host;
/// the MAUI head uses the real SecureStorage instead). window.open for
/// external links. No live camera — the Scan screen falls back to a manual
/// token entry field on this host (see IPlatformServices.SupportsLiveCamera).
/// </summary>
public sealed class WebPlatformServices(IJSRuntime js) : IPlatformServices
{
    public async Task<string?> GetPreference(string key)
        => await js.InvokeAsync<string?>("localStorage.getItem", key);

    public async Task SetPreference(string key, string? value)
    {
        if (value is null) await js.InvokeVoidAsync("localStorage.removeItem", key);
        else await js.InvokeVoidAsync("localStorage.setItem", key, value);
    }

    public Task<string?> GetSecureToken() => GetPreference("exb.token");
    public Task SetSecureToken(string? value) => SetPreference("exb.token", value);

    public async Task OpenExternal(Uri uri) => await js.InvokeVoidAsync("open", uri.ToString(), "_blank");

    public string PlatformName => "web";
    public string DeviceLabel => "Browser";
    public bool SupportsLiveCamera => false;

    public Task<bool> OpenScannerAsync(Func<string, Task<ScannerFeedback>> onScanned) => Task.FromResult(false);
}
