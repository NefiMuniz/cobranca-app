using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Cobranca.Domain;
using static Cobranca.Api.Storage.DynamoMapping;

namespace Cobranca.Api.Storage;

/// <summary>
/// Accounts live in the same table, in their own partitions:
///   PK = ACCOUNTS          SK = ACCOUNT#{id}                 account profile
///   PK = ACCOUNTS          SK = ACCOUNT#{id}#GRANT#{grantId}  access grants (audit trail)
///   PK = EMAIL#{email}     SK = #EMAIL                       uniqueness guard, points to the account id
/// The account and its e-mail guard are written in one transaction with attribute_not_exists,
/// so two sign-ups with the same e-mail can never both succeed.
/// A single ACCOUNTS partition keeps the admin listing to one Query; it comfortably holds thousands of accounts.
/// </summary>
public sealed class DynamoDbAccountRepository(IAmazonDynamoDB db, string tableName) : IAccountRepository
{
    private const string AccountsPk = "ACCOUNTS";

    public async Task<bool> TryCreateAsync(Account account, CancellationToken ct)
    {
        var emailGuard = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(EmailPk(account.Email)),
            ["SK"] = S("#EMAIL"),
            ["AccountId"] = S(account.Id),
        };

        try
        {
            await db.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new TransactWriteItem
                    {
                        Put = new Put { TableName = tableName, Item = emailGuard, ConditionExpression = "attribute_not_exists(PK)" },
                    },
                    new TransactWriteItem
                    {
                        Put = new Put { TableName = tableName, Item = ToItem(account), ConditionExpression = "attribute_not_exists(PK)" },
                    },
                ],
            }, ct);
            return true;
        }
        catch (TransactionCanceledException)
        {
            return false;
        }
    }

    public async Task<Account?> GetByIdAsync(string accountId, CancellationToken ct)
    {
        var response = await db.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue> { ["PK"] = S(AccountsPk), ["SK"] = S(AccountSk(accountId)) },
            ConsistentRead = true,
        }, ct);

        return response.Item is { Count: > 0 } item ? ToAccount(item) : null;
    }

    public async Task<Account?> GetByEmailAsync(string email, CancellationToken ct)
    {
        var response = await db.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue> { ["PK"] = S(EmailPk(email)), ["SK"] = S("#EMAIL") },
            ConsistentRead = true,
        }, ct);

        return response.Item is { Count: > 0 } item ? await GetByIdAsync(item["AccountId"].S, ct) : null;
    }

    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct)
    {
        var items = await QueryAsync(db, tableName, AccountsPk, "ACCOUNT#", projection: null, ct);
        return items.Where(i => Optional(i, "EntityType") == "Account").Select(ToAccount).ToList();
    }

    public Task UpdateAsync(Account account, CancellationToken ct) =>
        db.PutItemAsync(new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(account),
            ConditionExpression = "attribute_exists(PK)",
        }, ct);

    public Task AddGrantAsync(AccessGrant grant, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(AccountsPk),
            ["SK"] = S($"{AccountSk(grant.AccountId)}#GRANT#{grant.Id}"),
            ["EntityType"] = S("AccessGrant"),
            ["Id"] = S(grant.Id),
            ["AccountId"] = S(grant.AccountId),
            ["Days"] = N(grant.Days),
            ["GrantedAt"] = S(FormatTimestamp(grant.GrantedAt)),
            ["NewUntil"] = S(FormatDate(grant.NewUntil)),
        };
        if (grant.AmountPaid is { } amount)
        {
            item["AmountPaid"] = N(amount);
        }

        if (grant.PreviousUntil is { } previous)
        {
            item["PreviousUntil"] = S(FormatDate(previous));
        }

        AddOptional(item, "Note", grant.Note);
        return db.PutItemAsync(new PutItemRequest { TableName = tableName, Item = item }, ct);
    }

    public async Task<IReadOnlyList<AccessGrant>> ListGrantsAsync(string accountId, CancellationToken ct)
    {
        var items = await QueryAsync(db, tableName, AccountsPk, $"{AccountSk(accountId)}#GRANT#", projection: null, ct);
        return items.Select(item => new AccessGrant(
                item["Id"].S,
                item["AccountId"].S,
                ParseInt(item["Days"].N),
                item.TryGetValue("AmountPaid", out var amount) ? ParseDecimal(amount.N) : null,
                Optional(item, "Note"),
                ParseTimestamp(item["GrantedAt"].S),
                Optional(item, "PreviousUntil") is { } previous ? ParseDate(previous) : null,
                ParseDate(item["NewUntil"].S)))
            .ToList();
    }

    private static string AccountSk(string accountId) => $"ACCOUNT#{accountId}";

    private static string EmailPk(string email) => $"EMAIL#{email}";

    private static Dictionary<string, AttributeValue> ToItem(Account account)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(AccountsPk),
            ["SK"] = S(AccountSk(account.Id)),
            ["EntityType"] = S("Account"),
            ["Id"] = S(account.Id),
            ["Email"] = S(account.Email),
            ["Name"] = S(account.Name),
            ["PasswordHash"] = S(account.PasswordHash),
            ["Status"] = S(account.Status.ToString()),
            ["Plan"] = S(account.Plan.ToString()),
            ["CreatedAt"] = S(FormatTimestamp(account.CreatedAt)),
        };
        AddOptional(item, "Phone", account.Phone);
        AddOptional(item, "AdminNote", account.AdminNote);
        if (account.AccessUntil is { } until)
        {
            item["AccessUntil"] = S(FormatDate(until));
        }

        return item;
    }

    private static Account ToAccount(Dictionary<string, AttributeValue> item) => new(
        item["Id"].S,
        item["Email"].S,
        item["Name"].S,
        Optional(item, "Phone"),
        item["PasswordHash"].S,
        Enum.Parse<AccountStatus>(item["Status"].S),
        Enum.Parse<Plan>(item["Plan"].S),
        ParseTimestamp(item["CreatedAt"].S),
        Optional(item, "AccessUntil") is { } until ? ParseDate(until) : null,
        Optional(item, "AdminNote"));
}
