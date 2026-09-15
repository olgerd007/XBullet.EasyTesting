using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

internal static class JsonOptions
{
    internal static JsonSerializerOptions Default { get; } =
        new(JsonSerializerDefaults.Web);
}
