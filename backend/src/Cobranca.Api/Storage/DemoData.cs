using Cobranca.Api.Auth;
using Cobranca.Domain;

namespace Cobranca.Api.Storage;

/// <summary>
/// Fictitious data shaped like the real use cases (monthly fees, partial payments, a bundled package).
/// Dates are relative to today so the demo always shows something open and something overdue.
/// </summary>
internal static class DemoData
{
    public static (List<Client> Clients, List<Charge> Charges, List<Payment> Payments) Create(DateOnly today)
    {
        var clients = new List<Client>();
        var charges = new List<Charge>();
        var payments = new List<Payment>();
        var created = DateTimeOffset.UtcNow;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        // 1. Monthly bookkeeping fee: fee raised from 500 to 600, last months still open.
        var bakery = new Client("demo-bakery", "Padaria Sol Nascente", "(77) 90000-0001", "Mensalidade vence todo dia 5.", created);
        clients.Add(bakery);
        for (var monthsAgo = 8; monthsAgo >= 0; monthsAgo--)
        {
            var reference = thisMonth.AddMonths(-monthsAgo);
            var amount = monthsAgo <= 3 ? 600m : 500m;
            var charge = new Charge(
                $"demo-bakery-{monthsAgo}", bakery.Id, "Honorários contábeis", "Escrituração e folha do mês",
                reference, reference.AddMonths(1).AddDays(4), amount, ChargeKind.Recurring);
            charges.Add(charge);

            if (monthsAgo >= 4)
            {
                payments.Add(new Payment($"demo-bakery-pay-{monthsAgo}", bakery.Id, amount,
                    charge.DueDate.AddDays(3), charge.Id, "Pix"));
            }
        }

        // 2. One-off services paid in parts, not tied to a specific charge.
        var clinic = new Client("demo-clinic", "Clínica Bem-Estar", "(77) 90000-0002", null, created);
        clients.Add(clinic);
        charges.Add(new Charge("demo-clinic-1", clinic.Id, "Regularização fiscal",
            "Parcelamento de débitos e certidões negativas", thisMonth.AddMonths(-5), thisMonth.AddMonths(-4), 1500m, ChargeKind.OneOff));
        charges.Add(new Charge("demo-clinic-2", clinic.Id, "Entrega de ECF", "Ano-calendário anterior",
            thisMonth.AddMonths(-4), thisMonth.AddMonths(-3).AddDays(14), 150m, ChargeKind.OneOff));
        payments.Add(new Payment("demo-clinic-pay-1", clinic.Id, 500m, thisMonth.AddMonths(-4).AddDays(10), null, "Entrada"));
        payments.Add(new Payment("demo-clinic-pay-2", clinic.Id, 500m, thisMonth.AddMonths(-2).AddDays(3), null, null));

        // 3. Bundled package billed once, paid over time.
        var store = new Client("demo-store", "Marcos Andrade ME", null, "Pacote fechado em 6x sem juros.", created);
        clients.Add(store);
        charges.Add(new Charge("demo-store-1", store.Id, "Escrituração retroativa",
            "12 meses + ECF", thisMonth.AddMonths(-3), thisMonth.AddMonths(-2), 3950m, ChargeKind.OneOff));
        charges.Add(new Charge("demo-store-2", store.Id, "ECFs zeradas", "3 declarações sem movimento",
            thisMonth.AddMonths(-3), thisMonth.AddMonths(-2), 525m, ChargeKind.OneOff));
        payments.Add(new Payment("demo-store-pay-1", store.Id, 1000m, thisMonth.AddMonths(-2).AddDays(1), null, null));
        payments.Add(new Payment("demo-store-pay-2", store.Id, 2000m, thisMonth.AddMonths(-1).AddDays(15), null, null));

        // 4. Fully paid client, to show the "em dia" state.
        var studio = new Client("demo-studio", "Studio Lume", "(77) 90000-0004", null, created);
        clients.Add(studio);
        for (var i = 1; i <= 3; i++)
        {
            var reference = thisMonth.AddMonths(-i);
            var charge = new Charge($"demo-studio-{i}", studio.Id, "Consultoria tributária", null,
                reference, reference.AddDays(14), 300m, ChargeKind.OneOff);
            charges.Add(charge);
            payments.Add(new Payment($"demo-studio-pay-{i}", studio.Id, 300m, charge.DueDate, charge.Id, null));
        }

        return (clients, charges, payments);
    }

    /// <summary>
    /// Fictitious subscriber accounts for the admin panel in local development: one pending, one active, one expired.
    /// Their passwords are random and discarded, so nobody can sign in with them.
    /// </summary>
    public static IReadOnlyList<Account> CreateAccounts(DateOnly today)
    {
        var created = DateTimeOffset.UtcNow;
        string Unusable() => PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

        return
        [
            new Account("demo-salon", "contato@salaobellavista.exemplo", "Salão Bella Vista", "(77) 90000-1001",
                Unusable(), AccountStatus.Pending, Plan.Starter, created, null, null),
            new Account("demo-petshop", "financeiro@amigofiel.exemplo", "Pet Shop Amigo Fiel", "(77) 90000-1002",
                Unusable(), AccountStatus.Active, Plan.Starter, created.AddDays(-40), today.AddDays(12), "Pix mensal"),
            new Account("demo-barber", "navalha@barbearia.exemplo", "Barbearia Navalha", "(77) 90000-1003",
                Unusable(), AccountStatus.Active, Plan.Starter, created.AddDays(-70), today.AddDays(-3), null),
        ];
    }
}
