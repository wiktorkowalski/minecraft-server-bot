using System.Collections.Concurrent;

namespace MinecraftServerBot.Services;

public sealed class RateLimitService
{
    private readonly ConcurrentDictionary<ulong, Queue<DateTime>> _buckets = new();

    public bool TryAcquire(ulong userId, int maxPerWindow, TimeSpan window)
    {
        if (maxPerWindow <= 0)
        {
            return true;
        }

        var queue = _buckets.GetOrAdd(userId, _ => new Queue<DateTime>());
        lock (queue)
        {
            var now = DateTime.UtcNow;
            var cutoff = now - window;
            while (queue.TryPeek(out var head) && head < cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count >= maxPerWindow)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
