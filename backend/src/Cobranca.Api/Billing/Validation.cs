using Cobranca.Domain;

namespace Cobranca.Api.Billing;

/// <summary>Input validation. Messages are in Portuguese because they are shown to the end user.</summary>
internal static class Validation
{
    public const decimal MaxAmount = 1_000_000m;

    public static Dictionary<string, string[]> ForClient(ClientInput input)
    {
        var errors = new Dictionary<string, string[]>();
        Required(errors, "name", input.Name, "Informe o nome do cliente.");
        MaxLength(errors, "name", input.Name, 120);
        MaxLength(errors, "phone", input.Phone, 30);
        MaxLength(errors, "notes", input.Notes, 1000);
        return errors;
    }

    public static Dictionary<string, string[]> ForCharge(ChargeInput input, bool allowPlan)
    {
        var errors = new Dictionary<string, string[]>();
        Required(errors, "serviceName", input.ServiceName, "Informe o serviço.");
        MaxLength(errors, "serviceName", input.ServiceName, 120);
        MaxLength(errors, "description", input.Description, 1000);
        if (input.Reference is null)
        {
            errors["reference"] = ["Informe o mês de referência."];
        }

        if (input.DueDate is null)
        {
            errors["dueDate"] = ["Informe o vencimento."];
        }

        Amount(errors, input.Amount);
        if (allowPlan)
        {
            Plan(errors, input);
        }

        return errors;
    }

    public static Dictionary<string, string[]> ForPayment(PaymentInput input)
    {
        var errors = new Dictionary<string, string[]>();
        Amount(errors, input.Amount);
        if (input.PaidOn is null)
        {
            errors["paidOn"] = ["Informe a data do pagamento."];
        }

        MaxLength(errors, "note", input.Note, 500);
        return errors;
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Required(Dictionary<string, string[]> errors, string field, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [message];
        }
    }

    private static void MaxLength(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (value is not null && value.Trim().Length > max && !errors.ContainsKey(field))
        {
            errors[field] = [$"Use no máximo {max} caracteres."];
        }
    }

    private static void Plan(Dictionary<string, string[]> errors, ChargeInput input)
    {
        var kind = input.Kind ?? ChargeKind.OneOff;

        if (input.RepeatUntil is { } until)
        {
            if (kind != ChargeKind.Recurring)
            {
                errors["repeatUntil"] = ["A repetição mensal só vale para mensalidades."];
            }
            else if (input.Reference is { } reference)
            {
                var months = ChargePlanner.CountMonths(reference, until);
                if (months < 1)
                {
                    errors["repeatUntil"] = ["O mês final deve ser igual ou posterior à referência."];
                }
                else if (months > ChargePlanner.MaxCharges)
                {
                    errors["repeatUntil"] = [$"Gere no máximo {ChargePlanner.MaxCharges} meses de uma vez."];
                }
            }
        }

        if (input.Installments is { } installments)
        {
            if (kind != ChargeKind.OneOff)
            {
                errors["installments"] = ["O parcelamento só vale para serviços avulsos."];
            }
            else if (installments < 2 || installments > ChargePlanner.MaxCharges)
            {
                errors["installments"] = [$"Informe de 2 a {ChargePlanner.MaxCharges} parcelas."];
            }
            else if (input.Amount is { } amount && amount / installments < 0.01m)
            {
                errors["installments"] = ["Cada parcela precisa ter pelo menos R$ 0,01."];
            }
        }
    }

    private static void Amount(Dictionary<string, string[]> errors, decimal? amount)
    {
        if (amount is not { } value || value <= 0m)
        {
            errors["amount"] = ["Informe um valor maior que zero."];
        }
        else if (value > MaxAmount)
        {
            errors["amount"] = ["Valor acima do limite permitido."];
        }
        else if (decimal.Round(value, 2) != value)
        {
            errors["amount"] = ["Use no máximo duas casas decimais."];
        }
    }
}
