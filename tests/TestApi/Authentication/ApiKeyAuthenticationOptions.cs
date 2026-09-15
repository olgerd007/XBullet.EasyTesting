using Microsoft.AspNetCore.Authentication;

namespace TestApi.Authentication;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Api-Key";

    public string QueryParameterName { get; set; } = "api_key";

    public IDictionary<string, string> ValidKeys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
