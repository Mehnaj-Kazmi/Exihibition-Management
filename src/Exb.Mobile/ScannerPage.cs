#if ANDROID || IOS || MACCATALYST
using Exb.Mobile.Shared.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Exb.Mobile;

/// <summary>
/// The native camera scanner, pushed modally over the BlazorWebView. It stays
/// open across scans — the visitor works down an aisle scanning stand after
/// stand, and each result is confirmed in the banner at the bottom without
/// ever leaving the camera. Mirrors the original app's scanner behaviour:
/// QR-only, a 3-second same-code cooldown so an unsure visitor can simply
/// point again, and camera shutdown the moment the page goes away.
/// </summary>
public sealed class ScannerPage : ContentPage
{
    private readonly Func<string, Task<ScannerFeedback>> _onScanned;
    private readonly TaskCompletionSource _closed = new();
    private readonly CameraBarcodeReaderView _camera;
    private readonly Label _banner;
    private readonly Border _bannerBox;

    private bool _busy;
    private string? _coolingToken;
    private Timer? _cooldown;

    /// <summary>Completes when the visitor dismisses the scanner.</summary>
    public Task Closed => _closed.Task;

    public ScannerPage(Func<string, Task<ScannerFeedback>> onScanned)
    {
        _onScanned = onScanned;
        BackgroundColor = Color.FromArgb("#14161A");

        _camera = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode,
                AutoRotate = true,
                TryHarder = true,
                Multiple = false,
            },
            CameraLocation = CameraLocation.Rear,
            IsDetecting = true,
        };
        _camera.BarcodesDetected += OnBarcodesDetected;

        var reticle = new Border
        {
            WidthRequest = 230,
            HeightRequest = 230,
            Stroke = Color.FromRgba(255, 255, 255, 179),
            StrokeThickness = 3,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };

        var caption = new Label
        {
            Text = "Point at the QR code on the stand",
            TextColor = Colors.White,
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(16, 0, 16, 18),
        };

        var topBar = new HorizontalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(8),
            Children =
            {
                IconButton("☀", "Torch", (s, e) => _camera.IsTorchOn = !_camera.IsTorchOn),
                IconButton("↻", "Switch camera", (s, e) =>
                    _camera.CameraLocation = _camera.CameraLocation == CameraLocation.Rear
                        ? CameraLocation.Front : CameraLocation.Rear),
                IconButton("✕", "Close", async (s, e) => await CloseAsync()),
            },
        };

        _banner = new Label
        {
            Text = "Everything you scan today is collected into one pack and emailed to you this evening.",
            TextColor = Color.FromArgb("#E4E2E6"),
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        _bannerBox = new Border
        {
            BackgroundColor = Color.FromArgb("#26272A"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14, 12),
            Margin = new Thickness(12),
            Content = _banner,
        };

        var cameraArea = new Grid { Children = { _camera, reticle, caption, topBar } };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children = { cameraArea, _bannerBox },
        };
        Grid.SetRow(cameraArea, 0);
        Grid.SetRow(_bannerBox, 1);
    }

    private static Button IconButton(string glyph, string hint, EventHandler handler)
    {
        var button = new Button
        {
            Text = glyph,
            FontSize = 20,
            TextColor = Colors.White,
            BackgroundColor = Color.FromRgba(0, 0, 0, 90),
            CornerRadius = 22,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
        };
        SemanticProperties.SetDescription(button, hint);
        button.Clicked += handler;
        return button;
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;
        MainThread.BeginInvokeOnMainThread(async () => await HandleAsync(value));
    }

    private async Task HandleAsync(string value)
    {
        if (_busy || value == _coolingToken) return;
        _busy = true;
        _coolingToken = value;
        try
        {
            var feedback = await _onScanned(value);
            _banner.Text = feedback.Detail is null ? feedback.Message : $"{feedback.Message}\n{feedback.Detail}";
            _bannerBox.BackgroundColor = feedback.Ok
                ? Color.FromArgb("#0F3B22")   // matches the app's "good" banner
                : Color.FromArgb("#4A3200");  // matches the app's "warning" banner
            _banner.TextColor = Colors.White;
        }
        catch
        {
            _banner.Text = "Could not record that scan. Check the venue wifi and point at the code again.";
            _bannerBox.BackgroundColor = Color.FromArgb("#601410");
            _banner.TextColor = Colors.White;
        }
        finally
        {
            _busy = false;
            // The same code becomes scannable again after 3 seconds, so a
            // visitor who is not sure it worked can simply point at it again.
            _cooldown?.Dispose();
            _cooldown = new Timer(_ => _coolingToken = null, null, 3000, Timeout.Infinite);
        }
    }

    private async Task CloseAsync()
    {
        _camera.IsDetecting = false;
        await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        // Stop the camera the moment the page goes away — privacy (app stores
        // flag a backgrounded-but-running camera) and battery for an all-day
        // show, same rationale as the original app's lifecycle handling.
        _camera.IsDetecting = false;
        _cooldown?.Dispose();
        _closed.TrySetResult();
        base.OnDisappearing();
    }
}
#endif
