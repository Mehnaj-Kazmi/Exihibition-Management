using Exb.Mobile.Shared.Services;
using Exb.Mobile.Web.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Running in a browser on the same machine as the exhibition system during
// development, so "localhost" (this host's own origin, matching where the
// admin console listens) is the sane default — unlike the native app, which
// defaults to the Android emulator's special loopback address instead.
const string defaultBaseUrl = "http://localhost:5080";

builder.Services.AddScoped<IPlatformServices, WebPlatformServices>();
builder.Services.AddScoped<Func<string, ApiClient>>(sp => baseUrl =>
    new ApiClient(new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") }));
builder.Services.AddScoped(sp => new AppState(
    sp.GetRequiredService<IPlatformServices>(),
    sp.GetRequiredService<Func<string, ApiClient>>(),
    defaultBaseUrl));

await builder.Build().RunAsync();
