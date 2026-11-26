using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cobranca.Api.Accounts;
using Cobranca.Api.Storage;
using Cobranca.Domain;
using Microsoft.Extensions.Options;

namespace Cobranca.Api.Auth;

public sealed record LoginRequest(string? Username, string? Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Role, string Name);

public static partial class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";
    public const string SignupRateLimitPolicy = "signup";

    // Verified when the e-mail does not exist, so a miss costs the same time as a wrong password.
    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString("N")));

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        app.MapPost("/api/signup", Signup).AllowAnonymous().RequireRateLimiting(SignupRateLimitPolicy);
        app.MapGet("/api/me", Me).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// Two kinds of login: the operator (username configured at deploy) and subscribers (e-mail).
    /// Subscribers only get a token while their access is active.
    /// </summary>
    private static async Task<IResult> Login(
        LoginRequest request, IOptions<AuthSettings> options, IAccountRepository accounts, TokenService tokens, Clock clock,
        CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return Results.Problem(
                title: "Autenticação não configurada no servidor.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var identifier = request.Username?.Trim() ?? "";
        var password = request.Password ?? "";

        if (FixedTimeEquals(identifier, settings.Username))
        {
            if (!PasswordHasher.Verify(password, settings.PasswordHash))
            {
                return InvalidCredentials();
            }

            var (adminToken, adminExpires) = tokens.Create("admin", Tenancy.AdminTenantId, Tenancy.AdminRole);
            return Results.Ok(new LoginResponse(adminToken, adminExpires, Tenancy.AdminRole, "Administrador"));
        }

        var account = await accounts.GetByEmailAsync(NormalizeEmail(identifier), ct);
        var passwordMatches = PasswordHasher.Verify(password, account?.PasswordHash ?? DummyHash.Value);
        if (account is null || !passwordMatches)
        {
            return InvalidCredentials();
        }

        // The access state is only revealed after the password is proven, so it cannot be used to probe e-mails.
        var state = AccessPolicy.StateOf(account, clock.Today);
        if (state != AccessState.Active)
        {
            return AccessProblems.For(state, account.AccessUntil);
        }

        var (token, expires) = tokens.Create(account.Id, account.Id, Tenancy.UserRole);
        return Results.Ok(new LoginResponse(token, expires, Tenancy.UserRole, account.Name));
    }

    /// <summary>
    /// Public access request. The account is created as Pending and cannot sign in until approved.
    /// The response is identical whether or not the e-mail already exists (no account enumeration).
    /// </summary>
    private static async Task<IResult> Signup(
        SignupRequest request, IAccountRepository accounts, IOptions<AuthSettings> options, ILogger<SignupRequest> logger,
        CancellationToken ct)
    {
        var accepted = Results.Accepted(value: new
        {
            message = "Solicitação recebida. Você será avisado pelo WhatsApp quando o acesso for liberado.",
        });

        // Honeypot: the field is hidden in the form, so only bots fill it in.
        if (!string.IsNullOrEmpty(request.Website))
        {
            return accepted;
        }

        var errors = ValidateSignup(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var pending = (await accounts.ListAsync(ct)).Count(a => a.Status == AccountStatus.Pending);
        if (pending >= options.Value.MaxPendingRequests)
        {
            logger.LogWarning("Sign-up refused: {Pending} requests already pending.", pending);
            return Results.Problem(
                title: "No momento não estamos recebendo novas solicitações. Tente novamente mais tarde.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var account = new Account(
            Guid.NewGuid().ToString("N"),
            NormalizeEmail(request.Email!),
            request.Name!.Trim(),
            request.Phone!.Trim(),
            PasswordHasher.Hash(request.Password!),
            AccountStatus.Pending,
            Plan.Starter,
            DateTimeOffset.UtcNow,
            AccessUntil: null,
            AdminNote: null);

        if (!await accounts.TryCreateAsync(account, ct))
        {
            logger.LogInformation("Sign-up for an e-mail that already has an account was ignored.");
        }

        return accepted;
    }

    private static async Task<IResult> Me(ClaimsPrincipal user, IAccountRepository accounts, Clock clock, CancellationToken ct)
    {
        if (user.IsAdmin())
        {
            return Results.Ok(new MeResponse(Tenancy.AdminRole, "Administrador", null, "Administrador", AccessState.Active, null, null));
        }

        var account = await accounts.GetByIdAsync(user.TenantId(), ct);
        if (account is null)
        {
            return Results.Unauthorized();
        }

        var today = clock.Today;
        return Results.Ok(new MeResponse(
            Tenancy.UserRole,
            account.Name,
            account.Email,
            PlanCatalog.Get(account.Plan).DisplayName,
            AccessPolicy.StateOf(account, today),
            account.AccessUntil,
            AccessPolicy.DaysLeft(account.AccessUntil, today)));
    }

    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static Dictionary<string, string[]> ValidateSignup(SignupRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            errors["name"] = ["Informe seu nome ou o nome do negócio (até 120 caracteres)."];
        }

        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 200 || !EmailPattern().IsMatch(request.Email.Trim()))
        {
            errors["email"] = ["Informe um e-mail válido."];
        }

        var phoneDigits = request.Phone is null ? 0 : request.Phone.Count(char.IsDigit);
        if (phoneDigits is < 10 or > 15 || request.Phone!.Length > 30)
        {
            errors["phone"] = ["Informe um WhatsApp com DDD."];
        }

        if (request.Password is null || request.Password.Length < 8 || request.Password.Length > 128)
        {
            errors["password"] = ["A senha precisa ter de 8 a 128 caracteres."];
        }

        return errors;
    }

    private static IResult InvalidCredentials() => Results.Problem(
        title: "E-mail ou senha inválidos.",
        statusCode: StatusCodes.Status401Unauthorized);

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
