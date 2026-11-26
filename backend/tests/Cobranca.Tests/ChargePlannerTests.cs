using Cobranca.Domain;

namespace Cobranca.Tests;

public class ChargePlannerTests
{
    private static int _ids;

    private static string NewId() => $"id-{Interlocked.Increment(ref _ids)}";

    private static ChargePlan Plan(
        decimal amount,
        ChargeKind kind,
        DateOnly? repeatUntil = null,
        int? installments = null,
        DateOnly? dueDate = null) =>
        new("client", "Serviço", "Detalhe", new DateOnly(2027, 1, 1), dueDate ?? new DateOnly(2027, 2, 5),
            amount, kind, repeatUntil, installments);

    [Fact]
    public void Monthly_fee_from_january_until_june_generates_six_charges()
    {
        var charges = ChargePlanner.Expand(Plan(600m, ChargeKind.Recurring, repeatUntil: new DateOnly(2027, 6, 1)), NewId);

        Assert.Equal(6, charges.Count);
        Assert.Equal(new DateOnly(2027, 1, 1), charges[0].Reference);
        Assert.Equal(new DateOnly(2027, 6, 1), charges[^1].Reference);
        Assert.Equal(new DateOnly(2027, 7, 5), charges[^1].DueDate);
        Assert.All(charges, c => Assert.Equal(600m, c.Amount));
        Assert.All(charges, c => Assert.Equal("Detalhe", c.Description));
        Assert.Equal(6, charges.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Repetition_crosses_the_year_boundary()
    {
        var charges = ChargePlanner.Expand(
            Plan(500m, ChargeKind.Recurring, repeatUntil: new DateOnly(2028, 2, 1)), NewId);

        Assert.Equal(14, charges.Count);
        Assert.Equal(new DateOnly(2028, 2, 1), charges[^1].Reference);
    }

    [Fact]
    public void Five_thousand_in_five_installments_generates_five_monthly_charges_of_one_thousand()
    {
        var charges = ChargePlanner.Expand(Plan(5000m, ChargeKind.OneOff, installments: 5), NewId);

        Assert.Equal(5, charges.Count);
        Assert.All(charges, c => Assert.Equal(1000m, c.Amount));
        Assert.Equal("Parcela 1/5 · Detalhe", charges[0].Description);
        Assert.Equal(new DateOnly(2027, 6, 5), charges[^1].DueDate);
        Assert.All(charges, c => Assert.Equal(ChargeKind.OneOff, c.Kind));
    }

    [Fact]
    public void Installments_always_add_up_to_the_total()
    {
        Assert.Equal(new[] { 333.33m, 333.33m, 333.34m }, ChargePlanner.Split(1000m, 3).ToArray());
        Assert.Equal(1000m, ChargePlanner.Split(1000m, 7).Sum());
        Assert.Equal(0.05m, ChargePlanner.Split(0.05m, 5).Sum());
    }

    [Fact]
    public void Due_date_on_the_31st_falls_back_to_the_last_day_of_shorter_months()
    {
        var charges = ChargePlanner.Expand(
            Plan(300m, ChargeKind.OneOff, installments: 2, dueDate: new DateOnly(2027, 1, 31)), NewId);

        Assert.Equal(new DateOnly(2027, 2, 28), charges[1].DueDate);
    }

    [Fact]
    public void Without_repetition_or_installments_a_single_charge_is_created()
    {
        var charges = ChargePlanner.Expand(Plan(250m, ChargeKind.OneOff), NewId);

        var charge = Assert.Single(charges);
        Assert.Equal(250m, charge.Amount);
        Assert.Equal("Detalhe", charge.Description);
    }

    [Theory]
    [InlineData(2027, 1, 2027, 1, 1)]
    [InlineData(2027, 1, 2027, 6, 6)]
    [InlineData(2027, 11, 2028, 2, 4)]
    [InlineData(2027, 6, 2027, 1, -4)]
    public void Counts_months_inclusively(int fromYear, int fromMonth, int toYear, int toMonth, int expected)
    {
        Assert.Equal(expected, ChargePlanner.CountMonths(new DateOnly(fromYear, fromMonth, 1), new DateOnly(toYear, toMonth, 1)));
    }
}
