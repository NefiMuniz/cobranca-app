using Cobranca.Api.Storage;
using Cobranca.Domain;

namespace Cobranca.Api.Accounts;

/// <summary>
/// Runs on every billing request. A token stays valid for hours, so expiry and suspension are re-checked
/// against the stored account each time: suspending an account cuts access on its very next request.
/// </summary>
public sealed class ActiveAccountFilter(IAccountRepository accounts, Clock clock) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.IsAdmin())
        {
            return await next(context);
        }

        var account = await accounts.GetByIdAsync(user.TenantId(), context.HttpContext.RequestAborted);
        if (account is null)
        {
            return Results.Unauthorized();
        }

        var state = AccessPolicy.StateOf(account, clock.Today);
        return state == AccessState.Active
            ? await next(context)
            : AccessProblems.For(state, account.AccessUntil);
    }
}
