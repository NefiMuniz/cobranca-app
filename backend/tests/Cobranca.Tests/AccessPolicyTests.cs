using Cobranca.Domain;

namespace Cobranca.Tests;

public class AccessPolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    private static Account Account(AccountStatus status, DateOnly? until) =>
        new("a", "a@exemplo.com", "Conta", null, "hash", status, Plan.Starter, DateTimeOffset.UtcNow, until, null);

    [Theory]
    [InlineData(AccountStatus.Pending, null, AccessState.Pending)]
    [InlineData(AccountStatus.Active, 0, AccessState.Active)] // last day still counts
    [InlineData(AccountStatus.Active, 10, AccessState.Active)]
    [InlineData(AccountStatus.Active, -1, AccessState.Expired)]
    [InlineData(AccountStatus.Active, null, AccessState.Expired)]
    [InlineData(AccountStatus.Suspended, 10, AccessState.Suspended)] // suspension wins over remaining days
    [InlineData(AccountStatus.Rejected, null, AccessState.Rejected)]
    public void State_combines_status_and_expiry(AccountStatus status, int? daysFromToday, AccessState expected)
    {
        DateOnly? until = daysFromToday is { } d ? Today.AddDays(d) : null;

        Assert.Equal(expected, AccessPolicy.StateOf(Account(status, until), Today));
    }

    [Fact]
    public void Renewal_while_active_keeps_the_remaining_days()
    {
        Assert.Equal(Today.AddDays(10 + 30), AccessPolicy.Extend(Today.AddDays(10), 30, Today));
    }

    [Fact]
    public void Renewal_after_expiry_counts_from_today()
    {
        Assert.Equal(Today.AddDays(30), AccessPolicy.Extend(Today.AddDays(-5), 30, Today));
        Assert.Equal(Today.AddDays(30), AccessPolicy.Extend(null, 30, Today));
    }

    [Fact]
    public void Days_left_is_negative_after_expiry()
    {
        Assert.Equal(3, AccessPolicy.DaysLeft(Today.AddDays(3), Today));
        Assert.Equal(-2, AccessPolicy.DaysLeft(Today.AddDays(-2), Today));
        Assert.Null(AccessPolicy.DaysLeft(null, Today));
    }

    [Fact]
    public void Extending_by_zero_days_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AccessPolicy.Extend(Today, 0, Today));
    }
}
