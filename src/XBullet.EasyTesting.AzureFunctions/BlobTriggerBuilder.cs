using System.Text;
using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Builds a Blob Storage trigger stream and blob metadata.</summary>
public sealed class BlobTriggerBuilder
{
    private readonly Dictionary<string, object?> _metadata =
        new(StringComparer.OrdinalIgnoreCase);
    private string _bindingName = "blob";
    private byte[] _content = [];

    /// <summary>Sets the input binding name.</summary>
    /// <param name="bindingName">The non-empty worker input name. The default is <c>blob</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public BlobTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Sets UTF-8 blob content.</summary>
    /// <param name="content">The non-null text encoded immediately as UTF-8; empty text is accepted.</param>
    /// <returns>This builder, for chaining, replacing prior content.</returns>
    public BlobTriggerBuilder WithContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = Encoding.UTF8.GetBytes(content);
        return this;
    }

    /// <summary>Sets binary blob content.</summary>
    /// <param name="content">The non-null bytes copied immediately; an empty array is accepted.</param>
    /// <returns>This builder, for chaining, replacing prior content.</returns>
    public BlobTriggerBuilder WithContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = [.. content];
        return this;
    }

    /// <summary>Serializes a value as UTF-8 JSON blob content.</summary>
    /// <typeparam name="T">The type of value serialized with the package's web JSON defaults.</typeparam>
    /// <param name="value">The value serialized immediately; null is emitted when permitted by <typeparamref name="T"/>.</param>
    /// <returns>This builder, for chaining, replacing prior content.</returns>
    public BlobTriggerBuilder WithJsonContent<T>(T value)
    {
        _content = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions.Default);
        return this;
    }

    /// <summary>Sets the blob path exposed as binding data.</summary>
    /// <param name="path">The non-empty path stored as both <c>BlobTrigger</c> and <c>Uri</c> metadata.</param>
    /// <returns>This builder, for chaining.</returns>
    public BlobTriggerBuilder WithPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _metadata["BlobTrigger"] = path;
        _metadata["Uri"] = path;
        return this;
    }

    /// <summary>Adds blob binding metadata.</summary>
    /// <param name="name">The non-empty metadata key, matched case-insensitively.</param>
    /// <param name="value">The value retained without cloning; <see langword="null"/> is accepted.</param>
    /// <returns>This builder, for chaining. An existing key is replaced.</returns>
    public BlobTriggerBuilder WithMetadata(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _metadata[name] = value;
        return this;
    }

    /// <summary>Builds trigger data whose value can be passed to a stream trigger parameter.</summary>
    /// <returns>
    /// New trigger data containing a caller-owned, read-only memory stream positioned at zero and the
    /// current metadata. Dispose the stream after invocation.
    /// </returns>
    public TestTriggerData<Stream> Build() =>
        new(new MemoryStream(_content, writable: false), _bindingName, "blobTrigger", _metadata);
}
