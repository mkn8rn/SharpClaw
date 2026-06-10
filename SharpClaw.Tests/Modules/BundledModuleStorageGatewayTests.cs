using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharpClaw.Application.API;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Tests.Modules;

[TestFixture]
public sealed class BundledModuleStorageGatewayTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ListContracts_ExposesParentBackedModuleStorageOperations()
    {
        await using var db = CreateDbContext();
        var gateway = new BundledModuleStorageGateway(db);

        var contracts = gateway.ListContracts();

        contracts.Should().ContainSingle();
        contracts.Single().Operations.Select(operation => operation.Name)
            .Should()
            .BeEquivalentTo("get", "upsert", "delete", "list", "query");
    }

    [Test]
    public async Task UpsertAndGet_PersistsRecordAndTypedIndexRows()
    {
        await using var db = CreateDbContext();
        var gateway = new BundledModuleStorageGateway(db);
        var dueAt = DateTimeOffset.Parse("2026-06-10T12:00:00Z");

        await InvokeAsync(gateway, "upsert", new
        {
            key = "alpha",
            value = new
            {
                name = "Alpha",
                count = 2,
            },
            indexes = new
            {
                name = "Alpha",
                dueAt,
                priority = 2,
                active = true,
            },
        });

        var result = await InvokeAsync(gateway, "get", new { key = "alpha" });

        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("value").GetProperty("name").GetString().Should().Be("Alpha");
        db.ModuleStorageRecords.Should().ContainSingle();
        db.ModuleStorageIndexEntries.Should().HaveCount(4);
        db.ModuleStorageIndexEntries.Single(index => index.IndexName == "dueAt")
            .DateTimeValue
            .Should()
            .Be(dueAt);
    }

    [Test]
    public async Task Query_UsesTypedDateIndexAndReturnsRecordsInIndexOrder()
    {
        await using var db = CreateDbContext();
        var gateway = new BundledModuleStorageGateway(db);

        await UpsertJobAsync(gateway, "late", DateTimeOffset.Parse("2026-06-10T14:00:00Z"));
        await UpsertJobAsync(gateway, "early", DateTimeOffset.Parse("2026-06-10T10:00:00Z"));
        await UpsertJobAsync(gateway, "middle", DateTimeOffset.Parse("2026-06-10T12:00:00Z"));

        var result = await InvokeAsync(gateway, "query", new
        {
            indexName = "nextRunAt",
            lessThanOrEqual = DateTimeOffset.Parse("2026-06-10T12:00:00Z"),
            order = "asc",
        });

        result.GetProperty("records")
            .EnumerateArray()
            .Select(record => record.GetProperty("key").GetString())
            .Should()
            .Equal("early", "middle");
    }

    [Test]
    public async Task Delete_RemovesRecordAndIndexRows()
    {
        await using var db = CreateDbContext();
        var gateway = new BundledModuleStorageGateway(db);
        await UpsertJobAsync(gateway, "delete-me", DateTimeOffset.Parse("2026-06-10T10:00:00Z"));

        var result = await InvokeAsync(gateway, "delete", new { key = "delete-me" });

        result.GetProperty("deleted").GetBoolean().Should().BeTrue();
        db.ModuleStorageRecords.Should().BeEmpty();
        db.ModuleStorageIndexEntries.Should().BeEmpty();
    }

    private static async Task UpsertJobAsync(
        BundledModuleStorageGateway gateway,
        string key,
        DateTimeOffset nextRunAt)
    {
        await InvokeAsync(gateway, "upsert", new
        {
            key,
            value = new
            {
                key,
                nextRunAt,
            },
            indexes = new
            {
                nextRunAt,
            },
        });
    }

    private static Task<JsonElement> InvokeAsync(
        BundledModuleStorageGateway gateway,
        string operation,
        object parameters) =>
        gateway.InvokeAsync(
            "test_module",
            "records",
            operation,
            JsonSerializer.SerializeToElement(parameters, JsonOptions));

    private static SharpClawDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SharpClawDbContext>()
            .UseInMemoryDatabase($"ModuleStorage_{Guid.NewGuid():N}", new InMemoryDatabaseRoot())
            .Options;
        return new SharpClawDbContext(options);
    }
}
