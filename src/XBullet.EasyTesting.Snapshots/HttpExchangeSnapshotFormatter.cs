using System.Text;
using System.Text.Json;

namespace XBullet.EasyTesting.Snapshots;

internal static class HttpExchangeSnapshotFormatter
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true
    };

    public static string Format(string json, HttpExchangeSnapshotFormat format) => format switch
    {
        HttpExchangeSnapshotFormat.Json => json,
        HttpExchangeSnapshotFormat.Http => FormatHttp(json),
        HttpExchangeSnapshotFormat.Yaml => FormatYaml(json),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown HTTP exchange snapshot format.")
    };

    private static string FormatHttp(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder();
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
            {
                return "=== Exchanges ===\n<none>";
            }

            var index = 0;
            foreach (var exchange in root.EnumerateArray())
            {
                if (index > 0)
                {
                    builder.AppendLine();
                }

                builder.Append("=== Exchange ")
                    .Append(++index)
                    .AppendLine(" ===")
                    .AppendLine();
                WriteHttpExchange(builder, exchange);
            }
        }
        else
        {
            WriteHttpExchange(builder, root);
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static void WriteHttpExchange(StringBuilder builder, JsonElement exchange)
    {
        if (exchange.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("An HTTP exchange snapshot must serialize as an object or an array of objects.");
        }

        WriteRequest(builder, GetProperty(exchange, "Request"));
        builder.AppendLine();
        WriteResponse(builder, GetProperty(exchange, "Response"));

        var failure = GetProperty(exchange, "Failure");
        if (failure is { ValueKind: not JsonValueKind.Null })
        {
            builder.AppendLine();
            WriteFailure(builder, "Failure", failure.Value);
        }
    }

    private static void WriteRequest(StringBuilder builder, JsonElement? request)
    {
        builder.AppendLine("=== Request ===");
        if (request is null || request.Value.ValueKind == JsonValueKind.Null)
        {
            builder.AppendLine("<none>");
            return;
        }

        var method = GetString(request.Value, "Method") ?? "<unknown-method>";
        var url = GetString(request.Value, "Url") ?? "<unknown-url>";
        builder.Append(method).Append(' ').AppendLine(url);
        WriteHeaders(builder, GetProperty(request.Value, "Headers"));
        WriteBody(builder, GetProperty(request.Value, "Body"), GetContentType(request.Value));
    }

    private static void WriteResponse(StringBuilder builder, JsonElement? response)
    {
        builder.AppendLine("=== Response ===");
        if (response is null || response.Value.ValueKind == JsonValueKind.Null)
        {
            builder.AppendLine("<none>");
            return;
        }

        var statusCode = GetProperty(response.Value, "StatusCode");
        builder.Append(statusCode?.GetRawText() ?? "<unknown-status>");
        var reasonPhrase = GetString(response.Value, "ReasonPhrase");
        if (!string.IsNullOrEmpty(reasonPhrase))
        {
            builder.Append(' ').Append(reasonPhrase);
        }

        builder.AppendLine();
        WriteHeaders(builder, GetProperty(response.Value, "Headers"));
        WriteBody(builder, GetProperty(response.Value, "Body"), GetContentType(response.Value));

        var bodyFailure = GetProperty(response.Value, "BodyFailure");
        if (bodyFailure is { ValueKind: not JsonValueKind.Null })
        {
            builder.AppendLine();
            WriteFailure(builder, "Response Body Failure", bodyFailure.Value);
        }
    }

    private static void WriteHeaders(StringBuilder builder, JsonElement? headers)
    {
        if (headers is null || headers.Value.ValueKind == JsonValueKind.Null)
        {
            builder.AppendLine("# Headers: <excluded>");
            return;
        }

        foreach (var header in headers.Value.EnumerateObject())
        {
            if (header.Value.ValueKind != JsonValueKind.Array || header.Value.GetArrayLength() == 0)
            {
                builder.Append(header.Name).AppendLine(":");
                continue;
            }

            foreach (var value in header.Value.EnumerateArray())
            {
                builder.Append(header.Name)
                    .Append(": ")
                    .AppendLine(EscapeHeaderValue(value.GetString() ?? string.Empty));
            }
        }
    }

    private static void WriteBody(
        StringBuilder builder,
        JsonElement? body,
        string? contentType)
    {
        if (body is null || body.Value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        builder.AppendLine();
        if (IsBinaryBody(body.Value, contentType, out var encoding, out var value))
        {
            builder.Append("# Body encoding: ").AppendLine(encoding);
            builder.AppendLine(value);
            return;
        }

        if (body.Value.ValueKind == JsonValueKind.String && !IsJson(contentType))
        {
            AppendText(builder, body.Value.GetString() ?? string.Empty);
            return;
        }

        builder.AppendLine(JsonSerializer.Serialize(body.Value, IndentedJsonOptions));
    }

    private static void WriteFailure(StringBuilder builder, string name, JsonElement failure)
    {
        builder.Append("=== ").Append(name).AppendLine(" ===");
        builder.Append("Type: ").AppendLine(GetString(failure, "Type") ?? "<unknown>");
        builder.Append("Message: ")
            .AppendLine(JsonSerializer.Serialize(GetString(failure, "Message") ?? string.Empty));
    }

    private static void AppendText(StringBuilder builder, string value)
    {
        builder.Append(value);
        if (!value.EndsWith('\n'))
        {
            builder.AppendLine();
        }
    }

    private static string EscapeHeaderValue(string value) => value
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static bool IsBinaryBody(
        JsonElement body,
        string? contentType,
        out string encoding,
        out string value)
    {
        encoding = string.Empty;
        value = string.Empty;
        if (IsJson(contentType) || IsText(contentType) || body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        encoding = GetString(body, "Encoding") ?? string.Empty;
        value = GetString(body, "Value") ?? string.Empty;
        return encoding.Length > 0 && GetProperty(body, "Value") is not null;
    }

    private static string? GetContentType(JsonElement message)
    {
        var headers = GetProperty(message, "Headers");
        if (headers is null || headers.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var contentType = GetProperty(headers.Value, "Content-Type");
        if (contentType is null || contentType.Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var value in contentType.Value.EnumerateArray())
        {
            return value.GetString();
        }

        return null;
    }

    private static bool IsJson(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType is not null &&
            (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
             mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsText(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true ||
            contentType?.Contains("charset=", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string FormatYaml(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder();
        WriteYamlNode(builder, document.RootElement, 0);
        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static void WriteYamlNode(StringBuilder builder, JsonElement element, int indentation)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            WriteYamlObject(builder, element, indentation);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            WriteYamlArray(builder, element, indentation);
        }
        else
        {
            AppendIndentation(builder, indentation);
            builder.AppendLine(FormatYamlScalar(element));
        }
    }

    private static void WriteYamlObject(StringBuilder builder, JsonElement element, int indentation)
    {
        if (!element.EnumerateObject().Any())
        {
            AppendIndentation(builder, indentation);
            builder.AppendLine("{}");
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            AppendIndentation(builder, indentation);
            builder.Append(FormatYamlKey(property.Name)).Append(':');
            if (IsYamlBlock(property.Value))
            {
                builder.AppendLine();
                WriteYamlNode(builder, property.Value, indentation + 2);
            }
            else
            {
                builder.Append(' ').AppendLine(FormatYamlScalar(property.Value));
            }
        }
    }

    private static void WriteYamlArray(StringBuilder builder, JsonElement element, int indentation)
    {
        if (element.GetArrayLength() == 0)
        {
            AppendIndentation(builder, indentation);
            builder.AppendLine("[]");
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            AppendIndentation(builder, indentation);
            builder.Append('-');
            if (IsYamlBlock(item))
            {
                builder.AppendLine();
                WriteYamlNode(builder, item, indentation + 2);
            }
            else
            {
                builder.Append(' ').AppendLine(FormatYamlScalar(item));
            }
        }
    }

    private static bool IsYamlBlock(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any() ||
        element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0;

    private static string FormatYamlScalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Object => "{}",
        JsonValueKind.Array => "[]",
        _ => throw new InvalidOperationException($"Unsupported JSON value kind '{element.ValueKind}'.")
    };

    private static string FormatYamlKey(string value)
    {
        if (value.Length > 0 &&
            !IsYamlKeyword(value) &&
            (char.IsLetter(value[0]) || value[0] == '_') &&
            value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-'))
        {
            return value;
        }

        return JsonSerializer.Serialize(value);
    }

    private static bool IsYamlKeyword(string value) =>
        value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static void AppendIndentation(StringBuilder builder, int indentation) =>
        builder.Append(' ', indentation);

    private static string? GetString(JsonElement element, string name)
    {
        var property = GetProperty(element, name);
        return property is { ValueKind: JsonValueKind.String }
            ? property.Value.GetString()
            : null;
    }

    private static JsonElement? GetProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
