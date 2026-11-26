using System.Net;
using System.Net.Http.Json;
using Cobranca.Api.Accounts;
using Cobranca.Api.Billing;
using Cobranca.Domain;

namespace Cobranca.Tests;

/// <summary>Sign-up, approval and, above all, isolation between accounts.</summary>
public class TenantTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UserPassword = "senha-do-cliente-1";

    [Fact]
    public async Task Signup_creates_a_pending_account_that_cannot_sign_in()
    {
        var email = UniqueEmail();
        var signup = await SignupAsync(email);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = email, password = UserPassword });

        Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Contains("\"pending\"", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Approved_accounts_only_ever_see_their_own_data()
    {
        var admin = await factory.SignInAsAdminAsync();
        var adminClient = await CreateClientAsync(admin, "Cliente do Admin");
        var alice = await ApprovedUserAsync(days: 30);
        var bob = await ApprovedUserAsync(days: 30);
        var aliceClient = await CreateClientAsync(alice, "Cliente da Alice");

        var bobList = await bob.GetFromJsonAsync<ClientSummary[]>("/api/clients", TestJson.Options);
        var aliceList = await alice.GetFromJsonAsync<ClientSummary[]>("/api/clients", TestJson.Options);
        var adminDashboard = await admin.GetFromJsonAsync<DashboardResponse>("/api/dashboard", TestJson.Options);

        Assert.Empty(bobList!);
        Assert.Equal(new[] { aliceClient.Id }, aliceList!.Select(c => c.Id).ToArray());
        Assert.DoesNotContain(adminDashboard!.Clients, c => c.Id == aliceClient.Id);

        // Knowing another tenant's ids is not enough: every operation is scoped to the caller's own tenant.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/clients/{aliceClient.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/clients/{aliceClient.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/clients/{adminClient.Id}")).StatusCode);
        var payment = await bob.PostAsJsonAsync($"/api/clients/{aliceClient.Id}/payments", new { amount = 10m, paidOn = "2026-11-24" });
        Assert.Equal(HttpStatusCode.NotFound, payment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/clients/{aliceClient.Id}")).StatusCode);
    }

    [Fact]
    public async Task Subscribers_cannot_use_the_admin_panel()
    {
        var user = await ApprovedUserAsync(days: 30);

        var response = await user.GetAsync("/api/admin/accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Suspension_cuts_access_on_the_next_request_and_reactivation_restores_it()
    {
        var email = UniqueEmail();
        var user = await ApprovedUserAsync(days: 30, email: email);
        var admin = await factory.SignInAsAdminAsync();
        var accountId = (await FindAccountAsync(admin, email)).Id;

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/dashboard")).StatusCode);

        (await admin.PostAsJsonAsync($"/api/admin/accounts/{accountId}/suspend", new { note = "Pagamento pendente" })).EnsureSuccessStatusCode();
        var blocked = await user.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("\"suspended\"", await blocked.Content.ReadAsStringAsync());

        (await admin.PostAsJsonAsync($"/api/admin/accounts/{accountId}/reactivate", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Renewal_adds_days_to_the_current_expiry()
    {
        var email = UniqueEmail();
        await ApprovedUserAsync(days: 30, email: email);
        var admin = await factory.SignInAsAdminAsync();
        var account = await FindAccountAsync(admin, email);

        var renewal = await admin.PostAsJsonAsync($"/api/admin/accounts/{account.Id}/grant", new { days = 30, amountPaid = 49.90m });
        var detail = await renewal.Content.ReadFromJsonAsync<AccountDetail>(TestJson.Options);

        var today = DateOnly.FromDateTime(ApiFactory.Now.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        Assert.Equal(today.AddDays(60), detail!.Account.AccessUntil);
        Assert.Equal(2, detail.Grants.Count);
        Assert.Contains(detail.Grants, g => g.AmountPaid == 49.90m);
    }

    [Fact]
    public async Task Duplicate_signup_is_indistinguishable_and_does_not_replace_the_password()
    {
        var email = UniqueEmail();
        var first = await SignupAsync(email);
        var second = await SignupAsync(email, password: "outra-senha-qualquer");
        var admin = await factory.SignInAsAdminAsync();

        var accounts = await admin.GetFromJsonAsync<AccountView[]>("/api/admin/accounts", TestJson.Options);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Single(accounts!, a => a.Email == email);
    }

    [Fact]
    public async Task Bots_filling_the_honeypot_are_silently_ignored()
    {
        var email = UniqueEmail();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/signup", new
        {
            name = "Bot",
            email,
            phone = "(77) 99999-0000",
            password = UserPassword,
            website = "http://spam.example",
        });
        var admin = await factory.SignInAsAdminAsync();
        var accounts = await admin.GetFromJsonAsync<AccountView[]>("/api/admin/accounts", TestJson.Options);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(accounts!, a => a.Email == email);
    }

    [Fact]
    public async Task Invalid_signup_returns_field_errors()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/signup", new
        {
            name = "",
            email = "sem-arroba",
            phone = "123",
            password = "curta",
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        foreach (var field in new[] { "name", "email", "phone", "password" })
        {
            Assert.Contains(field, body);
        }
    }

    [Fact]
    public async Task Ids_with_key_separators_are_rejected()
    {
        var admin = await factory.SignInAsAdminAsync();

        var response = await admin.GetAsync("/api/clients/abc%23CHARGE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string UniqueEmail() => $"cliente-{Guid.NewGuid():N}@exemplo.com";

    private Task<HttpResponseMessage> SignupAsync(string email, string password = UserPassword) =>
        factory.CreateClient().PostAsJsonAsync("/api/signup", new
        {
            name = "Negócio de Teste",
            email = email.ToUpperInvariant(), // normalized by the API
            phone = "(77) 99999-0000",
            password,
        });

    private async Task<HttpClient> ApprovedUserAsync(int days, string? email = null)
    {
        email ??= UniqueEmail();
        (await SignupAsync(email)).EnsureSuccessStatusCode();
        var admin = await factory.SignInAsAdminAsync();
        var account = await FindAccountAsync(admin, email);
        (await admin.PostAsJsonAsync($"/api/admin/accounts/{account.Id}/grant", new { days })).EnsureSuccessStatusCode();
        return await factory.SignInAsync(email, UserPassword);
    }

    private static async Task<AccountView> FindAccountAsync(HttpClient admin, string email)
    {
        var accounts = await admin.GetFromJsonAsync<AccountView[]>("/api/admin/accounts", TestJson.Options);
        return Assert.Single(accounts!, a => a.Email == email);
    }

    private static async Task<Client> CreateClientAsync(HttpClient http, string name)
    {
        var response = await http.PostAsJsonAsync("/api/clients", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Client>(TestJson.Options))!;
    }
}

/// <summary>Moves the clock, so it runs on its own factory instance.</summary>
public class ExpiryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Access_ends_after_the_granted_days_and_renewal_counts_from_the_renewal_day()
    {
        const string email = "expira@exemplo.com";
        const string password = "senha-que-expira";
        (await factory.CreateClient().PostAsJsonAsync("/api/signup", new
        {
            name = "Conta que expira",
            email,
            phone = "(77) 98888-0000",
            password,
        })).EnsureSuccessStatusCode();

        var admin = await factory.SignInAsAdminAsync();
        var accounts = await admin.GetFromJsonAsync<AccountView[]>("/api/admin/accounts", TestJson.Options);
        var accountId = Assert.Single(accounts!, a => a.Email == email).Id;
        (await admin.PostAsJsonAsync($"/api/admin/accounts/{accountId}/grant", new { days = 10 })).EnsureSuccessStatusCode();
        var user = await factory.SignInAsync(email, password);

        factory.Time.Advance(TimeSpan.FromDays(10));
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/dashboard")).StatusCode); // last day included

        factory.Time.Advance(TimeSpan.FromDays(1));
        var expired = await user.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        Assert.Contains("\"expired\"", await expired.Content.ReadAsStringAsync());

        var me = await user.GetFromJsonAsync<MeResponse>("/api/me", TestJson.Options);
        Assert.Equal(AccessState.Expired, me!.State);

        var renewal = await admin.PostAsJsonAsync($"/api/admin/accounts/{accountId}/grant", new { days = 30 });
        var detail = await renewal.Content.ReadFromJsonAsync<AccountDetail>(TestJson.Options);
        Assert.Equal(30, detail!.Account.DaysLeft);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/dashboard")).StatusCode);
    }
}
