namespace Cobranca.Domain;

public sealed record ChargeLine(
    Charge Charge,
    decimal Paid,
    decimal Remaining,
    ChargeStatus Status,
    bool IsOverdue);

public sealed record ClientStatement(
    decimal TotalCharged,
    decimal TotalPaid,
    decimal Outstanding,
    decimal OverdueAmount,
    decimal Credit,
    int OpenCharges,
    int OverdueCharges,
    IReadOnlyList<ChargeLine> Lines);

/// <summary>
/// Allocates payments to charges and derives each charge's status.
/// Rules:
/// 1. A payment linked to a charge pays that charge first; any excess joins the general pool.
/// 2. Unlinked payments (and excess) pay the oldest charges first (by due date, then competence).
/// 3. Whatever is left after every charge is paid is reported as client credit.
/// 4. A charge is overdue when it still has a remaining amount and its due date is before today.
/// </summary>
public static class BalanceCalculator
{
    public static ClientStatement Calculate(
        IReadOnlyList<Charge> charges,
        IReadOnlyList<Payment> payments,
        DateOnly today)
    {
        var ordered = charges
            .OrderBy(c => c.DueDate)
            .ThenBy(c => c.Reference)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        var paid = ordered.ToDictionary(c => c.Id, _ => 0m);
        var amounts = ordered.ToDictionary(c => c.Id, c => c.Amount);
        var pool = 0m;

        foreach (var payment in payments)
        {
            if (payment.ChargeId is { } chargeId && paid.TryGetValue(chargeId, out var alreadyPaid))
            {
                var applied = Math.Min(amounts[chargeId] - alreadyPaid, payment.Amount);
                paid[chargeId] = alreadyPaid + applied;
                pool += payment.Amount - applied;
            }
            else
            {
                // Unlinked, or linked to a charge that no longer exists.
                pool += payment.Amount;
            }
        }

        foreach (var charge in ordered)
        {
            if (pool <= 0m)
            {
                break;
            }

            var room = charge.Amount - paid[charge.Id];
            if (room <= 0m)
            {
                continue;
            }

            var applied = Math.Min(room, pool);
            paid[charge.Id] += applied;
            pool -= applied;
        }

        var lines = ordered.Select(charge =>
        {
            var paidAmount = paid[charge.Id];
            var remaining = charge.Amount - paidAmount;
            var status = remaining == 0m
                ? ChargeStatus.Paid
                : paidAmount > 0m ? ChargeStatus.Partial : ChargeStatus.Open;
            var overdue = remaining > 0m && charge.DueDate < today;
            return new ChargeLine(charge, paidAmount, remaining, status, overdue);
        }).ToList();

        return new ClientStatement(
            TotalCharged: ordered.Sum(c => c.Amount),
            TotalPaid: payments.Sum(p => p.Amount),
            Outstanding: lines.Sum(l => l.Remaining),
            OverdueAmount: lines.Where(l => l.IsOverdue).Sum(l => l.Remaining),
            Credit: pool,
            OpenCharges: lines.Count(l => l.Remaining > 0m),
            OverdueCharges: lines.Count(l => l.IsOverdue),
            Lines: lines);
    }
}
