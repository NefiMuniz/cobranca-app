using Cobranca.Domain;

namespace Cobranca.Api.Storage;

/// <summary>
/// Billing data, always scoped to one tenant (account). Every method takes the tenant id explicitly,
/// so no query can ever span two tenants.
/// </summary>
public interface IBillingRepository
{
    Task<IReadOnlyList<ClientLedger>> GetAllLedgersAsync(string tenantId, CancellationToken ct);

    Task<ClientLedger?> GetLedgerAsync(string tenantId, string clientId, CancellationToken ct);

    Task SaveClientAsync(string tenantId, Client client, CancellationToken ct);

    /// <summary>Deletes the client together with all of its charges and payments.</summary>
    Task DeleteClientAsync(string tenantId, string clientId, CancellationToken ct);

    Task SaveChargeAsync(string tenantId, Charge charge, CancellationToken ct);

    Task DeleteChargeAsync(string tenantId, string clientId, string chargeId, CancellationToken ct);

    Task SavePaymentAsync(string tenantId, Payment payment, CancellationToken ct);

    Task DeletePaymentAsync(string tenantId, string clientId, string paymentId, CancellationToken ct);
}

public interface IAccountRepository
{
    /// <summary>Creates the account if its e-mail is not taken yet. Returns false when it is.</summary>
    Task<bool> TryCreateAsync(Account account, CancellationToken ct);

    Task<Account?> GetByIdAsync(string accountId, CancellationToken ct);

    Task<Account?> GetByEmailAsync(string email, CancellationToken ct);

    Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct);

    /// <summary>Updates an existing account (status, expiry, notes). E-mail is immutable.</summary>
    Task UpdateAsync(Account account, CancellationToken ct);

    Task AddGrantAsync(AccessGrant grant, CancellationToken ct);

    Task<IReadOnlyList<AccessGrant>> ListGrantsAsync(string accountId, CancellationToken ct);
}
