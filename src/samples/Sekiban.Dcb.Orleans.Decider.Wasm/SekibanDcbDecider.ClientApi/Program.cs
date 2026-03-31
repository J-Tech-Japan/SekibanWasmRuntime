using System.Security.Claims;
using System.Text.Encodings.Web;
using Dcb.EventSource;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.WasmRuntime.Remote;
using SekibanDcbDecider.ApiService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

builder.Services
    .AddAuthentication("Sample")
    .AddScheme<AuthenticationSchemeOptions, SampleAuthenticationHandler>("Sample", _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("Sample")
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);
builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();

var wasmServerBaseUrl = ResolveWasmServerBase(builder.Configuration);
builder.Services.AddHttpClient("wasmserver", client =>
{
    client.BaseAddress = new Uri(wasmServerBaseUrl);
});

builder.Services.AddScoped<ISekibanExecutor>(sp =>
    new RemoteSekibanExecutor(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver"),
        sp.GetRequiredService<DcbDomainTypes>(),
        sp.GetRequiredService<IEventPublisher>(),
        sp.GetRequiredService<ILogger<RemoteSekibanExecutor>>(),
        sp.GetRequiredService<ILoggerFactory>()));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapOpenApi();

var apiRoute = app.MapGroup("/api");
apiRoute.MapStudentEndpoints();
apiRoute.MapClassRoomEndpoints();
apiRoute.MapEnrollmentEndpoints();
apiRoute.MapWeatherEndpoints();
apiRoute.MapRoomEndpoints();
apiRoute.MapReservationEndpoints();
apiRoute.MapApprovalEndpoints();
apiRoute.MapUserDirectoryEndpoints();
apiRoute.MapTestDataEndpoints();

app.Run();

static string ResolveWasmServerBase(IConfiguration configuration)
{
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("WASM_SERVER_URL"),
        Environment.GetEnvironmentVariable("services__wasmserver__http__0"),
        Environment.GetEnvironmentVariable("services__wasmserver__https__0"),
        configuration["services:wasmserver:http:0"],
        configuration["services:wasmserver:https:0"],
        "http://127.0.0.1:3000"
    };

    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
    }

    return "http://127.0.0.1:3000";
}

file sealed class NoOpEventPublisher : IEventPublisher
{
    public Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<Sekiban.Dcb.Tags.ITag> Tags)> events,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

file sealed class SampleAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var displayName = Request.Headers["X-Debug-Display-Name"].FirstOrDefault() ?? "Sample User";
        var userId = Request.Headers["X-Debug-User-Id"].FirstOrDefault() ?? Guid.Empty.ToString();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, displayName),
            new Claim("display_name", displayName),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
