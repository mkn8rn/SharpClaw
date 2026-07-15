using SharpClaw.Shared.DurableStorage;
using SharpClaw.Shared.Logging;

namespace SharpClaw.Tests.Logging;

[TestFixture]
public sealed class DurableProcessLogWriterTests
{
    [Test]
    public async Task NewBootPreservesEarlierBootAndUsesIndependentStream()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpClawDurableProcessLogWriterTests_" + Guid.NewGuid().ToString("N"));
        var options = new DurableStorageOptions
        {
            RootDirectory = root,
            EncryptionKey = Enumerable.Repeat((byte)0x41, 32).ToArray(),
            SegmentMaxBytes = 64 * 1024,
            SegmentMaxAge = TimeSpan.FromHours(1),
        };

        try
        {
            await using var store = new DurableSegmentStore(options);
            var firstBoot = Guid.NewGuid();
            var secondBoot = Guid.NewGuid();

            await using (var writer = new DurableProcessLogWriter(
                             "core",
                             store,
                             firstBoot,
                             TimeSpan.FromHours(1)))
            {
                writer.AppendLog("first boot");
                await writer.FlushAsync();
            }

            await using (var writer = new DurableProcessLogWriter(
                             "core",
                             store,
                             secondBoot,
                             TimeSpan.FromHours(1)))
            {
                writer.AppendLog("second boot");
                await writer.FlushAsync();
            }

            var first = await store.ReadAsync(
                DurableStreamKey.Process("core", firstBoot),
                nextSequence: 1,
                options: new DurableReadOptions());
            var second = await store.ReadAsync(
                DurableStreamKey.Process("core", secondBoot),
                nextSequence: 1,
                options: new DurableReadOptions());

            first.Records.Select(record => record.Message)
                .Should().Equal("first boot");
            second.Records.Select(record => record.Message)
                .Should().Equal("second boot");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
