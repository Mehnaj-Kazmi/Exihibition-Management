using Exb.Mobile.Shared.Services;
using Microsoft.Extensions.Logging;
#if ANDROID || IOS || MACCATALYST
using ZXing.Net.Maui.Controls;
#endif

namespace Exb.Mobile;

public static class MauiProgram
{
    // The Android emulator's own loopback proxy back to the host machine —
    // "localhost" from inside the emulator means the emulator itself, not
    // the machine running the exhibition system. A real device on the venue
    // wifi will use the "Change" address dialog on the login screen instead.
    private static string DefaultBaseUrl =>
#if ANDROID
        "http://10.0.2.2:5080";
#else
        "http://localhost:5080";
#endif

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if ANDROID || IOS || MACCATALYST
        builder.UseBarcodeReader();
#endif

        builder.Services.AddSingleton<IPlatformServices, MauiPlatformServices>();
        builder.Services.AddSingleton<Func<string, ApiClient>>(sp => baseUrl =>
            new ApiClient(new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") }));
        builder.Services.AddSingleton(sp => new AppState(
            sp.GetRequiredService<IPlatformServices>(),
            sp.GetRequiredService<Func<string, ApiClient>>(),
            DefaultBaseUrl));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
