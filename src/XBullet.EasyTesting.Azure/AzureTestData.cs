using Azure;

namespace XBullet.EasyTesting.Azure;

/// <summary>Creates common Azure SDK response and paging values without a mocking framework.</summary>
public static class AzureTestData
{
    /// <summary>Creates a raw Azure response.</summary>
    public static TestAzureResponse Response(int status = 200, string? reasonPhrase = null) =>
        new(status, reasonPhrase);

    /// <summary>Creates a model response with a configurable raw response.</summary>
    public static Response<T> ModelResponse<T>(
        T value,
        int status = 200,
        Action<TestAzureResponse>? configure = null)
    {
        var response = new TestAzureResponse(status);
        configure?.Invoke(response);
        return response.FromValue(value);
    }

    /// <summary>Creates one Azure result page.</summary>
    public static Page<T> Page<T>(
        IEnumerable<T> values,
        string? continuationToken = null,
        Response? response = null)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        return global::Azure.Page<T>.FromValues(
            values.ToArray(),
            continuationToken,
            response ?? new TestAzureResponse());
    }

    /// <summary>Creates a synchronous pageable sequence from pages.</summary>
    public static Pageable<T> Pageable<T>(params Page<T>[] pages)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(pages);
        return global::Azure.Pageable<T>.FromPages(pages);
    }

    /// <summary>Creates an asynchronous pageable sequence from pages.</summary>
    public static AsyncPageable<T> AsyncPageable<T>(params Page<T>[] pages)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(pages);
        return global::Azure.AsyncPageable<T>.FromPages(pages);
    }
}
