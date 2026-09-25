namespace XBullet.EasyTesting.Snapshots;

internal static class SensitiveQueryParameterDefaults
{
    private static readonly string[] ParameterNames =
    [
        "access_token",
        "api_key",
        "apikey",
        "client_secret",
        "code",
        "key",
        "password",
        "sas",
        "secret",
        "sig",
        "signature",
        "token"
    ];

    internal static HashSet<string> Create() =>
        new(ParameterNames, StringComparer.OrdinalIgnoreCase);
}
