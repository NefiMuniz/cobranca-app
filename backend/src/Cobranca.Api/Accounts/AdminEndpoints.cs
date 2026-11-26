using Cobranca.Api.Storage;
using Cobranca.Domain;

namespace Cobranca.Api.Accounts;

/// <summary>Operator panel: review access requests and grant paid periods. Only the admin token can call these.</summary>
public static class AdminEndpoints
{
    public const string AdminPolicy = "admin";
    private const int MaxGrantDays = 3650;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AdminPolicy);

        admin.MapGet("/accounts", ListAccounts);
        admin.MapGet("/accounts/{accountId}", GetAccount);
        admin.MapPost("/accounts/{accountId}/grant", Grant);
        admin.MapPost("/accounts/{accountId}/suspend", Suspend);
        admin.MapPost("/accounts/{accountId}/reactivate", Reactivate);
        admin.MapPost("/accounts/{accountId}/reject", Reject);

        return app;
    }

    private static async Task<IResult> ListAccounts(IAccountRepository accounts, Clock clock, CancellationToken ct)
    {
        var today = clock.Today;
        var views = (await accounts.ListAsync(ct))
            .Select(a => AccountView.From(a, today))
            .OrderBy(v => v.State == AccessState.Pending ? 0 : 1)
            .ThenBy(v => v.DaysLeft ?? int.MaxValue)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Results.Ok(views);
    }

    private static async Task<IResult> GetAccount(string accountId, IAccountRepository accounts, Clock clock, CancellationToken ct)
    {
        var account = await FindAsync(accounts, accountId, ct);
        return account is null ? Results.NotFound() : Results.Ok(await DetailAsync(accounts, account, clock.Today, ct));
    }

    /// <summary>Approves a request or renews an account for N paid days (counted from the current expiry when still active).</summary>
    private static async Task<IResult> Grant(
        string accountId, GrantRequest request, IAccountRepository accounts, Clock clock, CancellationToken ct)
    {
        var account = await FindAsync(accounts, accountId, ct);
        if (account is null)
        {
            return Results.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        if (request.Days is not { } days || days < 1 || days > MaxGrantDays)
        {
            errors["days"] = [$"Informe de 1 a {MaxGrantDays} dias."];
        }

        if (request.AmountPaid is { } amount && (amount < 0m || amount > 1_000_000m || decimal.Round(amount, 2) != amount))
        {
            errors["amountPaid"] = ["Valor pago inválido."];
        }

        if (request.Note is { Length: > 500 })
        {
            errors["note"] = ["Use no máximo 500 caracteres."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var today = clock.Today;
        var grantedDays = request.Days!.Value;
        var newUntil = AccessPolicy.Extend(account.AccessUntil, grantedDays, today);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        await accounts.AddGrantAsync(new AccessGrant(
            Guid.NewGuid().ToString("N"),
            account.Id,
            grantedDays,
            request.AmountPaid,
            note,
            DateTimeOffset.UtcNow,
            account.AccessUntil,
            newUntil), ct);

        var updated = account with { Status = AccountStatus.Active, AccessUntil = newUntil };
        await accounts.UpdateAsync(updated, ct);
        return Results.Ok(await DetailAsync(accounts, updated, today, ct));
    }

    private static Task<IResult> Suspend(
        string accountId, NoteRequest request, IAccountRepository accounts, Clock clock, CancellationToken ct) =>
        ChangeStatusAsync(accountId, request, AccountStatus.Suspended, [AccountStatus.Active], accounts, clock, ct);

    private static Task<IResult> Reactivate(
        string accountId, NoteRequest request, IAccountRepository accounts, Clock clock, CancellationToken ct) =>
        ChangeStatusAsync(accountId, request, AccountStatus.Active, [AccountStatus.Suspended], accounts, clock, ct);

    private static Task<IResult> Reject(
        string accountId, NoteRequest request, IAccountRepository accounts, Clock clock, CancellationToken ct) =>
        ChangeStatusAsync(accountId, request, AccountStatus.Rejected, [AccountStatus.Pending], accounts, clock, ct);

    private static async Task<IResult> ChangeStatusAsync(
        string accountId,
        NoteRequest request,
        AccountStatus target,
        AccountStatus[] allowedFrom,
        IAccountRepository accounts,
        Clock clock,
        CancellationToken ct)
    {
        var account = await FindAsync(accounts, accountId, ct);
        if (account is null)
        {
            return Results.NotFound();
        }

        if (!allowedFrom.Contains(account.Status))
        {
            return Results.Problem(
                title: "Esta ação não se aplica à situação atual da conta.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? account.AdminNote : request.Note.Trim()[..Math.Min(500, request.Note.Trim().Length)];
        var updated = account with { Status = target, AdminNote = note };
        await accounts.UpdateAsync(updated, ct);
        return Results.Ok(await DetailAsync(accounts, updated, clock.Today, ct));
    }

    private static Task<Account?> FindAsync(IAccountRepository accounts, string accountId, CancellationToken ct) =>
        Tenancy.IsValidId(accountId) ? accounts.GetByIdAsync(accountId, ct) : Task.FromResult<Account?>(null);

    private static async Task<AccountDetail> DetailAsync(IAccountRepository accounts, Account account, DateOnly today, CancellationToken ct)
    {
        var grants = (await accounts.ListGrantsAsync(account.Id, ct)).OrderByDescending(g => g.GrantedAt).ToList();
        return new AccountDetail(AccountView.From(account, today), grants);
    }
}
