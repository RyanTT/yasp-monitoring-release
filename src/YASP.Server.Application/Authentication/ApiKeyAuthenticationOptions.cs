using Microsoft.AspNetCore.Authentication;

namespace YASP.Server.Application.Authentication
{
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        public readonly static string HeaderName = "x-api-key";
        public readonly static string Scheme = "ApiKeyAuthenticationScheme";
    }
}
