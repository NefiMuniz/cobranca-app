using Cobranca.Domain;
using Microsoft.Extensions.Options;

namespace Cobranca.Api.Storage;

public sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Account> _accounts = [];
    private readonly Dictionary<string, string> _idsByEmail = [];
    private readonly List<AccessGrant> _grants = [];

    public InMemoryAccountRepository(IOptions<StorageSettings> settings, TimeProvider time)
    {
        if (settings.Value.SeedDemoData)
        {
            foreach (var account in DemoData.CreateAccounts(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)))
            {
                _accounts[account.Id] = account;
                _idsByEmail[account.Email] = account.Id;
            }
        }
    }

    public Task<bool> TryCreateAsync(Account account, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_idsByEmail.TryAdd(account.Email, account.Id))
            {
                return Task.FromResult(false);
            }

            _accounts[account.Id] = account;
            return Task.FromResult(true);
        }
    }

    public Task<Account?> GetByIdAsync(string accountId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_accounts.GetValueOrDefault(accountId));
        }
    }

    public Task<Account?> GetByEmailAsync(string email, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_idsByEmail.TryGetValue(email, out var id) ? _accounts[id] : null);
        }
    }

    public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            IReadOnlyList<Account> accounts = _accounts.Values.ToList();
            return Task.FromResult(accounts);
        }
    }

    public Task UpdateAsync(Account account, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_accounts.ContainsKey(account.Id))
            {
                _accounts[account.Id] = account;
            }
        }

        return Task.CompletedTask;
    }

    public Task AddGrantAsync(AccessGrant grant, CancellationToken ct)
    {
        lock (_gate)
        {
            _grants.Add(grant);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccessGrant>> ListGrantsAsync(string accountId, CancellationToken ct)
    {
        lock (_gate)
        {
            IReadOnlyList<AccessGrant> grants = _grants.Where(g => g.AccountId == accountId).ToList();
            return Task.FromResult(grants);
        }
    }
}
