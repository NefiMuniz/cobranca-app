using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Cobranca.Domain;
using static Cobranca.Api.Storage.DynamoMapping;

namespace Cobranca.Api.Storage;

/// <summary>
/// Single-table design, one partition per tenant:
///   PK = TENANT#{tenantId}
///   SK = CLIENT#{clientId}#PROFILE | CLIENT#{clientId}#CHARGE#{chargeId} | CLIENT#{clientId}#PAYMENT#{paymentId}
/// The partition key is built from the tenant on every call, so a request can only ever read its own partition.
/// The dashboard is one Query on the partition; a single client is the same Query with begins_with on the sort key.
/// </summary>
public sealed class DynamoDbBillingRepository(IAmazonDynamoDB db, string tableName) : IBillingRepository
{
    public async Task<IReadOnlyList<ClientLedger>> GetAllLedgersAsync(string tenantId, CancellationToken ct)
    {
        var items = await QueryAsync(db, tableName, Pk(tenantId), sortKeyPrefix: null, projection: null, ct);
        return items
            .GroupBy(item => item["ClientId"].S)
            .Select(group => ToLedger(group))
            .OfType<ClientLedger>()
            .ToList();
    }

    public async Task<ClientLedger?> GetLedgerAsync(string tenantId, string clientId, CancellationToken ct) =>
        ToLedger(await QueryAsync(db, tableName, Pk(tenantId), ClientPrefix(clientId), projection: null, ct));

    public Task SaveClientAsync(string tenantId, Client client, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(Pk(tenantId)),
            ["SK"] = S(ClientPrefix(client.Id) + "PROFILE"),
            ["EntityType"] = S("Client"),
            ["Id"] = S(client.Id),
            ["ClientId"] = S(client.Id),
            ["Name"] = S(client.Name),
            ["CreatedAt"] = S(FormatTimestamp(client.CreatedAt)),
        };
        AddOptional(item, "Phone", client.Phone);
        AddOptional(item, "Notes", client.Notes);
        return PutAsync(item, ct);
    }

    public async Task DeleteClientAsync(string tenantId, string clientId, CancellationToken ct)
    {
        var keys = await QueryAsync(db, tableName, Pk(tenantId), ClientPrefix(clientId), projection: "PK, SK", ct);
        foreach (var key in keys)
        {
            await db.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { ["PK"] = key["PK"], ["SK"] = key["SK"] },
            }, ct);
        }
    }

    public Task SaveChargeAsync(string tenantId, Charge charge, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(Pk(tenantId)),
            ["SK"] = S(ClientPrefix(charge.ClientId) + "CHARGE#" + charge.Id),
            ["EntityType"] = S("Charge"),
            ["Id"] = S(charge.Id),
            ["ClientId"] = S(charge.ClientId),
            ["ServiceName"] = S(charge.ServiceName),
            ["Reference"] = S(FormatDate(charge.Reference)),
            ["DueDate"] = S(FormatDate(charge.DueDate)),
            ["Amount"] = N(charge.Amount),
            ["Kind"] = S(charge.Kind.ToString()),
        };
        AddOptional(item, "Description", charge.Description);
        return PutAsync(item, ct);
    }

    public Task DeleteChargeAsync(string tenantId, string clientId, string chargeId, CancellationToken ct) =>
        DeleteAsync(tenantId, ClientPrefix(clientId) + "CHARGE#" + chargeId, ct);

    public Task SavePaymentAsync(string tenantId, Payment payment, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(Pk(tenantId)),
            ["SK"] = S(ClientPrefix(payment.ClientId) + "PAYMENT#" + payment.Id),
            ["EntityType"] = S("Payment"),
            ["Id"] = S(payment.Id),
            ["ClientId"] = S(payment.ClientId),
            ["Amount"] = N(payment.Amount),
            ["PaidOn"] = S(FormatDate(payment.PaidOn)),
        };
        AddOptional(item, "ChargeId", payment.ChargeId);
        AddOptional(item, "Note", payment.Note);
        return PutAsync(item, ct);
    }

    public Task DeletePaymentAsync(string tenantId, string clientId, string paymentId, CancellationToken ct) =>
        DeleteAsync(tenantId, ClientPrefix(clientId) + "PAYMENT#" + paymentId, ct);

    private static string Pk(string tenantId) => $"TENANT#{tenantId}";

    // Trailing '#' so "CLIENT#ab#" never matches the items of client "abc".
    private static string ClientPrefix(string clientId) => $"CLIENT#{clientId}#";

    private Task PutAsync(Dictionary<string, AttributeValue> item, CancellationToken ct) =>
        db.PutItemAsync(new PutItemRequest { TableName = tableName, Item = item }, ct);

    private Task DeleteAsync(string tenantId, string sortKey, CancellationToken ct) =>
        db.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue> { ["PK"] = S(Pk(tenantId)), ["SK"] = S(sortKey) },
        }, ct);

    private static ClientLedger? ToLedger(IEnumerable<Dictionary<string, AttributeValue>> items)
    {
        Client? client = null;
        var charges = new List<Charge>();
        var payments = new List<Payment>();

        foreach (var item in items)
        {
            switch (Optional(item, "EntityType"))
            {
                case "Client":
                    client = new Client(
                        item["Id"].S,
                        item["Name"].S,
                        Optional(item, "Phone"),
                        Optional(item, "Notes"),
                        ParseTimestamp(item["CreatedAt"].S));
                    break;
                case "Charge":
                    charges.Add(new Charge(
                        item["Id"].S,
                        item["ClientId"].S,
                        item["ServiceName"].S,
                        Optional(item, "Description"),
                        ParseDate(item["Reference"].S),
                        ParseDate(item["DueDate"].S),
                        ParseDecimal(item["Amount"].N),
                        Enum.Parse<ChargeKind>(item["Kind"].S)));
                    break;
                case "Payment":
                    payments.Add(new Payment(
                        item["Id"].S,
                        item["ClientId"].S,
                        ParseDecimal(item["Amount"].N),
                        ParseDate(item["PaidOn"].S),
                        Optional(item, "ChargeId"),
                        Optional(item, "Note")));
                    break;
            }
        }

        // Orphan charges/payments without a profile (e.g. a half-finished delete) are ignored.
        return client is null ? null : new ClientLedger(client, charges, payments);
    }
}
