namespace XBullet.EasyTesting.Http;

/// <summary>Controls how much request and response data an HTTP stub retains.</summary>
public sealed class StubHttpMessageHandlerOptions
{
    /// <summary>
    /// Gets the maximum number of completed or pending exchanges retained in recording order, or
    /// <see langword="null"/> for no limit. Zero disables exchange retention.
    /// </summary>
    /// <value>A non-negative limit, or <see langword="null"/>. The default is unlimited.</value>
    public int? MaximumRecordedExchanges { get; init; }

    /// <summary>
    /// Gets the maximum number of request-body bytes decoded and retained per exchange, or
    /// <see langword="null"/> for no limit.
    /// </summary>
    /// <value>A non-negative byte limit, or <see langword="null"/>. The default is unlimited.</value>
    public int? MaximumRequestBodyBytes { get; init; }

    /// <summary>
    /// Gets the maximum number of response-body bytes retained per exchange, or
    /// <see langword="null"/> for no limit.
    /// </summary>
    /// <value>A non-negative byte limit, or <see langword="null"/>. The default is unlimited.</value>
    public int? MaximumResponseBodyBytes { get; init; }

    /// <summary>Gets whether request bodies are read and retained. The default is true.</summary>
    /// <value><see langword="true"/> to capture request bodies; otherwise, <see langword="false"/>.</value>
    public bool CaptureRequestBodies { get; init; } = true;

    /// <summary>Gets whether consumed response bodies are retained. The default is true.</summary>
    /// <value><see langword="true"/> to capture response bodies; otherwise, <see langword="false"/>.</value>
    public bool CaptureResponseBodies { get; init; } = true;

    internal void Validate()
    {
        ValidateNonNegative(MaximumRecordedExchanges, nameof(MaximumRecordedExchanges));
        ValidateNonNegative(MaximumRequestBodyBytes, nameof(MaximumRequestBodyBytes));
        ValidateNonNegative(MaximumResponseBodyBytes, nameof(MaximumResponseBodyBytes));
    }

    private static void ValidateNonNegative(int? value, string propertyName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(propertyName, "Capture limits must be zero or greater.");
        }
    }
}
