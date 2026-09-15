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
    public BlobTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Sets UTF-8 blob content.</summary>
    public BlobTriggerBuilder WithContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = Encoding.UTF8.GetBytes(content);
        return this;
    }

    /// <summary>Sets binary blob content.</summary>
    public BlobTriggerBuilder WithContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = [.. content];
        return this;
    }

    /// <summary>Serializes a value as UTF-8 JSON blob content.</summary>
    public BlobTriggerBuilder WithJsonContent<T>(T value)
    {
        _content = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions.Default);
        return this;
    }

    /// <summary>Sets the blob path exposed as binding data.</summary>
    public BlobTriggerBuilder WithPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _metadata["BlobTrigger"] = path;
        _metadata["Uri"] = path;
        return this;
    }

    /// <summary>Adds blob binding metadata.</summary>
    public BlobTriggerBuilder WithMetadata(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _metadata[name] = value;
        return this;
    }

    /// <summary>Builds trigger data whose value can be passed to a stream trigger parameter.</summary>
    public TestTriggerData<Stream> Build() =>
        new(new MemoryStream(_content, writable: false), _bindingName, "blobTrigger", _metadata);
}
