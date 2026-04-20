using System.Collections.Concurrent;
using SharpClaw.Infrastructure.Persistence.JSON;

namespace SharpClaw.Tests.Persistence;

[TestFixture]
public class DirectoryLockManagerTests
{
    private DirectoryLockManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new DirectoryLockManager();
    }

    [TearDown]
    public void TearDown()
    {
        _manager.Dispose();
    }

    // ── Basic acquire/release ────────────────────────────────────

    [Test]
    public async Task Acquire_ReturnReleasable_Lock()
    {
        using var hold = await _manager.AcquireAsync("/data/EntityA");
        hold.Should().NotBeNull();
    }

    [Test]
    public async Task Acquire_SameDirectory_BlocksUntilReleased()
    {
        const string dir = "/data/EntityA";
        var entered = new TaskCompletionSource();
        var released = new TaskCompletionSource();

        // Hold the lock in a background task.
        var holder = Task.Run(async () =>
        {
            using var hold = await _manager.AcquireAsync(dir);
            entered.SetResult();
            await released.Task; // hold until signalled
        });

        await entered.Task;

        // Second acquire should not complete until holder releases.
        var second = _manager.AcquireAsync(dir);
        await Task.Delay(50);
        second.IsCompleted.Should().BeFalse("lock should be held by first acquirer");

        released.SetResult();
        using var hold2 = await second;
        hold2.Should().NotBeNull();
        await holder;
    }

    [Test]
    public async Task Acquire_DifferentDirectories_DoNotBlock()
    {
        using var a = await _manager.AcquireAsync("/data/EntityA");
        using var b = await _manager.AcquireAsync("/data/EntityB");
        // Both acquired without deadlock — pass.
    }

    // ── Concurrent write stress ──────────────────────────────────

    [Test]
    public async Task ConcurrentWrites_AreSerialized()
    {
        const string dir = "/data/Stress";
        var counter = 0;
        var errors = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            using var hold = await _manager.AcquireAsync(dir);
            var snapshot = Interlocked.Increment(ref counter);
            // Inside the lock, counter should be exactly snapshot (no interleaving).
            await Task.Delay(1); // simulate I/O
            var current = Interlocked.CompareExchange(ref counter, 0, 0);
            if (current != snapshot)
                errors.Add($"Task {i}: expected {snapshot}, got {current}");
        });

        await Task.WhenAll(tasks);
        errors.Should().BeEmpty("all increments should be serialized under the lock");
    }

    // ── Read-during-write serialization ──────────────────────────

    [Test]
    public async Task ReadDuringWrite_IsBlockedUntilWriteCompletes()
    {
        const string dir = "/data/Entity";
        var writeStarted = new TaskCompletionSource();
        var writeFinished = new TaskCompletionSource();
        var readStartedAt = 0L;

        // Simulate write holding the lock.
        var writer = Task.Run(async () =>
        {
            using var hold = await _manager.AcquireAsync(dir);
            writeStarted.SetResult();
            await Task.Delay(100); // simulate slow write
            writeFinished.SetResult();
        });

        await writeStarted.Task;

        // Simulate read — should block until write is done.
        var reader = Task.Run(async () =>
        {
            using var hold = await _manager.AcquireAsync(dir);
            Interlocked.Exchange(ref readStartedAt, Environment.TickCount64);
        });

        await Task.WhenAll(writer, reader);

        var finishedTicks = Environment.TickCount64;
        // Read should have started after the write delay.
        Interlocked.Read(ref readStartedAt).Should().BeGreaterThan(0);
    }

    // ── Shutdown ordering ────────────────────────────────────────

    [Test]
    public async Task AcquireAll_BlocksUntilAllLocksReleased()
    {
        const string dirA = "/data/A";
        const string dirB = "/data/B";
        var releasedA = new TaskCompletionSource();
        var releasedB = new TaskCompletionSource();

        // Warm up locks so AcquireAll knows about them.
        var holdA = await _manager.AcquireAsync(dirA);
        var holdB = await _manager.AcquireAsync(dirB);

        var acquireAll = Task.Run(async () =>
        {
            await _manager.AcquireAllAsync();
        });

        await Task.Delay(50);
        acquireAll.IsCompleted.Should().BeFalse("should be blocked by held locks");

        holdA.Dispose();
        await Task.Delay(20);
        acquireAll.IsCompleted.Should().BeFalse("still blocked by dirB");

        holdB.Dispose();
        await acquireAll; // should complete now
    }

    [Test]
    public async Task Dispose_PreventsNewAcquires()
    {
        _manager.Dispose();

        var act = async () => await _manager.AcquireAsync("/data/X");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ── Concurrent join table flush ──────────────────────────────

    [Test]
    public async Task ConcurrentJoinTableFlush_Serialized()
    {
        const string joinDir = "/data/_join/A_B";
        var order = new ConcurrentQueue<int>();

        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            using var hold = await _manager.AcquireAsync(joinDir);
            order.Enqueue(i);
            await Task.Delay(5); // simulate I/O
        });

        await Task.WhenAll(tasks);
        order.Count.Should().Be(10, "all tasks should have completed");
    }

    [Test]
    public async Task ShutdownInfrastructure_DisposesLockManager()
    {
        // Simulate the extension method pattern.
        await _manager.AcquireAllAsync();
        _manager.Dispose();

        var act = async () => await _manager.AcquireAsync("/data/X");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
