using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Fluently builds timer-trigger input without a Functions runtime.</summary>
public sealed class TestTimerInfoBuilder
{
    private bool _isPastDue;
    private ScheduleStatus? _scheduleStatus;

    /// <summary>Marks the timer invocation as past due.</summary>
    /// <param name="isPastDue"><see langword="true"/> for a late invocation; otherwise, <see langword="false"/>. The default is true.</param>
    /// <returns>This builder, for chaining.</returns>
    public TestTimerInfoBuilder PastDue(bool isPastDue = true)
    {
        _isPastDue = isPastDue;
        return this;
    }

    /// <summary>Sets the timer schedule timestamps.</summary>
    /// <param name="last">The previous scheduled time; its <see cref="DateTime.Kind"/> is preserved.</param>
    /// <param name="next">The next scheduled time; ordering is not validated.</param>
    /// <param name="lastUpdated">
    /// The time the schedule was updated, or <see langword="null"/> to reuse <paramref name="last"/>.
    /// </param>
    /// <returns>This builder, for chaining, replacing prior schedule status.</returns>
    public TestTimerInfoBuilder WithSchedule(
        DateTime last,
        DateTime next,
        DateTime? lastUpdated = null)
    {
        _scheduleStatus = new ScheduleStatus
        {
            Last = last,
            Next = next,
            LastUpdated = lastUpdated ?? last,
        };
        return this;
    }

    /// <summary>Creates timer input for direct function invocation.</summary>
    /// <returns>A new timer value with the current past-due flag and optional schedule status.</returns>
    public TimerInfo Build() => new()
    {
        IsPastDue = _isPastDue,
        ScheduleStatus = _scheduleStatus,
    };

    /// <summary>Creates timer input together with capturable trigger metadata.</summary>
    /// <param name="bindingName">The non-empty worker input name. The default is <c>timer</c>.</param>
    /// <returns>
    /// New timer trigger data containing <c>IsPastDue</c> and <c>ScheduleStatus</c> binding metadata.
    /// </returns>
    public TestTriggerData<TimerInfo> BuildTrigger(string bindingName = "timer")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        var timer = Build();
        var bindingData = new Dictionary<string, object?>
        {
            ["IsPastDue"] = timer.IsPastDue,
            ["ScheduleStatus"] = timer.ScheduleStatus,
        };
        return new TestTriggerData<TimerInfo>(
            timer,
            bindingName,
            "timerTrigger",
            bindingData);
    }
}
