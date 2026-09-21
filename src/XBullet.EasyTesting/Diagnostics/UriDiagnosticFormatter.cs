namespace XBullet.EasyTesting.Diagnostics;

internal static class UriDiagnosticFormatter
{
    private const string RedactedValue = "{Redacted}";

    private static readonly HashSet<string> SensitiveQueryParameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
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
            "token",

            // Azure shared access signature fields. Redact the complete SAS rather than
            // attempting to distinguish the signature from its authorization scope.
            "rscc",
            "rscd",
            "rsce",
            "rscl",
            "rsct",
            "saoid",
            "scid",
            "sdd",
            "se",
            "ses",
            "si",
            "sip",
            "ske",
            "skn",
            "skoid",
            "sks",
            "skt",
            "sktid",
            "skv",
            "sp",
            "spr",
            "sr",
            "srt",
            "ss",
            "st",
            "suoid",
            "sv"
        };

    public static string Format(Uri? uri)
    {
        if (uri is null)
        {
            return "<no URI>";
        }

        try
        {
            return RedactQueryValues(uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString);
        }
        catch (Exception exception) when (exception is UriFormatException or InvalidOperationException)
        {
            return "<malformed URI>";
        }
    }

    public static string Format(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return "<no URI>";
        }

        if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsedUri))
        {
            return "<malformed URI>";
        }

        return Format(parsedUri);
    }

    public static bool IsSensitiveQueryParameter(string name) =>
        SensitiveQueryParameters.Contains(name);

    private static string RedactQueryValues(string value)
    {
        var queryIndex = value.IndexOf('?');
        if (queryIndex < 0)
        {
            return value;
        }

        var fragmentIndex = value.IndexOf('#', queryIndex + 1);
        var queryEnd = fragmentIndex < 0 ? value.Length : fragmentIndex;
        var query = value[(queryIndex + 1)..queryEnd];
        var redacted = query.Split('&').Select(RedactQueryParameter);
        var fragment = fragmentIndex < 0 ? string.Empty : value[fragmentIndex..];
        return $"{value[..(queryIndex + 1)]}{string.Join('&', redacted)}{fragment}";
    }

    private static string RedactQueryParameter(string parameter)
    {
        var equalsIndex = parameter.IndexOf('=');
        var encodedName = equalsIndex < 0 ? parameter : parameter[..equalsIndex];
        if (!TryDecodeQueryComponent(encodedName, out var name))
        {
            return $"<malformed query parameter>={RedactedValue}";
        }

        if (!IsSensitiveQueryParameter(name) || equalsIndex < 0)
        {
            return parameter;
        }

        return $"{encodedName}={RedactedValue}";
    }

    private static bool TryDecodeQueryComponent(string value, out string decoded)
    {
        try
        {
            decoded = Uri.UnescapeDataString(value.Replace('+', ' '));
            return true;
        }
        catch (UriFormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }
}
