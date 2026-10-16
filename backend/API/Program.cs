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
                .SelectMany(e => e.Value!.Errors.Select(err => new { field = e.Key, message = err.ErrorMessage }))
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

// ---- Servicios ----
builder.Services.AddSingleton(TimeProvider.System);
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

public partial class Program;
