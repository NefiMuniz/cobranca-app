using System.Security.Claims;
using Cobranca.Api.Accounts;
using Cobranca.Api.Storage;
using Cobranca.Domain;

namespace Cobranca.Api.Billing;

/// <summary>
/// Billing endpoints. Every handler reads the tenant from the signed token (<see cref="Tenancy.TenantId"/>)
/// and passes it to the repository, so a request can only reach its own account's data.
/// </summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api")
            .RequireAuthorization()
            .AddEndpointFilter<ActiveAccountFilter>();

        api.MapGet("/dashboard", GetDashboard);

        api.MapGet("/clients", ListClients);
        api.MapPost("/clients", CreateClient);
        api.MapGet("/clients/{clientId}", GetClient);
        api.MapPut("/clients/{clientId}", UpdateClient);
        api.MapDelete("/clients/{clientId}", DeleteClient);

        api.MapPost("/clients/{clientId}/charges", CreateCharge);
        api.MapPut("/clients/{clientId}/charges/{chargeId}", UpdateCharge);
        api.MapDelete("/clients/{clientId}/charges/{chargeId}", DeleteCharge);

        api.MapPost("/clients/{clientId}/payments", CreatePayment);
        api.MapDelete("/clients/{clientId}/payments/{paymentId}", DeletePayment);

        return app;
    }

    private static async Task<IResult> GetDashboard(
        ClaimsPrincipal user, IBillingRepository repo, Clock clock, CancellationToken ct)
    {
        var today = clock.Today;
        var ledgers = await repo.GetAllLedgersAsync(user.TenantId(), ct);
        var summaries = ledgers
            .Select(ledger => Summarize(ledger, today))
            .OrderByDescending(s => s.OverdueAmount)
            .ThenByDescending(s => s.Outstanding)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var receivedThisMonth = ledgers
            .SelectMany(l => l.Payments)
            .Where(p => p.PaidOn.Year == today.Year && p.PaidOn.Month == today.Month)
            .Sum(p => p.Amount);

        return Results.Ok(new DashboardResponse(
            today,
            summaries.Sum(s => s.Outstanding),
            summaries.Sum(s => s.OverdueAmount),
            summaries.Sum(s => s.OverdueCharges),
            receivedThisMonth,
            summaries));
    }

    private static async Task<IResult> ListClients(
        ClaimsPrincipal user, IBillingRepository repo, Clock clock, CancellationToken ct)
    {
        var today = clock.Today;
        var ledgers = await repo.GetAllLedgersAsync(user.TenantId(), ct);
        return Results.Ok(ledgers
            .Select(ledger => Summarize(ledger, today))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<IResult> CreateClient(
        ClaimsPrincipal user, ClientInput input, IBillingRepository repo, IAccountRepository accounts, CancellationToken ct)
    {
        var errors = Validation.ForClient(input);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var tenantId = user.TenantId();
        if (!user.IsAdmin() && await accounts.GetByIdAsync(tenantId, ct) is { } account
            && PlanCatalog.Get(account.Plan).MaxClients is { } maxClients
            && (await repo.GetAllLedgersAsync(tenantId, ct)).Count >= maxClients)
        {
            return Results.Problem(
                title: $"Seu plano permite até {maxClients} clientes.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = "plan_limit" });
        }

        var client = new Client(
            NewId(),
            input.Name!.Trim(),
            Validation.Clean(input.Phone),
            Validation.Clean(input.Notes),
            DateTimeOffset.UtcNow);
        await repo.SaveClientAsync(tenantId, client, ct);
        return Results.Created($"/api/clients/{client.Id}", client);
    }

    private static async Task<IResult> GetClient(
        ClaimsPrincipal user, string clientId, IBillingRepository repo, Clock clock, CancellationToken ct)
    {
        var ledger = await FindLedgerAsync(user, clientId, repo, ct);
        if (ledger is null)
        {
            return Results.NotFound();
        }

        var statement = BalanceCalculator.Calculate(ledger.Charges, ledger.Payments, clock.Today);
        var payments = ledger.Payments.OrderByDescending(p => p.PaidOn).ToList();
        return Results.Ok(new ClientDetail(ledger.Client, statement, payments));
    }

    private static async Task<IResult> UpdateClient(
        ClaimsPrincipal user, string clientId, ClientInput input, IBillingRepository repo, CancellationToken ct)
    {
        var ledger = await FindLedgerAsync(user, clientId, repo, ct);
        if (ledger is null)
        {
            return Results.NotFound();
        }

        var errors = Validation.ForClient(input);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var client = ledger.Client with
        {
            Name = input.Name!.Trim(),
            Phone = Validation.Clean(input.Phone),
            Notes = Validation.Clean(input.Notes),
        };
        await repo.SaveClientAsync(user.TenantId(), client, ct);
        return Results.Ok(client);
    }

    private static async Task<IResult> DeleteClient(
        ClaimsPrincipal user, string clientId, IBillingRepository repo, CancellationToken ct)
    {
        if (await FindLedgerAsync(user, clientId, repo, ct) is null)
        {
            return Results.NotFound();
        }

        await repo.DeleteClientAsync(user.TenantId(), clientId, ct);
        return Results.NoContent();
    }

    /// <summary>Creates one charge, or several when the input asks for repetition or installments.</summary>
    private static async Task<IResult> CreateCharge(
        ClaimsPrincipal user, string clientId, ChargeInput input, IBillingRepository repo, CancellationToken ct)
    {
        if (await FindLedgerAsync(user, clientId, repo, ct) is null)
        {
            return Results.NotFound();
        }

        var errors = Validation.ForCharge(input, allowPlan: true);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var plan = new ChargePlan(
            clientId,
            input.ServiceName!.Trim(),
            Validation.Clean(input.Description),
            input.Reference!.Value,
            input.DueDate!.Value,
            input.Amount!.Value,
            input.Kind ?? ChargeKind.OneOff,
            input.RepeatUntil,
            input.Installments);

        var tenantId = user.TenantId();
        var charges = ChargePlanner.Expand(plan, NewId);
        foreach (var charge in charges)
        {
            await repo.SaveChargeAsync(tenantId, charge, ct);
        }

        return Results.Created($"/api/clients/{clientId}/charges/{charges[0].Id}", charges);
    }

    private static async Task<IResult> UpdateCharge(
        ClaimsPrincipal user, string clientId, string chargeId, ChargeInput input, IBillingRepository repo, CancellationToken ct)
    {
        var ledger = await FindLedgerAsync(user, clientId, repo, ct);
        if (ledger is null || ledger.Charges.All(c => c.Id != chargeId))
        {
            return Results.NotFound();
        }

        var errors = Validation.ForCharge(input, allowPlan: false);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var charge = ToCharge(chargeId, clientId, input);
        await repo.SaveChargeAsync(user.TenantId(), charge, ct);
        return Results.Ok(charge);
    }

    private static async Task<IResult> DeleteCharge(
        ClaimsPrincipal user, string clientId, string chargeId, IBillingRepository repo, CancellationToken ct)
    {
        var ledger = await FindLedgerAsync(user, clientId, repo, ct);
        if (ledger is null || ledger.Charges.All(c => c.Id != chargeId))
        {
            return Results.NotFound();
        }

        // Payments linked to this charge are kept; the calculator then applies them to other open charges.
        await repo.DeleteChargeAsync(user.TenantId(), clientId, chargeId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreatePayment(
        ClaimsPrincipal user, string clientId, PaymentInput input, IBillingRepository repo, CancellationToken ct)
    {
        var ledger = await FindLedgerAsync(user, clientId, repo, ct);
        if (ledger is null)
        {
            return Results.NotFound();
        }

        var errors = Validation.ForPayment(input);
        var chargeId = Validation.Clean(input.ChargeId);
        if (chargeId is not null && ledger.Charges.All(c => c.Id != chargeId))
        {
            errors["chargeId"] = ["A cobrança informada não pertence a este cliente."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var payment = new Payment(
            NewId(),
            clientId,
            input.Amount!.Value,
            input.PaidOn!.Value,
            chargeId,
            Validation.Clean(input.Note));
        await repo.SavePaymentAsync(user.TenantId(), payment, ct);
        return Results.Created($"/api/clients/{clientId}/payments/{payment.Id}", payment);
    }

    private static async Task<IResult> DeletePayment(
        ClaimsPrincipal user, string clientId, string paymentId, IBillingRepository repo, CancellationToken ct)
    {
        var ledger = await FindLedgerAsync(user, clientId, repo, ct);
        if (ledger is null || ledger.Payments.All(p => p.Id != paymentId))
        {
            return Results.NotFound();
        }

        await repo.DeletePaymentAsync(user.TenantId(), clientId, paymentId, ct);
        return Results.NoContent();
    }

    /// <summary>Loads a client of the caller's tenant. Malformed ids are treated as not found.</summary>
    private static Task<ClientLedger?> FindLedgerAsync(
        ClaimsPrincipal user, string clientId, IBillingRepository repo, CancellationToken ct) =>
        Tenancy.IsValidId(clientId)
            ? repo.GetLedgerAsync(user.TenantId(), clientId, ct)
            : Task.FromResult<ClientLedger?>(null);

    internal static ClientSummary Summarize(ClientLedger ledger, DateOnly today)
    {
        var statement = BalanceCalculator.Calculate(ledger.Charges, ledger.Payments, today);
        var open = statement.Lines.Where(l => l.Remaining > 0m).ToList();
        DateOnly? nextDue = open.Count > 0 ? open.Min(l => l.Charge.DueDate) : null;

        return new ClientSummary(
            ledger.Client.Id,
            ledger.Client.Name,
            ledger.Client.Phone,
            statement.Outstanding,
            statement.OverdueAmount,
            statement.Credit,
            statement.OpenCharges,
            statement.OverdueCharges,
            nextDue);
    }

    private static Charge ToCharge(string id, string clientId, ChargeInput input)
    {
        var reference = input.Reference!.Value;
        return new Charge(
            id,
            clientId,
            input.ServiceName!.Trim(),
            Validation.Clean(input.Description),
            new DateOnly(reference.Year, reference.Month, 1),
            input.DueDate!.Value,
            input.Amount!.Value,
            input.Kind ?? ChargeKind.OneOff);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
