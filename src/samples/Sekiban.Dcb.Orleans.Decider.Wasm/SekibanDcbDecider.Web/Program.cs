using SekibanDcbDecider.Web;
using SekibanDcbDecider.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
var clientApiBase = ResolveBaseUrl(
    builder.Configuration,
    "CLIENT_API_URL",
    "clientapi",
    "http://127.0.0.1:5000");
var authApiBase = ResolveBaseUrl(
    builder.Configuration,
    "AUTH_API_URL",
    "apiservice",
    "http://127.0.0.1:5001");

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add HttpClient for API calls
builder.Services.AddHttpClient(
    "ApiService",
    client =>
    {
        client.BaseAddress = new Uri(clientApiBase);
    });

// Add WeatherApiClient
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri(clientApiBase);
});

// Add StudentApiClient
builder.Services.AddHttpClient<StudentApiClient>(client =>
{
    client.BaseAddress = new Uri(clientApiBase);
});

// Add ClassRoomApiClient
builder.Services.AddHttpClient<ClassRoomApiClient>(client =>
{
    client.BaseAddress = new Uri(clientApiBase);
});

// Add EnrollmentApiClient
builder.Services.AddHttpClient<EnrollmentApiClient>(client =>
{
    client.BaseAddress = new Uri(clientApiBase);
});

// Add AuthApiClient
builder.Services.AddHttpClient<AuthApiClient>(client =>
{
    client.BaseAddress = new Uri(authApiBase);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

static string ResolveBaseUrl(
    IConfiguration configuration,
    string explicitEnvName,
    string serviceName,
    string fallback)
{
    string[] candidates =
    [
        Environment.GetEnvironmentVariable(explicitEnvName) ?? string.Empty,
        Environment.GetEnvironmentVariable($"services__{serviceName}__http__0") ?? string.Empty,
        Environment.GetEnvironmentVariable($"services__{serviceName}__https__0") ?? string.Empty,
        configuration[$"services:{serviceName}:http:0"] ?? string.Empty,
        configuration[$"services:{serviceName}:https:0"] ?? string.Empty,
        fallback
    ];

    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
    }

    return fallback;
}
