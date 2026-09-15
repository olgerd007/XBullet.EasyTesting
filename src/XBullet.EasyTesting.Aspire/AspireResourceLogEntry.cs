namespace XBullet.EasyTesting.Aspire;

/// <summary>One console line emitted by an Aspire resource.</summary>
public sealed class AspireResourceLogEntry
{
    /// <summary>Creates an immutable resource log entry.</summary>
    public AspireResourceLogEntry(int lineNumber, string content, bool isError)
    {
        ArgumentNullException.ThrowIfNull(content);
        LineNumber = lineNumber;
        Content = content;
        IsError = isError;
    }

    /// <summary>Gets the resource stream line number.</summary>
    public int LineNumber { get; }

    /// <summary>Gets the console content.</summary>
    public string Content { get; }

    /// <summary>Gets whether the line came from standard error.</summary>
    public bool IsError { get; }
}
