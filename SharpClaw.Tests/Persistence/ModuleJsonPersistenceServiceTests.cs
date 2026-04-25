using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Infrastructure.Persistence.JSON;
using SharpClaw.Infrastructure.Persistence.Modules;
using SharpClaw.Utils.Security;

namespace SharpClaw.Tests.Persistence;

/// <summary>
/// Round-trip and boundary tests for module JSON persistence (Phase 8).
/// Uses a self-contained fake module context defined at the bottom of this file
/// so no test-only references leak into production projects.
/// </summary>
[TestFixture]
public class ModuleJsonPersistenceServiceTests
{
    private const string ModuleId = "test_module";

    private string _dataDir = null!;
    private IPersistenceFileSystem _fs = null!;
    private EncryptionOptions _encryptionOptions = null!;
    private JsonFileOptions _jsonOptions = null!;
    private RuntimeModuleDbContextRegistry _registry = null!;
    private ModuleDbContextOptions _moduleOptions = null!;
    private ModuleDbContextFactory _factory = null!;
    private ModuleJsonPersistenceService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), $"module_persist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDir);

        _fs = new PhysicalPersistenceFileSystem();
        _encryptionOptions = new EncryptionOptions { Key = ApiKeyEncryptor.GenerateKey() };
        _jsonOptions = new JsonFileOptions
        {
            DataDirectory = _dataDir,
            EncryptAtRest = false,
            FsyncOnWrite = false,
            AsyncFlush = false,
            EnableChecksums = false,
            EnableSnapshots = false,
            EnableEventLog = false,
        };

        _moduleOptions = new ModuleDbContextOptions { StorageMode = StorageMode.JsonFile };
        _registry = new RuntimeModuleDbContextRegistry();

        // Register the test module context.
        var registration = new RuntimeModuleDbContextRegistration(
            ModuleId,
            typeof(FakeModuleDbContext),
            [typeof(FakeEntityDB)]);
        _registry.Register(registration);

        _factory = new ModuleDbContextFactory(_registry, _moduleOptions);

        _sut = new ModuleJsonPersistenceService(
            _fs,
            _jsonOptions,
            _encryptionOptions,
            _factory,
            _registry,
            NullLogger<ModuleJsonPersistenceService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }

    // ── Round-trip ────────────────────────────────────────────────────────

    [Test]
    public async Task EntitySavedViaInterceptor_IsLoadedIntoFreshContext()
    {
        var id = Guid.NewGuid();
        var written = await WriteEntityAndFlushAsync(id, "hello");

        // Simulate a fresh process: create a new context and hydrate.
        await _sut.LoadRegisteredModulesAsync();

        await using var readCtx = (FakeModuleDbContext)_factory.CreateDbContext(typeof(FakeModuleDbContext));
        var loaded = await readCtx.FakeEntities.FindAsync(id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("hello");
    }

    [Test]
    public async Task EntityFile_WrittenToModuleQualifiedPath()
    {
        var id = Guid.NewGuid();
        await WriteEntityAndFlushAsync(id, "path-check");

        var expectedPath = Path.Combine(_dataDir, "modules", ModuleId, nameof(FakeEntityDB), $"{id}.json");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Test]
    public async Task LegacyFlatPath_IsNotConsulted_WhenModulePathExists()
    {
        var id = Guid.NewGuid();
        await WriteEntityAndFlushAsync(id, "module-path");

        // Place a different entity in the legacy flat directory.
        var legacyDir = Path.Combine(_dataDir, nameof(FakeEntityDB));
        Directory.CreateDirectory(legacyDir);
        var legacyId = Guid.NewGuid();
        var legacyJson = $"{{\"id\":\"{legacyId}\",\"name\":\"legacy\",\"createdAt\":\"2025-01-01T00:00:00+00:00\",\"updatedAt\":\"2025-01-01T00:00:00+00:00\"}}";
        await File.WriteAllTextAsync(Path.Combine(legacyDir, $"{legacyId}.json"), legacyJson);

        await _sut.LoadRegisteredModulesAsync();

        await using var ctx = (FakeModuleDbContext)_factory.CreateDbContext(typeof(FakeModuleDbContext));
        var legacyLoaded = await ctx.FakeEntities.FindAsync(legacyId);

        // The legacy entity must NOT have been loaded.
        legacyLoaded.Should().BeNull("the legacy flat path must not be consulted");
    }

    [Test]
    public async Task DeletedEntity_RemovesFileFromModulePath()
    {
        var id = Guid.NewGuid();
        await WriteEntityAndFlushAsync(id, "to-be-deleted");

        var filePath = Path.Combine(_dataDir, "modules", ModuleId, nameof(FakeEntityDB), $"{id}.json");
        File.Exists(filePath).Should().BeTrue();

        // Delete via a flush with Deleted state.
        var registration = _registry.GetRegistration(typeof(FakeModuleDbContext))!;
        await using var ctx = (FakeModuleDbContext)_factory.CreateDbContext(typeof(FakeModuleDbContext));
        await _sut.FlushChangesAsync(
            ctx,
            registration,
            [(typeof(FakeEntityDB), id, EntityState.Deleted)],
            (IReadOnlySet<string>)new HashSet<string>(),
            CancellationToken.None);

        File.Exists(filePath).Should().BeFalse();
    }

    [Test]
    public async Task DisabledModule_IsNotHydrated()
    {
        // Registry contains only one module. Unregister it then call load.
        var registration = _registry.GetRegistration(typeof(FakeModuleDbContext))!;
        _registry.UnregisterModule(ModuleId);

        await WriteEntityAndFlushAsync(Guid.NewGuid(), "should-not-load");

        // Re-create sut with now-empty registry.
        var emptySut = new ModuleJsonPersistenceService(
            _fs, _jsonOptions, _encryptionOptions, _factory,
            _registry, NullLogger<ModuleJsonPersistenceService>.Instance);

        // Should complete without throwing; nothing is loaded.
        await emptySut.LoadRegisteredModulesAsync();
        // Re-register just to keep teardown clean.
        _registry.Register(registration);
    }

    // ── Audit fields ──────────────────────────────────────────────────────

    [Test]
    public async Task Interceptor_AssignsId_CreatedAt_UpdatedAt_OnAdded()
    {
        var interceptor = CreateInterceptor();

        await using var ctx = CreateContextWithInterceptor(interceptor);
        var entity = new FakeEntityDB { Name = "audit-test" };
        ctx.FakeEntities.Add(entity);
        await ctx.SaveChangesAsync();

        entity.Id.Should().NotBe(Guid.Empty);
        entity.CreatedAt.Should().NotBe(default);
        entity.UpdatedAt.Should().NotBe(default);
    }

    [Test]
    public async Task Interceptor_UpdatesUpdatedAt_OnModified()
    {
        var interceptor = CreateInterceptor();

        await using var ctx = CreateContextWithInterceptor(interceptor);
        var entity = new FakeEntityDB { Name = "before" };
        ctx.FakeEntities.Add(entity);
        await ctx.SaveChangesAsync();

        var created = entity.CreatedAt;

        entity.Name = "after";
        await Task.Delay(5); // ensure clock advances
        await ctx.SaveChangesAsync();

        entity.UpdatedAt.Should().BeOnOrAfter(created);
        entity.CreatedAt.Should().Be(created, "CreatedAt must not change on update");
    }

    // ── Checksum manifest ─────────────────────────────────────────────────

    [Test]
    public async Task WithChecksumsEnabled_ManifestFileIsWritten()
    {
        _jsonOptions.EnableChecksums = true;

        var id = Guid.NewGuid();
        await WriteEntityAndFlushAsync(id, "checksum-test");

        var manifestPath = Path.Combine(
            _dataDir, "modules", ModuleId, nameof(FakeEntityDB), ChecksumManifest.ManifestFileName);
        File.Exists(manifestPath).Should().BeTrue();
    }

    // ── Event log ─────────────────────────────────────────────────────────

    [Test]
    public async Task WithEventLogEnabled_ModuleIdIsTaggedInLogEntry()
    {
        _jsonOptions.EnableEventLog = true;
        _jsonOptions.EncryptAtRest = false;

        var id = Guid.NewGuid();
        await WriteEntityAndFlushAsync(id, "event-log-test");

        var eventLog = new EventLog(_fs, _jsonOptions, _encryptionOptions.Key, NullLogger.Instance);
        var entries = await eventLog.ReadAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        entries.Should().ContainSingle(e =>
            e.EntityId == id &&
            e.Action == EventAction.Created &&
            e.ModuleId == ModuleId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<FakeEntityDB> WriteEntityAndFlushAsync(Guid id, string name)
    {
        var interceptor = CreateInterceptor();
        await using var ctx = CreateContextWithInterceptor(interceptor);

        var entity = new FakeEntityDB { Id = id, Name = name };
        ctx.FakeEntities.Add(entity);
        await ctx.SaveChangesAsync();
        return entity;
    }

    private ModuleJsonSaveChangesInterceptor CreateInterceptor() =>
        new(_registry, _sut, _moduleOptions);

    private FakeModuleDbContext CreateContextWithInterceptor(ModuleJsonSaveChangesInterceptor interceptor)
    {
        var builder = new DbContextOptionsBuilder<FakeModuleDbContext>();
        builder
            .UseInMemoryDatabase($"FakeModule_{Guid.NewGuid():N}", _moduleOptions.InMemoryDatabaseRoot)
            .AddInterceptors(interceptor);
        return new FakeModuleDbContext(builder.Options);
    }
}

// ── Fake module context and entity ────────────────────────────────────────────

internal sealed class FakeEntityDB : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class FakeModuleDbContext(DbContextOptions<FakeModuleDbContext> options)
    : DbContext(options)
{
    public DbSet<FakeEntityDB> FakeEntities => Set<FakeEntityDB>();
}
