namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Result of executing a function through the test middleware pipeline.</summary>
public class TestFunctionInvocationResult
{
    internal TestFunctionInvocationResult(TestFunctionContext context, bool functionExecuted)
    {
        Context = context;
        FunctionExecuted = functionExecuted;
    }

    /// <summary>Gets the invocation context used by the function and middleware.</summary>
    /// <value>The caller-owned context after pipeline completion, including captured bindings and mutations.</value>
    public TestFunctionContext Context { get; }

    /// <summary>Indicates whether middleware reached the function delegate.</summary>
    /// <value><see langword="true"/> when the terminal function callback began; otherwise, <see langword="false"/>.</value>
    public bool FunctionExecuted { get; }
}

/// <summary>Result of executing a value-returning function through the test middleware pipeline.</summary>
/// <typeparam name="T">The function return type.</typeparam>
public sealed class TestFunctionInvocationResult<T> : TestFunctionInvocationResult
{
    internal TestFunctionInvocationResult(
        TestFunctionContext context,
        bool functionExecuted,
        T? result)
        : base(context, functionExecuted) => Result = result;

    /// <summary>Gets the value returned by the function, or the default value if middleware short-circuited.</summary>
    /// <value>
    /// The function's return value, or the default <typeparamref name="T"/> value when middleware did
    /// not execute it. Consult <see cref="TestFunctionInvocationResult.FunctionExecuted"/> to distinguish
    /// short-circuiting from a function that returned its type's default value.
    /// </value>
    public T? Result { get; }
}
