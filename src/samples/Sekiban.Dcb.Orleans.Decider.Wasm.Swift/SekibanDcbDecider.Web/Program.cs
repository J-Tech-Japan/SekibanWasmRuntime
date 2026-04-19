using SekibanDcbDecider.Web;
using SekibanDcbDecider.Web.Components;

// Swift sample's Blazor frontend. Every API call goes to the Swift ClientApi
// (Hummingbird server listening on CLIENT_API_URL). Until the Phase 3 auth work lands,
// AuthApiClient still points at the Swift ClientApi — the auth endpoints return 404 and
// AuthApiClient's try/catch swallows the error so the non-auth pages work anyway.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var clientApiBase = ResolveBaseUrl(
    builder.Configuration,
    envKey: "CLIENT_API_URL",
    serviceKey: "clientapi",
    fallback: "http://127.0.0.1:6298");
var authApiBase = ResolveBaseUrl(
    builder.Configuration,
    envKey: "AUTH_API_URL",
    serviceKey: "apiservice",
    fallback: clientApiBase);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Named client so razor components can inject a plain HttpClient pointed at the Swift
// ClientApi.
builder.Services.AddHttpClient(
    "SwiftClientApi",
    client => client.BaseAddress = new Uri(clientApiBase));

// Typed clients for every domain Page (ClassRooms / Students / Enrollments / Weather).
builder.Services.AddHttpClient<ClassRoomApiClient>(client => client.BaseAddress = new Uri(clientApiBase));
builder.Services.AddHttpClient<StudentApiClient>(client => client.BaseAddress = new Uri(clientApiBase));
builder.Services.AddHttpClient<EnrollmentApiClient>(client => client.BaseAddress = new Uri(clientApiBase));
builder.Services.AddHttpClient<WeatherApiClient>(client => client.BaseAddress = new Uri(clientApiBase));
builder.Services.AddHttpClient<AuthApiClient>(client => client.BaseAddress = new Uri(authApiBase));

builder.Services.AddSingleton(new SwiftClientApiEndpoint(clientApiBase));

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// No UseHttpsRedirection — Aspire AppHost wires a single HTTP endpoint for this
// sample. Redirecting to HTTPS would hang on a phantom port.

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

    var fromAspire = configuration[$"services:{serviceKey}:http:0"];
    if (!string.IsNullOrWhiteSpace(fromAspire)) return fromAspire;

    return fallback;
}

public sealed record SwiftClientApiEndpoint(string BaseUrl);
