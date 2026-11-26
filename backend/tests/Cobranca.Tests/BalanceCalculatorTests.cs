using System.Globalization;
using Cobranca.Domain;

namespace Cobranca.Tests;

public class BalanceCalculatorTests
{
    private static readonly DateOnly Today = D("2026-02-10");

    [Fact]
    public void Unlinked_payments_pay_the_oldest_charges_first()
    {
        var charges = new[] { MakeCharge("jan", 500m, "2026-01-05"), MakeCharge("feb", 500m, "2026-02-05"), MakeCharge("mar", 600m, "2026-03-05") };
        var payments = new[] { MakePayment(700m) };

        var statement = BalanceCalculator.Calculate(charges, payments, Today);

        Assert.Equal(ChargeStatus.Paid, Line(statement, "jan").Status);
        Assert.Equal(ChargeStatus.Partial, Line(statement, "feb").Status);
        Assert.Equal(300m, Line(statement, "feb").Remaining);
        Assert.Equal(ChargeStatus.Open, Line(statement, "mar").Status);
        Assert.Equal(900m, statement.Outstanding);
        Assert.Equal(0m, statement.Credit);
    }

    [Fact]
    public void Linked_payment_pays_its_charge_and_the_excess_goes_to_the_oldest_open_charge()
    {
        var charges = new[] { MakeCharge("a", 300m, "2026-01-05"), MakeCharge("b", 300m, "2026-02-05") };
        var payments = new[] { MakePayment(400m, chargeId: "b") };

        var statement = BalanceCalculator.Calculate(charges, payments, Today);

        Assert.Equal(ChargeStatus.Paid, Line(statement, "b").Status);
        Assert.Equal(100m, Line(statement, "a").Paid);
        Assert.Equal(ChargeStatus.Partial, Line(statement, "a").Status);
        Assert.Equal(200m, statement.Outstanding);
    }

    [Fact]
    public void Overpayment_becomes_credit()
    {
        var statement = BalanceCalculator.Calculate([MakeCharge("a", 500m, "2026-01-05")], [MakePayment(650m)], Today);

        Assert.Equal(ChargeStatus.Paid, Line(statement, "a").Status);
        Assert.Equal(0m, statement.Outstanding);
        Assert.Equal(150m, statement.Credit);
        Assert.Equal(650m, statement.TotalPaid);
    }

    [Fact]
    public void Charge_is_overdue_only_after_its_due_date_and_while_something_remains()
    {
        var charges = new[]
        {
            MakeCharge("late", 200m, "2026-02-05"),
            MakeCharge("due-today", 200m, "2026-02-10"),
            MakeCharge("paid-late", 100m, "2026-01-05"),
        };
        var payments = new[] { MakePayment(100m, chargeId: "paid-late") };

        var statement = BalanceCalculator.Calculate(charges, payments, Today);

        Assert.True(Line(statement, "late").IsOverdue);
        Assert.False(Line(statement, "due-today").IsOverdue);
        Assert.False(Line(statement, "paid-late").IsOverdue);
        Assert.Equal(200m, statement.OverdueAmount);
        Assert.Equal(1, statement.OverdueCharges);
        Assert.Equal(2, statement.OpenCharges);
    }

    [Fact]
    public void Payment_linked_to_a_deleted_charge_is_treated_as_unlinked()
    {
        var statement = BalanceCalculator.Calculate(
            [MakeCharge("a", 300m, "2026-01-05")],
            [MakePayment(300m, chargeId: "no-longer-exists")],
            Today);

        Assert.Equal(ChargeStatus.Paid, Line(statement, "a").Status);
        Assert.Equal(0m, statement.Outstanding);
    }

    [Fact]
    public void Bundled_package_paid_in_parts_matches_the_spreadsheet_balance()
    {
        // Same numbers as the original spreadsheet: four items totalling 10,925 with 3,000 received.
        var charges = new[]
        {
            MakeCharge("12m-ecf", 3950m, "2025-08-15"),
            MakeCharge("ecf-zeradas", 525m, "2025-08-15"),
            MakeCharge("12m-2ecf", 5020m, "2025-08-15"),
            MakeCharge("6m-ecf", 1430m, "2025-08-15"),
        };
        var payments = new[] { MakePayment(1000m, paidOn: "2026-08-01"), MakePayment(2000m, paidOn: "2026-10-16") };

        var statement = BalanceCalculator.Calculate(charges, payments, D("2026-11-24"));

        Assert.Equal(10925m, statement.TotalCharged);
        Assert.Equal(7925m, statement.Outstanding);
        Assert.Equal(7925m, statement.OverdueAmount);
    }

    [Fact]
    public void Client_without_charges_has_zero_balance()
    {
        var statement = BalanceCalculator.Calculate([], [], Today);

        Assert.Equal(0m, statement.Outstanding);
        Assert.Empty(statement.Lines);
    }

    private static DateOnly D(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Charge MakeCharge(string id, decimal amount, string dueDate)
    {
        var due = D(dueDate);
        return new Charge(id, "client", "Serviço", null, new DateOnly(due.Year, due.Month, 1), due, amount, ChargeKind.OneOff);
    }

    private static Payment MakePayment(decimal amount, string? chargeId = null, string paidOn = "2026-02-01") =>
        new(Guid.NewGuid().ToString("N"), "client", amount, D(paidOn), chargeId, null);

    private static ChargeLine Line(ClientStatement statement, string chargeId) =>
        statement.Lines.Single(l => l.Charge.Id == chargeId);
}
