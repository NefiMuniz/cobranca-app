namespace Cobranca.Domain;

/// <summary>Whether a charge is a one-off service or a monthly fee.</summary>
public enum ChargeKind
{
    OneOff,
    Recurring,
}

public enum ChargeStatus
{
    Open,
    Partial,
    Paid,
}

public sealed record Client(
    string Id,
    string Name,
    string? Phone,
    string? Notes,
    DateTimeOffset CreatedAt);

/// <summary>
/// A service billed to a client. The service is free text (name + optional description),
/// not a fixed catalog item, because each job is priced individually.
/// </summary>
/// <param name="Reference">Competence month (always stored as the 1st day of the month).</param>
/// <param name="DueDate">When the client is expected to pay.</param>
public sealed record Charge(
    string Id,
    string ClientId,
    string ServiceName,
    string? Description,
    DateOnly Reference,
    DateOnly DueDate,
    decimal Amount,
    ChargeKind Kind);

/// <summary>
/// Money received from a client. <see cref="ChargeId"/> is optional: when empty the payment
/// is applied to the client's oldest open charges, which mirrors how partial payments work in practice.
/// </summary>
public sealed record Payment(
    string Id,
    string ClientId,
    decimal Amount,
    DateOnly PaidOn,
    string? ChargeId,
    string? Note);

public sealed record ClientLedger(
    Client Client,
    IReadOnlyList<Charge> Charges,
    IReadOnlyList<Payment> Payments);
