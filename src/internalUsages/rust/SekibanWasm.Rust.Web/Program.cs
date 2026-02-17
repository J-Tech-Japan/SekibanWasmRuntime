using SekibanWasm.Rust.Web;
using SekibanWasm.Rust.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var clientApiBaseUrl = ResolveClientApiBase(builder.Configuration);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri(clientApiBaseUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
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
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("CLIENT_API_URL"),
        Environment.GetEnvironmentVariable("services__clientapi__http__0"),
        Environment.GetEnvironmentVariable("services__clientapi__https__0"),
        configuration["services:clientapi:http:0"],
        configuration["services:clientapi:https:0"],
        "http://127.0.0.1:5000"
    };

    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
    }

    return "http://127.0.0.1:5000";
}
