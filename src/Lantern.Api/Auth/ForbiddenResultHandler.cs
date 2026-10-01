using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Lantern.Api.Auth;

/// <summary>Turns authorization failures into a ProblemDetails 403 that names the policy (scenario C demo).</summary>
public sealed class ForbiddenResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var names = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy).OfType<string>().ToArray() ?? [];
            await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You don't have permission to do this.",
                    extensions: new Dictionary<string, object?> { ["policy"] = string.Join(",", names) })
                .ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
