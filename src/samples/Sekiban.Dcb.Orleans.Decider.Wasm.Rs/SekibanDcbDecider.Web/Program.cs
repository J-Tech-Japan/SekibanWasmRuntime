using SekibanDcbDecider.Web;
using SekibanDcbDecider.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var apiBaseUrl = ResolveClientApiBase(builder.Configuration);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddHttpClient(
    "ApiService",
    client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
    });

builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddHttpClient<StudentApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddHttpClient<ClassRoomApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddHttpClient<EnrollmentApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddHttpClient<AuthApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
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

static string ResolveClientApiBase(IConfiguration configuration)
{
    string[] candidates =
    [
        Environment.GetEnvironmentVariable("CLIENT_API_URL") ?? string.Empty,
        Environment.GetEnvironmentVariable("services__clientapi__http__0") ?? string.Empty,
        Environment.GetEnvironmentVariable("services__clientapi__https__0") ?? string.Empty,
        configuration["services:clientapi:http:0"] ?? string.Empty,
        configuration["services:clientapi:https:0"] ?? string.Empty,
        "http://127.0.0.1:5000"
    ];

    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
    }

    return "http://127.0.0.1:5000";
}
