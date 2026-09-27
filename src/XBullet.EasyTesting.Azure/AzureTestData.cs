using Azure;

namespace XBullet.EasyTesting.Azure;

/// <summary>Creates common Azure SDK response and paging values without a mocking framework.</summary>
public static class AzureTestData
{
    /// <summary>Creates a raw Azure response.</summary>
    /// <param name="status">The HTTP status code from 100 through 599. The default is 200.</param>
    /// <param name="reasonPhrase">
    /// The reason phrase, or <see langword="null"/> to derive a standard phrase from
    /// <paramref name="status"/>. Unknown status codes receive an empty phrase.
    /// </param>
    /// <returns>A new mutable response that the caller owns and should dispose after use.</returns>
    public static TestAzureResponse Response(int status = 200, string? reasonPhrase = null) =>
        new(status, reasonPhrase);

    /// <summary>Creates a model response with a configurable raw response.</summary>
    /// <typeparam name="T">The type of model value carried by the response.</typeparam>
    /// <param name="value">The model value to retain in the response; nullable values are accepted when <typeparamref name="T"/> permits them.</param>
    /// <param name="status">The raw HTTP status code from 100 through 599. The default is 200.</param>
    /// <param name="configure">
    /// An optional callback invoked synchronously once with the new raw response before it is wrapped.
    /// The callback may mutate the response but must not dispose it.
    /// </param>
    /// <returns>
    /// A model response containing <paramref name="value"/> and the configured raw response. Dispose
    /// the raw response returned by <c>GetRawResponse()</c> after use.
    /// </returns>
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
    /// <typeparam name="T">The non-null item type contained in the page.</typeparam>
    /// <param name="values">
    /// A non-null sequence of page values. The sequence is enumerated immediately into a stable array.
    /// </param>
    /// <param name="continuationToken">
    /// The token for retrieving the next page, or <see langword="null"/> for a terminal page.
    /// </param>
    /// <param name="response">
    /// The optional raw response associated with the page. When <see langword="null"/>, a new 200
    /// response is created. A supplied response is retained, and its caller retains disposal ownership.
    /// </param>
    /// <returns>A page containing the materialized values, continuation token, and raw response.</returns>
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
    /// <typeparam name="T">The non-null item type contained in the pages.</typeparam>
    /// <param name="pages">
    /// The non-null ordered array of pages to expose. An empty array produces an empty pageable sequence.
    /// Page instances and their raw responses remain caller-owned.
    /// </param>
    /// <returns>A synchronous pageable sequence that yields the supplied pages in order.</returns>
    public static Pageable<T> Pageable<T>(params Page<T>[] pages)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(pages);
        return global::Azure.Pageable<T>.FromPages(pages);
    }

    /// <summary>Creates an asynchronous pageable sequence from pages.</summary>
    /// <typeparam name="T">The non-null item type contained in the pages.</typeparam>
    /// <param name="pages">
    /// The non-null ordered array of pages to expose. An empty array produces an empty pageable sequence.
    /// Page instances and their raw responses remain caller-owned.
    /// </param>
    /// <returns>An asynchronous pageable sequence that yields the supplied pages in order.</returns>
    public static AsyncPageable<T> AsyncPageable<T>(params Page<T>[] pages)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(pages);
        return global::Azure.AsyncPageable<T>.FromPages(pages);
    }
}
