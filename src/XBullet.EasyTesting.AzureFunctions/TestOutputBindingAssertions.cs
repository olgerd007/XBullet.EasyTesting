namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Fluent assertions for output bindings captured during an invocation.</summary>
public sealed class TestOutputBindingAssertions
{
    private readonly TestFunctionBindings _bindings;

    internal TestOutputBindingAssertions(TestFunctionBindings bindings) => _bindings = bindings;

    /// <summary>Requires exactly <paramref name="expected"/> captured output bindings.</summary>
    public TestOutputBindingAssertions HaveCount(int expected)
    {
        if (_bindings.Outputs.Count != expected)
        {
            throw Failure($"Expected {expected} output binding(s), but captured {_bindings.Outputs.Count}: {Names()}.");
        }

        return this;
    }

    /// <summary>Requires an output binding with the supplied name.</summary>
    public TestOutputBindingAssertions Contain(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_bindings.Outputs.ContainsKey(name))
        {
            throw Failure($"Expected output binding '{name}', but captured: {Names()}.");
        }

        return this;
    }

    /// <summary>Requires an output binding to equal the supplied value.</summary>
    public TestOutputBindingAssertions HaveValue<T>(string name, T expected)
    {
        Contain(name);
        var actual = _bindings.Outputs[name];
        var matches = actual is null
            ? expected is null
            : actual is T typedActual && EqualityComparer<T>.Default.Equals(typedActual, expected);
        if (!matches)
        {
            throw Failure(
                $"Expected output binding '{name}' to equal '{expected}', but it was '{actual}'.");
        }

        return this;
    }

    /// <summary>Requires that an output binding was not captured.</summary>
    public TestOutputBindingAssertions NotContain(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_bindings.Outputs.ContainsKey(name))
        {
            throw Failure($"Did not expect output binding '{name}', but it was captured.");
        }

        return this;
    }

    private string Names() =>
        _bindings.Outputs.Count == 0
            ? "<none>"
            : string.Join(", ", _bindings.Outputs.Keys.Order(StringComparer.OrdinalIgnoreCase));

    private static TestOutputBindingVerificationException Failure(string message) => new(message);
}

/// <summary>Thrown when captured output bindings do not match an assertion.</summary>
public sealed class TestOutputBindingVerificationException(string message) : Exception(message);
