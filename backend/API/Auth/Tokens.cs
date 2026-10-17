using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Relia.Api.Configuration;

namespace Relia.Api.Auth;

/// <summary>Hash de contraseñas de agents y admins (PBKDF2 de ASP.NET Core Identity).</summary>
public static class PasswordHashing
{
    private static readonly PasswordHasher<object> Hasher = new();

    public static string Hash(string password) => Hasher.HashPassword(null!, password);

    public static bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}

/// <summary>Emite el JWT de POST /auth/login con sub, role y, para el corresponsal, agentId.</summary>
public class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;

    public int ExpiresInSeconds => _options.ExpiresInSeconds;

    public string Issue(Guid userId, string role, Guid? agentId)
    {
        var claims = new List<Claim>
        {
            new(ReliaClaims.UserId, userId.ToString()),
            new(ReliaClaims.Role, role),
        };
        if (agentId is not null) claims.Add(new Claim(ReliaClaims.AgentId, agentId.Value.ToString()));

        var now = clock.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(_options.ExpiresInSeconds),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
