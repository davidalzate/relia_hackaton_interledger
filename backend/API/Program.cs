using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Relia.Api.Auth;
using Relia.Api.Errors;
using Relia.Api.Persistence;
using Relia.Api.Rail;
using Relia.Api.Repositories;
using Relia.Api.Services;
using Relia.Api.Services.Demo;
using Relia.Api.Services.Support;

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
                .SelectMany(e => e.Value!.Errors.Select(err => new { field = JsonFieldName(e.Key), message = err.ErrorMessage }))
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

// ---- Persistencia: PostgreSQL con el esquema v0.2 ----
builder.Services.AddDbContext<ReliaDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Relia"), ReliaDbContext.MapEnums));

builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IAgentRepository, EfAgentRepository>();
builder.Services.AddScoped<IAdminRepository, EfAdminRepository>();
builder.Services.AddScoped<IFeeRepository, EfFeeRepository>();
builder.Services.AddScoped<IWalletKeyRepository, EfWalletKeyRepository>();
builder.Services.AddScoped<IOperationRepository, EfOperationRepository>();
builder.Services.AddScoped<IPaymentRepository, EfPaymentRepository>();
builder.Services.AddScoped<ILedgerRepository, EfLedgerRepository>();
builder.Services.AddScoped<IReceiptRepository, EfReceiptRepository>();
builder.Services.AddScoped<ISettlementRepository, EfSettlementRepository>();
builder.Services.AddScoped<ISimulatedMessageRepository, EfSimulatedMessageRepository>();
builder.Services.AddScoped<IDemoDataRepository, EfDemoDataRepository>();
builder.Services.AddScoped<IOtpChallengeRepository, EfOtpChallengeRepository>();

// ---- Rail Proxy (contracts/rail-proxy.openapi.yaml) ----
builder.Services.Configure<RailProxyOptions>(builder.Configuration.GetSection(RailProxyOptions.Section));
builder.Services.AddHttpClient<IRailClient, HttpRailClient>((sp, http) =>
{
    var rail = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RailProxyOptions>>().Value;
    http.BaseAddress = new Uri(rail.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(rail.TimeoutSeconds);
});

// ---- Servicios ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ReliaOptions>(builder.Configuration.GetSection(ReliaOptions.Section));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.AddSingleton<DemoState>();
builder.Services.AddSingleton<IdempotencyStore>();
builder.Services.AddScoped<BalanceLedger>();
builder.Services.AddScoped<Messenger>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddScoped<IKeyLinkService, KeyLinkService>();
builder.Services.AddSingleton<PollThrottle>();
builder.Services.AddSingleton<AgentLocks>();
builder.Services.AddScoped<Relia.Api.Services.Operations.OperationTracker>();
builder.Services.AddScoped<Relia.Api.Services.Operations.OperationView>();
builder.Services.AddScoped<Relia.Api.Services.Operations.PaymentSweeper>();
builder.Services.AddScoped<Relia.Api.Services.Operations.SettlementTracker>();
builder.Services.AddScoped<Relia.Api.Services.Operations.ISweepable>(sp =>
    sp.GetRequiredService<Relia.Api.Services.Operations.SettlementTracker>());
builder.Services.AddHostedService<Relia.Api.Services.Operations.PaymentSweepService>();
builder.Services.AddScoped<DemoSeeder>();
builder.Services.AddScoped<IDemoService, DemoService>();
builder.Services.AddScoped<IHistoryReader, HistoryReader>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<ICashAdjustmentService, CashAdjustmentService>();
builder.Services.AddScoped<IOperationService, OperationService>();
builder.Services.AddScoped<IKeyService, KeyService>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<ISettlementService, SettlementService>();

// ---- Auth: JWT bearer con rol y agentId ----
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),
            RoleClaimType = "role",
            NameClaimType = "sub",
        };
    });
// La vigencia del token se valida con el TimeProvider inyectado (en pruebas, el reloj falso).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TimeProvider>((o, time) =>
        o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            return (notBefore is null || notBefore <= now.AddMinutes(5)) && (expires is null || expires > now);
        });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Relia API v1"));
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

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

public partial class Program
{
    /// <summary>"Email" → "email", "Request.RequestedAmount" → "request.requestedAmount"; los headers quedan igual.</summary>
    private static string JsonFieldName(string key) =>
        key.Contains('-')
            ? key
            : string.Join('.', key.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
