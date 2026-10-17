using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Relia.Api.Auth;
using Relia.Api.Configuration;
using Relia.Api.Controllers;
using Relia.Api.Errors;
using Relia.Api.Persistence;
using Relia.Api.Rail;
using Relia.Api.Repositories;
using Relia.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- API: JSON camelCase, enums como string, errores problem+json ----
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    })
    .ConfigureApiBehaviorOptions(o =>
    {
        // El contrato usa 422 validation_error con detalle por campo, no 400.
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Datos inválidos",
                Type = "about:blank",
            };
            problem.Extensions["code"] = "validation_error";
            problem.Extensions["errors"] = ctx.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(err => new
                {
                    // Propiedades del cuerpo en camelCase; headers como Idempotency-Key, tal cual.
                    field = e.Key.Contains('-') ? e.Key : JsonNamingPolicy.CamelCase.ConvertName(e.Key),
                    message = err.ErrorMessage,
                }))
                .ToList();
            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
                ContentTypes = { "application/problem+json" },
            };
        };
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

// ---- Configuración ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<RailProxyOptions>(builder.Configuration.GetSection(RailProxyOptions.Section));
builder.Services.Configure<ReliaOptions>(builder.Configuration.GetSection(ReliaOptions.Section));
builder.Services.PostConfigure<ReliaOptions>(o => o.DemoMode = builder.Configuration.GetValue<bool>("DEMO_MODE"));

// ---- Persistencia: PostgreSQL con el esquema de backend/db/db_ddl.sql ----
builder.Services.AddDbContext<ReliaDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Relia"), ReliaDbContext.MapEnums));
builder.Services.AddScoped<DemoSeeder>();

builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IAgentRepository, EfAgentRepository>();
builder.Services.AddScoped<IAdminRepository, EfAdminRepository>();
builder.Services.AddScoped<IFeeRepository, EfFeeRepository>();
builder.Services.AddScoped<IWalletKeyRepository, EfWalletKeyRepository>();
builder.Services.AddScoped<IOperationRepository, EfOperationRepository>();
builder.Services.AddScoped<IPaymentRepository, EfPaymentRepository>();
builder.Services.AddScoped<ILedgerRepository, EfLedgerRepository>();
builder.Services.AddScoped<IHistoryRepository, EfHistoryRepository>();
builder.Services.AddScoped<IReceiptRepository, EfReceiptRepository>();
builder.Services.AddScoped<ISettlementRepository, EfSettlementRepository>();
builder.Services.AddScoped<ISimulatedMessageRepository, EfSimulatedMessageRepository>();
builder.Services.AddScoped<IOtpChallengeRepository, EfOtpChallengeRepository>();

// ---- Riel: cliente HTTP del proxy (contracts/rail-proxy.openapi.yaml) ----
builder.Services.AddHttpClient<IRailClient, RailClient>((sp, http) =>
{
    var rail = sp.GetRequiredService<IOptions<RailProxyOptions>>().Value;
    http.BaseAddress = new Uri(rail.BaseUrl.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", rail.ApiKey);
    http.Timeout = TimeSpan.FromSeconds(rail.TimeoutSeconds);
});

// ---- Servicios ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<ILedger, Ledger>();
builder.Services.AddScoped<ICashAdjustmentService, CashAdjustmentService>();
builder.Services.AddSingleton<RailRefreshThrottle>();
builder.Services.AddScoped<IOperationSync, OperationSync>();
builder.Services.AddScoped<IOperationService, OperationService>();
builder.Services.AddScoped<ISimulatedInbox, SimulatedInbox>();
builder.Services.AddScoped<IKeyService, KeyService>();
builder.Services.AddScoped<IKeyLinkService, KeyLinkService>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<ISettlementService, SettlementService>();

// ---- Auth: JWT bearer con rol y agentId ----
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException(
        "Falta Jwt:SigningKey (mínimo 32 bytes). Configúrela con user-secrets o la variable Jwt__SigningKey.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            RoleClaimType = "role",
            NameClaimType = "sub",
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

// ---- Rate limit en memoria de la vinculación pública: Relia:KeyLinkRateLimitPerMinute por IP ----
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(KeyLinksController.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IOptions<ReliaOptions>>().Value.KeyLinkRateLimitPerMinute,
            Window = TimeSpan.FromMinutes(1),
        }));
    o.OnRejected = async (context, ct) =>
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await response.WriteAsJsonAsync(
            new { type = "about:blank", title = "Demasiados intentos", status = 429, code = "rate_limited" },
            (JsonSerializerOptions?)null, "application/problem+json", ct);
    };
});

var app = builder.Build();

// Base recién creada (docker compose up sobre un volumen nuevo): sembrar los datos de la demo.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedIfEmptyAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Relia API v1"));
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

await app.RunAsync();

/// <summary>401 unauthorized y 403 forbidden en problem+json, como el resto de errores.</summary>
internal class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext http, AuthorizationPolicy policy,
        PolicyAuthorizationResult result)
    {
        if (result.Succeeded)
        {
            await _default.HandleAsync(next, http, policy, result);
            return;
        }

        var (status, code, title) = result.Forbidden
            ? (StatusCodes.Status403Forbidden, "forbidden", "Sin permiso")
            : (StatusCodes.Status401Unauthorized, "unauthorized", "Sin sesión");
        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(
            new { type = "about:blank", title, status, code },
            (JsonSerializerOptions?)null, "application/problem+json");
    }
}

public partial class Program;
