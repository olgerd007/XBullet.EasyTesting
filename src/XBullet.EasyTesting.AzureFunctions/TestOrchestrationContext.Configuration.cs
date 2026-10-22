using System.Collections.ObjectModel;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestOrchestrationContext
{
    /// <summary>Overrides the fallback activity dispatch callback.</summary>
    /// <param name="handler">The non-null handler used for activity names without a named registration.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithActivityHandler(Func<RecordedActivityCall, Task<object?>> handler)
    {
        ActivityHandler = handler;
        return this;
    }

    /// <summary>Configures the handler invoked for the activity named <paramref name="activityName"/>.</summary>
    /// <param name="activityName">The non-empty registered function name, matched case-sensitively with ordinal comparison.</param>
    /// <param name="handler">A non-null callback receiving the activity name and original input, returning its typed result.</param>
    /// <returns>This context, for chaining. Registering a name again replaces its handler for future calls.</returns>
    public TestOrchestrationContext WithActivityHandler(
        string activityName,
        Func<string, object?, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        ArgumentNullException.ThrowIfNull(handler);
        lock (_recordingGate)
        {
            _activityHandlers[activityName] = handler;
        }

        return this;
    }

    /// <summary>Overrides the child orchestration dispatch callback.</summary>
    /// <param name="handler">The non-null handler used for future recorded child calls.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithSubOrchestratorHandler(Func<RecordedSubOrchestrationCall, Task<object?>> handler)
    {
        SubOrchestratorHandler = handler;
        return this;
    }

    /// <summary>Overrides the orchestration name, initially TestOrchestrator.</summary>
    /// <param name="name">A task name with a non-empty Name value.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithName(TaskName name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name.Name);
        _name = name;
        return this;
    }

    /// <summary>Overrides parent metadata, initially null.</summary>
    /// <param name="parent">The parent descriptor retained by reference; null clears it.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithParent(ParentOrchestrationInstance? parent)
    {
        _parent = parent;
        return this;
    }

    /// <summary>Overrides the orchestration version, initially empty.</summary>
    /// <param name="version">The non-null version; an empty string represents an unversioned execution.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        _version = version;
        return this;
    }

    /// <summary>Overrides configuration properties, initially empty.</summary>
    /// <param name="properties">A non-null dictionary shallow-copied into a read-only ordinal dictionary.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithProperties(IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _properties = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(properties, StringComparer.Ordinal));
        return this;
    }

    /// <summary>Overrides the default entity stub with a caller-supplied feature.</summary>
    /// <param name="entities">The non-null caller-owned feature. The context does not dispose it.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithEntities(TaskOrchestrationEntityFeature entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _entities = entities;
        return this;
    }

    /// <summary>Overrides GUID generation for future calls.</summary>
    /// <param name="generator">A non-null generator invoked once per NewGuid call.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithGuidGenerator(Func<Guid> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _guidGenerator = generator;
        return this;
    }

    /// <summary>Overrides ordinal version ordering with a custom comparison.</summary>
    /// <param name="comparer">A non-null callback receiving the current version and comparison version.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithVersionComparer(Func<string, string, int> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        _versionComparer = comparer;
        return this;
    }

    /// <summary>Sets initial custom status using the same recording behavior as SetCustomStatus.</summary>
    /// <param name="status">The retained status object; null clears it.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithCustomStatus(object? status)
    {
        SetCustomStatus(status);
        return this;
    }

    /// <summary>Overrides the callback invoked after each custom-status update.</summary>
    /// <param name="handler">A non-null callback. Callback failures propagate after the status is recorded.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithCustomStatusHandler(Action<object?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _customStatusHandler = handler;
        return this;
    }

    /// <summary>Overrides the callback invoked after each continuation request is recorded.</summary>
    /// <param name="handler">A non-null callback. Callback failures propagate; no automatic restart occurs.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithContinueAsNewHandler(Action<ContinueAsNewOptions> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _continueAsNewHandler = handler;
        return this;
    }
}
