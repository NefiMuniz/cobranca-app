using System.Security.Claims;
using System.Text;
using Cobranca.Api.Storage;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cobranca.Api.Auth;

public sealed class TokenService(IOptions<AuthSettings> options)
{
    // "v2": tokens issued before multi-tenancy (without tenant/role claims) are rejected as invalid.
    public const string Issuer = "cobranca-api-v2";
    public const string Audience = "cobranca-app";

    public (string Token, DateTimeOffset ExpiresAt) Create(string subject, string tenantId, string role)
    {
        var settings = options.Value;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(settings.TokenMinutes);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(Tenancy.TenantClaim, tenantId),
                new Claim(Tenancy.RoleClaim, role),
            }),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateKey(settings.JwtKey), SecurityAlgorithms.HmacSha256),
        });

        return (token, expires);
    }

    public static TokenValidationParameters CreateValidationParameters(AuthSettings settings)
    {
        // With no key configured, validate against a random key so every token is rejected (fail closed).
        var key = settings.IsConfigured
            ? settings.JwtKey
            : Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        return new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = CreateKey(key),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    private static SymmetricSecurityKey CreateKey(string key) => new(Encoding.UTF8.GetBytes(key));
}
