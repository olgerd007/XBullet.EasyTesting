using System.Text.Json;

namespace XBullet.EasyTesting.Http;

internal sealed class StubRequestMatchContext(StubHttpRequest request) : IDisposable
{
    private JsonDocument? _jsonDocument;
    private bool _jsonParseAttempted;

    public StubHttpRequest Request { get; } = request;

    public bool TryGetJsonRoot(out JsonElement root)
    {
        if (!_jsonParseAttempted)
        {
            _jsonParseAttempted = true;
            if (Request.Body is not null)
            {
                try
                {
                    _jsonDocument = JsonDocument.Parse(Request.Body);
                }
                catch (JsonException)
                {
                }
            }
        }

        if (_jsonDocument is null)
        {
            root = default;
            return false;
        }

        root = _jsonDocument.RootElement;
        return true;
    }

    public void Dispose() => _jsonDocument?.Dispose();
}
