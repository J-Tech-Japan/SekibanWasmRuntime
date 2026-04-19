using SekibanDcbDecider.Web.Components;

// Swift sample's minimal Blazor frontend. Hits the Swift ClientApi (Hummingbird) directly
// and does NOT depend on a parallel auth/apiservice — the other language samples' Web
// projects carry that baggage because they share a full meeting-room domain with the
// native template, but the Swift sample is scoped to ClassRoomEnrollment MV + benchmark
// write paths so a single HttpClient aimed at ClientApi is enough.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var clientApiBase = ResolveBaseUrl(
    builder.Configuration,
    envKey: "CLIENT_API_URL",
    serviceKey: "clientapi",
    fallback: "http://127.0.0.1:6298");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Named client so Razor components can inject an `HttpClient` via the factory.
builder.Services.AddHttpClient(
    "SwiftClientApi",
    client => client.BaseAddress = new Uri(clientApiBase));

// Expose the resolved base URL so pages that need to render absolute links can read it
// without hard-coding the port here.
builder.Services.AddSingleton(new SwiftClientApiEndpoint(clientApiBase));

var app = builder.Build();

app.MapDefaultEndpoints();

// No HTTPS redirect — the Aspire AppHost wires a single non-proxied HTTP endpoint for this
// Swift sample on ASPNETCORE_URLS. Enabling UseHttpsRedirection would issue a 307 to an
// HTTPS port that was never configured, leaving curl/Playwright hanging.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolveBaseUrl(
    IConfiguration configuration,
    string envKey,
    string serviceKey,
    string fallback)
{
    var fromEnv = configuration[envKey];
    if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

    // Aspire injects service URLs into `services:<name>:http:0` keys when a service
    // reference is added. Pick that up so the frontend works without any manual env var.
    var fromAspire = configuration[$"services:{serviceKey}:http:0"];
    if (!string.IsNullOrWhiteSpace(fromAspire)) return fromAspire;

    return fallback;
}

public sealed record SwiftClientApiEndpoint(string BaseUrl);
