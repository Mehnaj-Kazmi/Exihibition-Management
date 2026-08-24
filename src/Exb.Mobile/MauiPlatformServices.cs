using Exb.Mobile.Shared.Services;

namespace Exb.Mobile;

/// <summary>
/// Native-host implementation: <see cref="Preferences"/> for ordinary
/// settings (base URL, last email), <see cref="SecureStorage"/> for the
/// bearer token (OS keychain on iOS, Keystore-backed on Android — a step up
/// from the original Flutter app's shared_preferences-for-everything, kept
/// deliberately since MAUI makes the safer option free), and
/// <see cref="Launcher"/> for opening websites/mailto/tel links externally,
/// and ZXing.Net.MAUI behind <see cref="OpenScannerAsync"/> for live camera
/// QR scanning on Android/iOS (see <see cref="ScannerPage"/>).
/// </summary>
public sealed class MauiPlatformServices : IPlatformServices
{
    public Task<string?> GetPreference(string key)
        => Task.FromResult(Preferences.Default.Get(key, (string?)null));

    public Task SetPreference(string key, string? value)
    {
        if (value is null) Preferences.Default.Remove(key);
        else Preferences.Default.Set(key, value);
        return Task.CompletedTask;
    }

    public async Task<string?> GetSecureToken()
    {
        try { return await SecureStorage.Default.GetAsync("exb.token"); }
        catch { return null; }
    }

    public async Task SetSecureToken(string? value)
    {
        if (value is null) { SecureStorage.Default.Remove("exb.token"); return; }
        await SecureStorage.Default.SetAsync("exb.token", value);
    }

    public Task OpenExternal(Uri uri) => Launcher.Default.OpenAsync(uri);

    public string PlatformName => DeviceInfo.Platform.ToString().ToLowerInvariant();
    public string DeviceLabel => DeviceInfo.Model;

    public bool SupportsLiveCamera =>
#if ANDROID || IOS || MACCATALYST
        true;
#else
        false; // Windows: ZXing's camera view isn't supported there — manual entry instead.
#endif

    public async Task<bool> OpenScannerAsync(Func<string, Task<ScannerFeedback>> onScanned)
    {
#if ANDROID || IOS || MACCATALYST
        // Everything here must run on the UI thread: the permission prompt,
        // the modal push, and the camera view's own lifecycle. The caller is
        // on Blazor's dispatcher, which is not necessarily the same thread.
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                return false; // caller shows the "camera access is off" guidance

            var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
            if (navigation is null) return false;

            var scanner = new ScannerPage(onScanned);
            await navigation.PushModalAsync(scanner);
            await scanner.Closed;
            return true;
        });
#else
        await Task.CompletedTask;
        return false;
#endif
    }
}
