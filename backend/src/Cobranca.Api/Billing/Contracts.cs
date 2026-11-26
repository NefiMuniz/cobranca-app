using Cobranca.Domain;

namespace Cobranca.Api.Billing;

// Inputs use nullable members so missing fields become validation errors instead of binding failures.
public sealed record ClientInput(string? Name, string? Phone, string? Notes);

public sealed record ChargeInput(
    string? ServiceName,
    string? Description,
    DateOnly? Reference,
    DateOnly? DueDate,
    decimal? Amount,
    ChargeKind? Kind,
    // Creation only. RepeatUntil (monthly fees): last competence month to generate.
    // Installments (one-off services): split Amount into this many monthly charges.
    DateOnly? RepeatUntil = null,
    int? Installments = null);

public sealed record PaymentInput(decimal? Amount, DateOnly? PaidOn, string? ChargeId, string? Note);

public sealed record ClientSummary(
    string Id,
    string Name,
    string? Phone,
    decimal Outstanding,
    decimal OverdueAmount,
    decimal Credit,
    int OpenCharges,
    int OverdueCharges,
    DateOnly? NextDueDate);

public sealed record ClientDetail(Client Client, ClientStatement Statement, IReadOnlyList<Payment> Payments);

public sealed record DashboardResponse(
    DateOnly Today,
    decimal TotalOutstanding,
    decimal TotalOverdue,
    int OverdueCharges,
    decimal ReceivedThisMonth,
    IReadOnlyList<ClientSummary> Clients);
