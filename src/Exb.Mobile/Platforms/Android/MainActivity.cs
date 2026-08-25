using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace Exb.Mobile;

// SoftInput.AdjustResize shrinks the WebView when the keyboard opens instead
// of sliding it out of view, so fields and buttons stay reachable while typing.
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Keep the web view inside the status and navigation bars. Left
        // edge-to-edge, the page draws underneath both: the app bar's title is
        // sliced by the status bar and — far worse — the bottom tab bar sits
        // behind the system navigation bar, where it cannot be tapped at all,
        // leaving the whole app unnavigable. CSS safe-area insets are not a
        // reliable substitute here; the older Android WebView reports them as
        // zero, so the fit has to be arranged on the window itself.
        if (Window is not null)
            WindowCompat.SetDecorFitsSystemWindows(Window, true);
    }
}
