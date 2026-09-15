using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Fluently builds timer-trigger input without a Functions runtime.</summary>
public sealed class TestTimerInfoBuilder
{
    private bool _isPastDue;
    private ScheduleStatus? _scheduleStatus;

    /// <summary>Marks the timer invocation as past due.</summary>
    public TestTimerInfoBuilder PastDue(bool isPastDue = true)
    {
        _isPastDue = isPastDue;
        return this;
    }

    /// <summary>Sets the timer schedule timestamps.</summary>
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
    public TimerInfo Build() => new()
    {
        IsPastDue = _isPastDue,
        ScheduleStatus = _scheduleStatus,
    };

    /// <summary>Creates timer input together with capturable trigger metadata.</summary>
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
