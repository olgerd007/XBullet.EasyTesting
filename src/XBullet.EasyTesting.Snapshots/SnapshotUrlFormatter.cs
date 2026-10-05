namespace XBullet.EasyTesting.Snapshots;

internal static class SnapshotUrlFormatter
{
    internal static string? Format(
        Uri? uri,
        ISet<string> redactedQueryParameters,
        ISet<string> scrubbedQueryParameters,
        IReadOnlyList<Func<string, string>> pathScrubbers)
    {
        if (uri is null)
        {
            return null;
        }

        var value = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
        var queryIndex = value.IndexOf('?');
        var fragmentIndex = value.IndexOf('#');
        var pathEnd = queryIndex >= 0
            ? queryIndex
            : fragmentIndex >= 0
                ? fragmentIndex
                : value.Length;
        var path = value[..pathEnd];

        foreach (var scrubber in pathScrubbers)
        {
            path = scrubber(path) ??
                throw new InvalidOperationException("A URL path scrubber returned null.");
        }

        if (queryIndex < 0)
        {
            return path + value[pathEnd..];
        }

        fragmentIndex = value.IndexOf('#', queryIndex + 1);
        var queryEnd = fragmentIndex < 0 ? value.Length : fragmentIndex;
        if (redactedQueryParameters.Count == 0 && scrubbedQueryParameters.Count == 0)
        {
            return path + value[pathEnd..];
        }

        var query = value[(queryIndex + 1)..queryEnd];
        var scrubbed = query.Split('&').Select(segment =>
        {
            var equalsIndex = segment.IndexOf('=');
            var encodedName = equalsIndex < 0 ? segment : segment[..equalsIndex];
            var name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));
            if (redactedQueryParameters.Contains(name))
            {
                return $"{encodedName}={{Redacted}}";
            }

            return scrubbedQueryParameters.Contains(name)
                ? $"{encodedName}={{Scrubbed}}"
                : segment;
        });

        var fragment = fragmentIndex < 0 ? string.Empty : value[fragmentIndex..];
        return $"{path}?{string.Join('&', scrubbed)}{fragment}";
    }

    internal static string ScrubGuidsInUrl(string url, Func<int, string, bool>? shouldScrub = null)
    {
        var pathEnd = url.IndexOfAny(['?', '#']);
        if (pathEnd < 0)
        {
            pathEnd = url.Length;
        }

        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        var authorityStart = schemeEnd > 0 && schemeEnd < pathEnd && Uri.CheckSchemeName(url[..schemeEnd])
            ? schemeEnd + 3
            : url.StartsWith("//", StringComparison.Ordinal) ? 2 : 0;
        var pathStart = authorityStart == 0 ? 0 : url.IndexOf('/', authorityStart);
        if (pathStart < 0 || pathStart >= pathEnd)
        {
            return url;
        }

        return url[..pathStart] + ScrubGuidsInPath(url[pathStart..pathEnd], shouldScrub) + url[pathEnd..];
    }

    internal static string ScrubGuidsInPath(string path) => ScrubGuidsInPath(path, shouldScrub: null);

    private static string ScrubGuidsInPath(string path, Func<int, string, bool>? shouldScrub)
    {
        var segments = path.Split('/');
        var firstSegment = path.StartsWith("/", StringComparison.Ordinal) ? 1 : 0;
        for (var index = firstSegment; index < segments.Length; index++)
        {
            if (Guid.TryParse(segments[index], out _) &&
                (shouldScrub is null || shouldScrub(index - firstSegment, segments[index])))
            {
                segments[index] = "{Guid}";
            }
        }

        return string.Join('/', segments);
    }
}
