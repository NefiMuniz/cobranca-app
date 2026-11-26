using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cobranca.Api.Auth;
using Cobranca.Api.Billing;
using Cobranca.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cobranca.Tests;

/// <summary>Runs the real ASP.NET Core pipeline (auth, validation, JSON) against the in-memory store.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "senha-de-teste";

    /// <summary>"Today" for every test starts at 24 Nov 2026, 12:00 in São Paulo; tests may move it forward.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

    private static readonly string PasswordHash = PasswordHasher.Hash(Password);

    public MutableTimeProvider Time { get; } = new(Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Username"] = "admin",
            ["Auth:PasswordHash"] = PasswordHash,
            ["Auth:JwtKey"] = "test-signing-key-that-is-long-enough-for-hs256",
            ["Auth:LoginAttemptsPerMinute"] = "1000",
            ["Auth:SignupRequestsPerHour"] = "1000",
            ["Security:AnonymousRequestsPerMinute"] = "1000",
            ["Storage:Provider"] = "InMemory",
            ["Storage:SeedDemoData"] = "false",
        }));
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }

    /// <summary>Signs in and returns a client that sends the token on every request.</summary>
    public async Task<HttpClient> SignInAsync(string username, string password)
    {
        var http = CreateClient();
        var login = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return http;
    }

    public Task<HttpClient> SignInAsAdminAsync() => SignInAsync("admin", Password);
}

