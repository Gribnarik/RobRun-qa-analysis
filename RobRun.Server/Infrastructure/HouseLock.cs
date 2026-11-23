using System.Collections.Concurrent;
using RobRun.Server.Abstractions;

namespace RobRun.Server.Infrastructure;

public sealed class HouseLock : IHouseLock
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(int houseId, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(houseId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            sem.Release();
            return ValueTask.CompletedTask;
        }
    }
}
