using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Cobranca.Api.Storage;

/// <summary>
/// Tenant resolution. The tenant always comes from the signed token, never from the URL or the request body.
/// </summary>
public static partial class Tenancy
{
    /// <summary>Tenant of the operator account configured at deploy time.</summary>
    public const string AdminTenantId = "admin";

    public const string TenantClaim = "tid";
    public const string RoleClaim = "role";
    public const string AdminRole = "admin";
    public const string UserRole = "user";

    public static string TenantId(this ClaimsPrincipal user) =>
        user.FindFirst(TenantClaim)?.Value
        ?? throw new InvalidOperationException("Authenticated request without a tenant claim.");

    public static bool IsAdmin(this ClaimsPrincipal user) =>
        user.FindFirst(RoleClaim)?.Value == AdminRole;

    /// <summary>
    /// Ids come from the URL. Only server-generated shapes are accepted, so an id can never smuggle
    /// key separators (#) into a storage key.
    /// </summary>
    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex IdPattern();
}
