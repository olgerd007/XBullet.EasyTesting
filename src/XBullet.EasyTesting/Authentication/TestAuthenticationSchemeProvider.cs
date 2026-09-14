using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace XBullet.EasyTesting.Authentication;

internal sealed class TestAuthenticationSchemeProvider : AuthenticationSchemeProvider
{
    private readonly IReadOnlyDictionary<string, AuthenticationScheme> _testSchemes;

    public TestAuthenticationSchemeProvider(
        IOptions<AuthenticationOptions> options,
        IEnumerable<string> testSchemes)
        : base(options)
    {
        _testSchemes = testSchemes.ToDictionary(
            name => name,
            name => new AuthenticationScheme(
                name,
                $"Integration test ({name})",
                typeof(TestAuthenticationHandler)),
            StringComparer.Ordinal);
    }

    public override Task<AuthenticationScheme?> GetSchemeAsync(string name) =>
        _testSchemes.TryGetValue(name, out var scheme)
            ? Task.FromResult<AuthenticationScheme?>(scheme)
            : base.GetSchemeAsync(name);

    public override async Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync()
    {
        var applicationSchemes = await base.GetAllSchemesAsync();
        return applicationSchemes
            .Where(scheme => !_testSchemes.ContainsKey(scheme.Name))
            .Concat(_testSchemes.Values);
    }
}
