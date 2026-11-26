namespace Cobranca.Domain;

/// <summary>
/// What the user asked to bill. A plan can expand into several charges:
/// a monthly fee repeated until a given month, or a one-off service split into monthly installments.
/// </summary>
public sealed record ChargePlan(
    string ClientId,
    string ServiceName,
    string? Description,
    DateOnly Reference,
    DateOnly DueDate,
    decimal Amount,
    ChargeKind Kind,
    DateOnly? RepeatUntil = null,
    int? Installments = null);

public static class ChargePlanner
{
    /// <summary>Upper bound for one plan (five years of monthly charges).</summary>
    public const int MaxCharges = 60;

    /// <summary>Number of competence months from <paramref name="from"/> to <paramref name="to"/>, both inclusive.</summary>
    public static int CountMonths(DateOnly from, DateOnly to) =>
        (to.Year - from.Year) * 12 + to.Month - from.Month + 1;

    /// <summary>
    /// Splits a total into equal installments rounded down to the cent; the last installment absorbs
    /// the remainder so the parts always add up to the total (1000 / 3 = 333.33 + 333.33 + 333.34).
    /// </summary>
    public static IReadOnlyList<decimal> Split(decimal total, int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        var installment = Math.Floor(total / parts * 100m) / 100m;
        var result = Enumerable.Repeat(installment, parts - 1).ToList();
        result.Add(total - installment * (parts - 1));
        return result;
    }

    /// <summary>
    /// Expands a plan into charges. Each extra month shifts both the competence and the due date by one
    /// month (a due date on the 31st becomes the last day of shorter months).
    /// </summary>
    public static IReadOnlyList<Charge> Expand(ChargePlan plan, Func<string> newId)
    {
        var reference = new DateOnly(plan.Reference.Year, plan.Reference.Month, 1);

        if (plan.Kind == ChargeKind.Recurring && plan.RepeatUntil is { } until)
        {
            var months = CountMonths(reference, until);
            return Enumerable.Range(0, months)
                .Select(i => new Charge(
                    newId(),
                    plan.ClientId,
                    plan.ServiceName,
                    plan.Description,
                    reference.AddMonths(i),
                    plan.DueDate.AddMonths(i),
                    plan.Amount,
                    ChargeKind.Recurring))
                .ToList();
        }

        if (plan.Kind == ChargeKind.OneOff && plan.Installments is { } count && count > 1)
        {
            var amounts = Split(plan.Amount, count);
            return amounts
                .Select((amount, i) => new Charge(
                    newId(),
                    plan.ClientId,
                    plan.ServiceName,
                    InstallmentDescription(i + 1, count, plan.Description),
                    reference.AddMonths(i),
                    plan.DueDate.AddMonths(i),
                    amount,
                    ChargeKind.OneOff))
                .ToList();
        }

        return [new Charge(newId(), plan.ClientId, plan.ServiceName, plan.Description, reference, plan.DueDate, plan.Amount, plan.Kind)];
    }

    private static string InstallmentDescription(int number, int count, string? description) =>
        description is null ? $"Parcela {number}/{count}" : $"Parcela {number}/{count} · {description}";
}
