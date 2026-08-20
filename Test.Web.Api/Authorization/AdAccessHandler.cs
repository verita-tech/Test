using Microsoft.AspNetCore.Authorization;
using Test.Web.Api.Services;

namespace Test.Web.Api.Authorization;

public sealed class AdAccessHandler(IAdAccessChecker checker) : AuthorizationHandler<AdAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdAccessRequirement requirement)
    {
        if (checker.IsAuthorized(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
