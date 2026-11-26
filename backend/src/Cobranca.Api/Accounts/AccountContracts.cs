using Cobranca.Domain;

namespace Cobranca.Api.Accounts;

public sealed record SignupRequest(string? Name, string? Email, string? Phone, string? Password, string? Website);

public sealed record GrantRequest(int? Days, decimal? AmountPaid, string? Note);

public sealed record NoteRequest(string? Note);

/// <summary>Account as shown to the administrator. Never includes the password hash.</summary>
public sealed record AccountView(
    string Id,
    string Name,
    string Email,
    string? Phone,
    AccountStatus Status,
    AccessState State,
    string Plan,
    DateTimeOffset CreatedAt,
    DateOnly? AccessUntil,
    int? DaysLeft,
    string? AdminNote)
{
    public static AccountView From(Account account, DateOnly today) => new(
        account.Id,
        account.Name,
        account.Email,
        account.Phone,
        account.Status,
        AccessPolicy.StateOf(account, today),
        PlanCatalog.Get(account.Plan).DisplayName,
        account.CreatedAt,
        account.AccessUntil,
        AccessPolicy.DaysLeft(account.AccessUntil, today),
        account.AdminNote);
}

public sealed record AccountDetail(AccountView Account, IReadOnlyList<AccessGrant> Grants);

/// <summary>Who is signed in and until when, used by the app to show expiry warnings.</summary>
public sealed record MeResponse(
    string Role,
    string Name,
    string? Email,
    string Plan,
    AccessState State,
    DateOnly? AccessUntil,
    int? DaysLeft);
