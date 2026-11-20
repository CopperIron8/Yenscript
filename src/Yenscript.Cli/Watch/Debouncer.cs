using System.Threading.Channels;

namespace Yenscript.Cli.Watch;

public sealed class Debouncer
{
    private readonly Channel<long> signals = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly TimeSpan delay;

    public Debouncer(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero || delay.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(delay));
        this.delay = delay;
    }

    public void Signal() => signals.Writer.TryWrite(Environment.TickCount64);

    public async Task RunAsync(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        while (await signals.Reader.WaitToReadAsync(cancellationToken))
        {
            if (!signals.Reader.TryRead(out var lastSignal)) continue;
            while (true)
            {
                while (signals.Reader.TryRead(out var newer)) lastSignal = newer;
                var elapsed = Environment.TickCount64 - lastSignal;
                var remaining = delay - TimeSpan.FromMilliseconds(elapsed);
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining, cancellationToken);
            }
            // Do not drain after checking the deadline: a signal arriving now
            // must remain queued for another build rather than being discarded.
            cancellationToken.ThrowIfCancellationRequested();
            action();
        }
    }
}
