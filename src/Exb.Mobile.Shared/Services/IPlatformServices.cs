namespace Exb.Mobile.Shared.Services;

/// <summary>
/// Everything that differs between a native MAUI head (Preferences/SecureStorage,
/// Launcher, a real camera) and the Blazor WebAssembly preview host (browser
/// localStorage, window.open, a placeholder camera area) — implemented once per
/// host project so the shared UI never touches a platform API directly.
/// </summary>
public interface IPlatformServices
{
    Task<string?> GetPreference(string key);
    Task SetPreference(string key, string? value);

    Task<string?> GetSecureToken();
    Task SetSecureToken(string? value);

    Task OpenExternal(Uri uri);

    /// <summary>"web" | "android" | "ios" | "windows" | "maccatalyst" — sent on sign-in, shown on the profile screen.</summary>
    string PlatformName { get; }

    /// <summary>A short device label — sent on sign-in for the visitor's own record of where they signed in from.</summary>
    string DeviceLabel { get; }

    /// <summary>True on hosts with a real camera-based scanner (native MAUI); false on the Web preview (static placeholder).</summary>
    bool SupportsLiveCamera { get; }

    /// <summary>
    /// Open the host's camera scanner, if it has one. Every detected QR code is
    /// passed to <paramref name="onScanned"/>, whose returned feedback is shown
    /// on the scanner itself so the visitor never has to leave the camera to
    /// see whether the scan landed — the scanner stays open for the next stand,
    /// matching the original app's continuous-scan behaviour. Returns false
    /// when there is no camera to open (Web preview, Windows) or the visitor
    /// has denied camera permission; the caller shows its fallback UI then.
    /// </summary>
    Task<bool> OpenScannerAsync(Func<string, Task<ScannerFeedback>> onScanned);
}

/// <summary>What the scanner overlay shows after each detection.</summary>
public sealed record ScannerFeedback(bool Ok, string Message, string? Detail = null);
