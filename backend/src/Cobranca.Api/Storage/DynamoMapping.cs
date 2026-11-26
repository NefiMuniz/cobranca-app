using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Cobranca.Api.Storage;

/// <summary>Attribute conversions and paginated queries shared by the DynamoDB repositories.</summary>
internal static class DynamoMapping
{
    private const string DateFormat = "yyyy-MM-dd";

    public static AttributeValue S(string value) => new() { S = value };

    public static AttributeValue N(decimal value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    public static AttributeValue N(int value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    public static string FormatDate(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

    public static string FormatTimestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static decimal ParseDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    public static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    public static string? Optional(Dictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var value) ? value.S : null;

    public static void AddOptional(Dictionary<string, AttributeValue> item, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            item[key] = S(value);
        }
    }

    /// <summary>Reads every page of a partition, optionally narrowed with begins_with on the sort key.</summary>
    public static async Task<List<Dictionary<string, AttributeValue>>> QueryAsync(
        IAmazonDynamoDB db,
        string tableName,
        string partitionKey,
        string? sortKeyPrefix,
        string? projection,
        CancellationToken ct)
    {
        var values = new Dictionary<string, AttributeValue> { [":pk"] = S(partitionKey) };
        var condition = "PK = :pk";
        if (sortKeyPrefix is not null)
        {
            condition += " AND begins_with(SK, :prefix)";
            values[":prefix"] = S(sortKeyPrefix);
        }

        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var request = new QueryRequest
            {
                TableName = tableName,
                KeyConditionExpression = condition,
                ExpressionAttributeValues = values,
                ExclusiveStartKey = startKey,
            };
            if (projection is not null)
            {
                request.ProjectionExpression = projection;
            }

            var response = await db.QueryAsync(request, ct);
            if (response.Items is not null)
            {
                items.AddRange(response.Items);
            }

            startKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (startKey is not null);

        return items;
    }
}
