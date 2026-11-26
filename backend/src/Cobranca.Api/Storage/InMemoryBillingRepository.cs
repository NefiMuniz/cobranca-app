using Cobranca.Domain;
using Microsoft.Extensions.Options;

namespace Cobranca.Api.Storage;

/// <summary>Thread-safe in-memory store for local development and integration tests.</summary>
public sealed class InMemoryBillingRepository : IBillingRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TenantData> _tenants = [];

    public InMemoryBillingRepository(IOptions<StorageSettings> settings, TimeProvider time)
    {
        if (!settings.Value.SeedDemoData)
        {
            return;
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var (clients, charges, payments) = DemoData.Create(today);
        var tenant = Tenant(Tenancy.AdminTenantId);
        foreach (var client in clients)
        {
            tenant.Clients[client.Id] = client;
        }

        foreach (var charge in charges)
        {
            tenant.ChargesOf(charge.ClientId)[charge.Id] = charge;
        }

        foreach (var payment in payments)
        {
            tenant.PaymentsOf(payment.ClientId)[payment.Id] = payment;
        }
    }

    public Task<IReadOnlyList<ClientLedger>> GetAllLedgersAsync(string tenantId, CancellationToken ct)
    {
        lock (_gate)
        {
            var tenant = Tenant(tenantId);
            IReadOnlyList<ClientLedger> ledgers = tenant.Clients.Keys.Select(tenant.BuildLedger).ToList();
            return Task.FromResult(ledgers);
        }
    }

    public Task<ClientLedger?> GetLedgerAsync(string tenantId, string clientId, CancellationToken ct)
    {
        lock (_gate)
        {
            var tenant = Tenant(tenantId);
            return Task.FromResult(tenant.Clients.ContainsKey(clientId) ? tenant.BuildLedger(clientId) : null);
        }
    }

    public Task SaveClientAsync(string tenantId, Client client, CancellationToken ct)
    {
        lock (_gate)
        {
            Tenant(tenantId).Clients[client.Id] = client;
        }

        return Task.CompletedTask;
    }

    public Task DeleteClientAsync(string tenantId, string clientId, CancellationToken ct)
    {
        lock (_gate)
        {
            var tenant = Tenant(tenantId);
            tenant.Clients.Remove(clientId);
            tenant.Charges.Remove(clientId);
            tenant.Payments.Remove(clientId);
        }

        return Task.CompletedTask;
    }

    public Task SaveChargeAsync(string tenantId, Charge charge, CancellationToken ct)
    {
        lock (_gate)
        {
            Tenant(tenantId).ChargesOf(charge.ClientId)[charge.Id] = charge;
        }

        return Task.CompletedTask;
    }

    public Task DeleteChargeAsync(string tenantId, string clientId, string chargeId, CancellationToken ct)
    {
        lock (_gate)
        {
            Tenant(tenantId).ChargesOf(clientId).Remove(chargeId);
        }

        return Task.CompletedTask;
    }

    public Task SavePaymentAsync(string tenantId, Payment payment, CancellationToken ct)
    {
        lock (_gate)
        {
            Tenant(tenantId).PaymentsOf(payment.ClientId)[payment.Id] = payment;
        }

        return Task.CompletedTask;
    }

    public Task DeletePaymentAsync(string tenantId, string clientId, string paymentId, CancellationToken ct)
    {
        lock (_gate)
        {
            Tenant(tenantId).PaymentsOf(clientId).Remove(paymentId);
        }

        return Task.CompletedTask;
    }

    // Caller must hold _gate.
    private TenantData Tenant(string tenantId)
    {
        if (!_tenants.TryGetValue(tenantId, out var tenant))
        {
            tenant = new TenantData();
            _tenants[tenantId] = tenant;
        }

        return tenant;
    }

    private sealed class TenantData
    {
        public Dictionary<string, Client> Clients { get; } = [];

        public Dictionary<string, Dictionary<string, Charge>> Charges { get; } = [];

        public Dictionary<string, Dictionary<string, Payment>> Payments { get; } = [];

        public Dictionary<string, Charge> ChargesOf(string clientId) => GetOrAdd(Charges, clientId);

        public Dictionary<string, Payment> PaymentsOf(string clientId) => GetOrAdd(Payments, clientId);

        // Copies the collections so callers never see later mutations.
        public ClientLedger BuildLedger(string clientId) => new(
            Clients[clientId],
            Charges.TryGetValue(clientId, out var charges) ? charges.Values.ToList() : [],
            Payments.TryGetValue(clientId, out var payments) ? payments.Values.ToList() : []);

        private static Dictionary<string, T> GetOrAdd<T>(Dictionary<string, Dictionary<string, T>> map, string key)
        {
            if (!map.TryGetValue(key, out var inner))
            {
                inner = [];
                map[key] = inner;
            }

            return inner;
        }
    }
}
