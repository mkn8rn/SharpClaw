using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Infrastructure.Persistence.JSON;

namespace SharpClaw.Tests.Persistence;

[TestFixture]
public class TransactionQueueTests
{
    private InMemoryPersistenceFileSystem _fs = null!;
    private JsonFileOptions _options = null!;
    private TransactionQueue _queue = null!;

    [SetUp]
    public void SetUp()
    {
        _fs = new InMemoryPersistenceFileSystem();
        _options = new JsonFileOptions { DataDirectory = "/data" };
        _queue = new TransactionQueue(
            _fs, _options,
            NullLogger<TransactionQueue>.Instance,
            maxRetries: 3);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static IReadOnlyList<(Type ClrType, Guid Id, EntityState State)> Changes(
        params (string TypeName, Guid Id, EntityState State)[] entries)
    {
        // Use a simple stub type — the manifest stores the name, not the CLR type.
        return entries.Select(e => (typeof(FakeEntity), e.Id, e.State)).ToList();
    }

    private sealed class FakeEntity { public Guid Id { get; set; } }

    // ── Enqueue / Dequeue ────────────────────────────────────────

    [Test]
    public async Task Enqueue_CreatesManifestFile()
    {
        var id = Guid.NewGuid();
        var changes = Changes(("FakeEntity", id, EntityState.Added));
        var joinChanges = new HashSet<string>();

        var path = await _queue.EnqueueAsync(changes, joinChanges);

        _fs.FileExists(path).Should().BeTrue();
        var json = await _fs.ReadAllTextAsync(path);
        json.Should().Contain("FakeEntity");
        json.Should().Contain(id.ToString());
    }

    [Test]
    public async Task Dequeue_RemovesManifestFile()
    {
        var changes = Changes(("FakeEntity", Guid.NewGuid(), EntityState.Modified));
        var path = await _queue.EnqueueAsync(changes, new HashSet<string>());

        _queue.Dequeue(path);

        _fs.FileExists(path).Should().BeFalse();
    }

    // ── Monotonic Sequence ───────────────────────────────────────

    [Test]
    public async Task Enqueue_ProducesMonotonicallyIncreasingSequence()
    {
        var changes = Changes(("FakeEntity", Guid.NewGuid(), EntityState.Added));
        var join = new HashSet<string>();

        var path1 = await _queue.EnqueueAsync(changes, join);
        var path2 = await _queue.EnqueueAsync(changes, join);
        var path3 = await _queue.EnqueueAsync(changes, join);

        // Filenames sort in sequence order
        var names = new[] { _fs.GetFileName(path1), _fs.GetFileName(path2), _fs.GetFileName(path3) };
        names.Should().BeInAscendingOrder();
    }

    [Test]
    public async Task Sequence_SurvivesReconstruction()
    {
        var changes = Changes(("FakeEntity", Guid.NewGuid(), EntityState.Added));
        await _queue.EnqueueAsync(changes, new HashSet<string>());
        await _queue.EnqueueAsync(changes, new HashSet<string>());

        // Reconstruct — should load persisted sequence
        var queue2 = new TransactionQueue(
            _fs, _options,
            NullLogger<TransactionQueue>.Instance);

        var path = await queue2.EnqueueAsync(changes, new HashSet<string>());
        var fileName = _fs.GetFileName(path);
        // Sequence should be 3 (continuing from 2)
        fileName.Should().StartWith("000000000003_");
    }

    // ── Replay ───────────────────────────────────────────────────

    [Test]
    public async Task ReplayPending_InvokesCallbackAndCleansUp()
    {
        var id = Guid.NewGuid();
        var changes = Changes(("FakeEntity", id, EntityState.Added));
        await _queue.EnqueueAsync(changes, new HashSet<string>());

        var replayed = new List<TransactionManifest>();

        var count = await _queue.ReplayPendingAsync((manifest, ct) =>
        {
            replayed.Add(manifest);
            return Task.CompletedTask;
        });

        count.Should().Be(1);
        replayed.Should().HaveCount(1);
        replayed[0].EntityChanges.Should().ContainSingle(e => e.Id == id);

        // Pending dir should be empty
        _fs.GetFiles("/data/_transactions/pending", "*.json").Should().BeEmpty();
    }

    [Test]
    public async Task ReplayPending_ReturnsZero_WhenNoPending()
    {
        var count = await _queue.ReplayPendingAsync((_, _) => Task.CompletedTask);
        count.Should().Be(0);
    }

    [Test]
    public async Task ReplayPending_ReplaysInSequenceOrder()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await _queue.EnqueueAsync(
            Changes(("FakeEntity", id1, EntityState.Added)), new HashSet<string>());
        await _queue.EnqueueAsync(
            Changes(("FakeEntity", id2, EntityState.Added)), new HashSet<string>());

        var order = new List<Guid>();
        await _queue.ReplayPendingAsync((m, _) =>
        {
            order.Add(m.EntityChanges[0].Id);
            return Task.CompletedTask;
        });

        order.Should().ContainInOrder(id1, id2);
    }

    // ── Failure Handling ─────────────────────────────────────────

    [Test]
    public async Task ReplayPending_MovesToFailed_AfterMaxRetries()
    {
        await _queue.EnqueueAsync(
            Changes(("FakeEntity", Guid.NewGuid(), EntityState.Added)),
            new HashSet<string>());

        // Fail 3 times (max retries = 3)
        for (var i = 0; i < 3; i++)
        {
            await _queue.ReplayPendingAsync((_, _) =>
                throw new InvalidOperationException("simulated failure"));
        }

        // 4th attempt should move to failed
        var count = await _queue.ReplayPendingAsync((_, _) => Task.CompletedTask);
        count.Should().Be(0);

        // Should be in failed dir
        _fs.GetFiles("/data/_transactions/failed", "*.json").Should().HaveCount(1);
        _fs.GetFiles("/data/_transactions/pending", "*.json").Should().BeEmpty();
    }

    [Test]
    public async Task ReplayPending_IncrementsRetryCount()
    {
        await _queue.EnqueueAsync(
            Changes(("FakeEntity", Guid.NewGuid(), EntityState.Added)),
            new HashSet<string>());

        // Fail once
        await _queue.ReplayPendingAsync((_, _) =>
            throw new InvalidOperationException("fail"));

        // Manifest should still be pending with retryCount = 1
        var files = _fs.GetFiles("/data/_transactions/pending", "*.json");
        files.Should().HaveCount(1);
        var json = await _fs.ReadAllTextAsync(files[0]);
        json.Should().Contain("\"retryCount\": 1");
    }

    // ── Join Table Changes ───────────────────────────────────────

    [Test]
    public async Task Enqueue_IncludesJoinTableChanges()
    {
        var changes = Changes(("FakeEntity", Guid.NewGuid(), EntityState.Added));
        var joins = new HashSet<string> { "AgentDBRoleDB", "ChannelDBModelDB" };

        var path = await _queue.EnqueueAsync(changes, joins);

        var json = await _fs.ReadAllTextAsync(path);
        json.Should().Contain("AgentDBRoleDB");
        json.Should().Contain("ChannelDBModelDB");
    }
}
