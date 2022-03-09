using Microsoft.AspNetCore.Authorization;

namespace YASP.Server.Application.Authentication
{
    public class CustomDenyAnonymousAuthorizationRequirement : AuthorizationHandler<CustomDenyAnonymousAuthorizationRequirement>, IAuthorizationRequirement
    {
        /// <summary>
        /// Forces the request to be authenticated.
        /// </summary>
        /// <param name="context">The authorization context.</param>
        /// <param name="requirement">The requirement to evaluate.</param>
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, CustomDenyAnonymousAuthorizationRequirement requirement)
        {
            var user = context.User;
            var userIsAnonymous =
                user?.Identity == null ||
                !user.Identities.Any(i => i.IsAuthenticated);
            if (!userIsAnonymous)
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
