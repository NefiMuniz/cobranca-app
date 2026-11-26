using System.Text;

namespace Cobranca.Api;

public sealed class AuthSettings
{
    public const string Section = "Auth";

    public string Username { get; set; } = "admin";

    /// <summary>PBKDF2 hash produced by <c>dotnet run -- hash-password &lt;password&gt;</c>.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>HMAC key used to sign tokens. Must be at least 32 bytes.</summary>
    public string JwtKey { get; set; } = "";

    public int TokenMinutes { get; set; } = 480;

    public int LoginAttemptsPerMinute { get; set; } = 5;

    /// <summary>Access requests accepted per IP per hour (public sign-up form).</summary>
    public int SignupRequestsPerHour { get; set; } = 5;

    /// <summary>New requests are refused while this many are waiting for approval (protects against spam floods).</summary>
    public int MaxPendingRequests { get; set; } = 50;

    /// <summary>Login fails closed (503) until both secrets are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PasswordHash) && Encoding.UTF8.GetByteCount(JwtKey) >= 32;
}

/// <summary>Request throttling. Defaults are far above what a person using the app generates.</summary>
public sealed class SecuritySettings
{
    public const string Section = "Security";

    /// <summary>Per signed-in account, all endpoints.</summary>
    public int AuthenticatedRequestsPerMinute { get; set; } = 300;

    /// <summary>Per IP, for requests without a valid token (bots, scanners, the login page itself).</summary>
    public int AnonymousRequestsPerMinute { get; set; } = 30;

    /// <summary>Largest request body accepted. The biggest real payload is a few hundred bytes.</summary>
    public long MaxRequestBodyBytes { get; set; } = 32 * 1024;
}

public sealed class StorageSettings
{
    public const string Section = "Storage";

    /// <summary><c>InMemory</c> for local development and tests, <c>DynamoDb</c> on AWS.</summary>
    public string Provider { get; set; } = "InMemory";

    public string TableName { get; set; } = "cobranca";

    /// <summary>Loads fictitious clients into the in-memory store. Never used with DynamoDB.</summary>
    public bool SeedDemoData { get; set; }
}

public sealed class AppSettings
{
    public const string Section = "App";

    /// <summary>Time zone that defines "today" for overdue calculations.</summary>
    public string TimeZone { get; set; } = "America/Sao_Paulo";
}

public sealed class CorsSettings
{
    public const string Section = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
