namespace z1.IO;

internal class DeferredEvent
{
    public static readonly DeferredEvent CompletedEvent = new DeferredEventSource(true).Event;

    public bool IsCompleted => _source.IsCompleted;

    private readonly DeferredEventSource _source;

    internal DeferredEvent(DeferredEventSource source)
    {
        _source = source;
    }
}

internal class DeferredEventSource
{
    public DeferredEvent Event { get; }

    internal bool IsCompleted { get; private set; }

    public DeferredEventSource()
    {
        Event = new DeferredEvent(this);
    }

    public DeferredEventSource(bool state)
    {
        Event = new DeferredEvent(this);
        IsCompleted = state;
    }

    public void SetCompleted() => IsCompleted = true;
}