public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = TestJson.Options;

    [Fact]
    public async Task Health_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Billing_endpoints_require_a_token()
    {
        var response = await factory.CreateClient().GetAsync("/api/clients");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Charges_and_payments_produce_the_expected_balance()
    {
        var http = await AuthenticatedClientAsync();
        var client = await CreateClientAsync(http, "Cliente Saldo");

        await PostOk(http, $"/api/clients/{client.Id}/charges", new
        {
            serviceName = "Honorários",
            description = "Outubro",
            reference = "2026-10-01",
            dueDate = "2026-11-05",
            amount = 500m,
            kind = "Recurring",
        });
        await PostOk(http, $"/api/clients/{client.Id}/charges", new
        {
            serviceName = "Honorários",
            reference = "2026-11-17", // normalized to the 1st of the month
            dueDate = "2026-12-05",
            amount = 600m,
            kind = "Recurring",
        });
        await PostOk(http, $"/api/clients/{client.Id}/payments", new { amount = 700m, paidOn = "2026-11-10" });

        var detail = await http.GetFromJsonAsync<ClientDetail>($"/api/clients/{client.Id}", Json);

        Assert.NotNull(detail);
        Assert.Equal(1100m, detail.Statement.TotalCharged);
        Assert.Equal(400m, detail.Statement.Outstanding);
        Assert.Equal(0m, detail.Statement.OverdueAmount);
        Assert.Equal(ChargeStatus.Paid, detail.Statement.Lines[0].Status);
        Assert.Equal(ChargeStatus.Partial, detail.Statement.Lines[1].Status);
        Assert.Equal(new DateOnly(2026, 11, 1), detail.Statement.Lines[1].Charge.Reference);
        Assert.Single(detail.Payments);
    }

    [Fact]
    public async Task Dashboard_reports_overdue_charges()
    {
        var http = await AuthenticatedClientAsync();
        var client = await CreateClientAsync(http, "Cliente Atrasado");
        await PostOk(http, $"/api/clients/{client.Id}/charges", new
        {
            serviceName = "Entrega de ECF",
            reference = "2026-10-01",
            dueDate = "2026-11-05",
            amount = 300m,
        });

        var dashboard = await http.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json);

        Assert.NotNull(dashboard);
        Assert.Equal(new DateOnly(2026, 11, 24), dashboard.Today);
        var summary = Assert.Single(dashboard.Clients, c => c.Id == client.Id);
        Assert.Equal(300m, summary.OverdueAmount);
        Assert.Equal(1, summary.OverdueCharges);
        Assert.True(dashboard.TotalOverdue >= 300m);
    }

    [Fact]
    public async Task Invalid_charge_returns_field_errors()
    {
        var http = await AuthenticatedClientAsync();
        var client = await CreateClientAsync(http, "Cliente Validação");

        var response = await http.PostAsJsonAsync($"/api/clients/{client.Id}/charges", new
        {
            serviceName = "",
            amount = -10m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("serviceName", body);
        Assert.Contains("amount", body);
        Assert.Contains("dueDate", body);
    }

    [Fact]
    public async Task Payment_cannot_reference_another_clients_charge()
    {
        var http = await AuthenticatedClientAsync();
        var owner = await CreateClientAsync(http, "Dono da Cobrança");
        var other = await CreateClientAsync(http, "Outro Cliente");
        var created = await PostOk<Charge[]>(http, $"/api/clients/{owner.Id}/charges", new
        {
            serviceName = "Consultoria",
            reference = "2026-11-01",
            dueDate = "2026-11-30",
            amount = 100m,
        });

        var response = await http.PostAsJsonAsync($"/api/clients/{other.Id}/payments", new
        {
            amount = 100m,
            paidOn = "2026-11-20",
            chargeId = created[0].Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Monthly_fee_can_be_repeated_until_a_given_month()
    {
        var http = await AuthenticatedClientAsync();
        var client = await CreateClientAsync(http, "Cliente Mensalidade");

        var created = await PostOk<Charge[]>(http, $"/api/clients/{client.Id}/charges", new
        {
            serviceName = "Honorários contábeis",
            reference = "2027-01-01",
            dueDate = "2027-02-05",
            amount = 600m,
            kind = "Recurring",
            repeatUntil = "2027-06-01",
        });

        Assert.Equal(6, created.Length);
        Assert.Equal(new DateOnly(2027, 6, 1), created[^1].Reference);
        Assert.Equal(new DateOnly(2027, 7, 5), created[^1].DueDate);
        Assert.All(created, c => Assert.Equal(600m, c.Amount));
    }

    [Fact]
    public async Task One_off_service_can_be_split_into_installments()
    {
        var http = await AuthenticatedClientAsync();
        var client = await CreateClientAsync(http, "Cliente Parcelado");

        var created = await PostOk<Charge[]>(http, $"/api/clients/{client.Id}/charges", new
        {
            serviceName = "Regularização fiscal",
            reference = "2027-01-01",
            dueDate = "2027-01-15",
            amount = 5000m,
            kind = "OneOff",
            installments = 5,
        });

        Assert.Equal(5, created.Length);
        Assert.All(created, c => Assert.Equal(1000m, c.Amount));
        Assert.Equal("Parcela 5/5", created[^1].Description);

        var detail = await http.GetFromJsonAsync<ClientDetail>($"/api/clients/{client.Id}", Json);
        Assert.Equal(5000m, detail!.Statement.Outstanding);
    }

    [Fact]
    public async Task Installments_are_rejected_for_monthly_fees()
    {
        var http = await AuthenticatedClientAsync();
        var client = await CreateClientAsync(http, "Cliente Plano Inválido");

        var response = await http.PostAsJsonAsync($"/api/clients/{client.Id}/charges", new
        {
            serviceName = "Honorários",
            reference = "2027-01-01",
            dueDate = "2027-02-05",
            amount = 600m,
            kind = "Recurring",
            installments = 3,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("installments", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deleting_a_client_removes_it()
    {
        var http = await AuthenticatedClientAsync();
        var client = await CreateClientAsync(http, "Cliente Removido");

        var delete = await http.DeleteAsync($"/api/clients/{client.Id}");
        var get = await http.GetAsync($"/api/clients/{client.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    private Task<HttpClient> AuthenticatedClientAsync() => factory.SignInAsAdminAsync();

    private static Task<Client> CreateClientAsync(HttpClient http, string name) =>
        PostOk<Client>(http, "/api/clients", new { name });

    private static async Task PostOk(HttpClient http, string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<T> PostOk<T>(HttpClient http, string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>(Json);
        Assert.NotNull(value);
        return value;
    }
}
