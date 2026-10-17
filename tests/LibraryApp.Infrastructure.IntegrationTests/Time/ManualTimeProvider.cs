namespace LibraryApp.Infrastructure.IntegrationTests.Time;

/// <summary>
/// A clock the test moves by hand; the real SystemClock on top of it gives Asia/Yerevan dates. Timers (and so
/// <c>Task.Delay(…, timeProvider)</c>) fire when the test moves the clock past their due time, never by themselves.
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _utcNow;
    }

    /// <summary>Timers waiting to fire: a background loop that waits for the clock has one.</summary>
    public int WaitingTimers
    {
        get
        {
            lock (_gate)
                return _timers.Count(t => t.DueAt is not null);
        }
    }

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_gate)
            _utcNow = utcNow;
        FireDueTimers();
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
            _utcNow += by;
        FireDueTimers();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>The UTC moment of an Asia/Yerevan wall time (UTC+4, no daylight saving).</summary>
    public static DateTimeOffset Yerevan(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(4));

    private void FireDueTimers()
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            due = _timers.Where(t => t.DueAt <= _utcNow).ToList();
            foreach (ManualTimer timer in due)
                timer.DueAt = timer.Period > TimeSpan.Zero ? _utcNow + timer.Period : null;
        }

        foreach (ManualTimer timer in due)
            timer.Fire();
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (!owner._timers.Contains(this))
                    owner._timers.Add(this);
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._utcNow + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
            }

            if (dueTime == TimeSpan.Zero)
                owner.FireDueTimers();
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
                owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
