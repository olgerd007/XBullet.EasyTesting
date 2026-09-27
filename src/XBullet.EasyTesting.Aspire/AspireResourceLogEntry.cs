namespace XBullet.EasyTesting.Aspire;

/// <summary>One console line emitted by an Aspire resource.</summary>
public sealed class AspireResourceLogEntry
{
    /// <summary>Creates an immutable resource log entry.</summary>
    /// <param name="lineNumber">The line number reported by Aspire.</param>
    /// <param name="content">The log-line content. Empty content is accepted.</param>
    /// <param name="isError"><see langword="true"/> for standard-error output; otherwise, <see langword="false"/>.</param>
    public AspireResourceLogEntry(int lineNumber, string content, bool isError)
    {
        ArgumentNullException.ThrowIfNull(content);
        LineNumber = lineNumber;
        Content = content;
        IsError = isError;
    }

    /// <summary>Gets the resource stream line number.</summary>
    /// <value>The line number reported by Aspire.</value>
    public int LineNumber { get; }

    /// <summary>Gets the console content.</summary>
    /// <value>The log-line content, which can be empty and is not redacted.</value>
    public string Content { get; }

    /// <summary>Gets whether the line came from standard error.</summary>
    /// <value><see langword="true"/> for standard-error output; otherwise, <see langword="false"/>.</value>
    public bool IsError { get; }
}
