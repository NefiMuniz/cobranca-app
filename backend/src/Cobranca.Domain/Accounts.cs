namespace Cobranca.Domain;

/// <summary>Lifecycle set by the administrator. Whether access is valid today also depends on <see cref="Account.AccessUntil"/>.</summary>
public enum AccountStatus
{
    Pending,
    Active,
    Suspended,
    Rejected,
}

/// <summary>What the account can do right now (status + expiry date combined).</summary>
public enum AccessState
{
    Pending,
    Active,
    Expired,
    Suspended,
    Rejected,
}

public enum Plan
{
    /// <summary>Entry plan: the current feature set.</summary>
    Starter,
}

/// <summary>
/// A subscriber of the service. The account id is also the tenant id that partitions all of its billing data.
/// </summary>
public sealed record Account(
    string Id,
    string Email,
    string Name,
    string? Phone,
    string PasswordHash,
    AccountStatus Status,
    Plan Plan,
    DateTimeOffset CreatedAt,
    DateOnly? AccessUntil,
    string? AdminNote);

/// <summary>Audit trail of every access period granted (typically after a manual payment).</summary>
public sealed record AccessGrant(
    string Id,
    string AccountId,
    int Days,
    decimal? AmountPaid,
    string? Note,
    DateTimeOffset GrantedAt,
    DateOnly? PreviousUntil,
    DateOnly NewUntil);

public static class AccessPolicy
{
    /// <summary>Access is valid through the whole <see cref="Account.AccessUntil"/> day.</summary>
    public static AccessState StateOf(Account account, DateOnly today) => account.Status switch
    {
        AccountStatus.Pending => AccessState.Pending,
        AccountStatus.Suspended => AccessState.Suspended,
        AccountStatus.Rejected => AccessState.Rejected,
        _ => account.AccessUntil is { } until && until >= today ? AccessState.Active : AccessState.Expired,
    };

    /// <summary>
    /// New expiry after paying for <paramref name="days"/> more days. Days still left are kept:
    /// an account active until the 20th that buys 30 days goes to the 20th + 30.
    /// An expired (or never activated) account counts from today.
    /// </summary>
    public static DateOnly Extend(DateOnly? currentUntil, int days, DateOnly today)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var start = currentUntil is { } until && until >= today ? until : today;
        return start.AddDays(days);
    }

    /// <summary>Days left including today; negative once expired. Null when access was never granted.</summary>
    public static int? DaysLeft(DateOnly? accessUntil, DateOnly today) =>
        accessUntil is { } until ? until.DayNumber - today.DayNumber : null;
}

public sealed record PlanInfo(Plan Plan, string DisplayName, int? MaxClients);

/// <summary>Single source of truth for what each plan includes. New plans and limits are added here.</summary>
public static class PlanCatalog
{
    private static readonly Dictionary<Plan, PlanInfo> Plans = new()
    {
        [Plan.Starter] = new PlanInfo(Plan.Starter, "Entrada", MaxClients: null),
    };

    public static PlanInfo Get(Plan plan) => Plans[plan];
}
