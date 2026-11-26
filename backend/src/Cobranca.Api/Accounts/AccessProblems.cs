using Cobranca.Domain;

namespace Cobranca.Api.Accounts;

/// <summary>403 responses for accounts that cannot use the app right now, with a machine-readable code for the UI.</summary>
internal static class AccessProblems
{
    public static IResult For(AccessState state, DateOnly? accessUntil)
    {
        var (code, title) = state switch
        {
            AccessState.Pending => ("pending", "Sua solicitação está aguardando aprovação."),
            AccessState.Expired when accessUntil is { } until =>
                ("expired", $"Seu acesso expirou em {until:dd/MM/yyyy}. Fale com o suporte para renovar."),
            AccessState.Expired => ("expired", "Seu acesso ainda não foi liberado. Fale com o suporte."),
            AccessState.Suspended => ("suspended", "Seu acesso está suspenso. Fale com o suporte."),
            _ => ("rejected", "Sua solicitação de acesso não foi aprovada."),
        };

        return Results.Problem(
            title: title,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
