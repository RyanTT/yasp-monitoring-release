using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Security.Claims;
using System.Text.Encodings.Web;

using YASP.Server.Application.Options;

namespace YASP.Server.Application.Authentication
{
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly IOptions<RootOptions> _rootOptions;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IOptions<RootOptions> rootOptions) : base(options, logger, encoder, clock)
        {
            _rootOptions = rootOptions;
        }

        /// <summary>
        /// Handles authentication based on the required api key.
        /// </summary>
        /// <returns>AuthenticationResult</returns>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(ApiKeyAuthenticationOptions.HeaderName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var apiKey = Request.Headers[ApiKeyAuthenticationOptions.HeaderName].First();

            if (apiKey.ToLowerInvariant() != _rootOptions.Value.Cluster.ApiKey)
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid api key."));
            }

            var claimsIdentity = new ClaimsIdentity(ApiKeyAuthenticationOptions.Scheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), ApiKeyAuthenticationOptions.Scheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
