using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SharpClaw.Runtime.Host.Routing;
using SharpClaw.Runtime.BLL.Services.Triggers;
using SharpClaw.Runtime.BLL.Services;
using SharpClaw.Contracts.DTOs.Tasks;
using SharpClaw.Contracts.Tasks;
using SharpClaw.Core.Tasks.Preflight;
using SharpClaw.Runtime.INF.DurableStorage;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Runtime.Host.Handlers;

// ═══════════════════════════════════════════════════════════════════
// Task definitions   /tasks
// ═══════════════════════════════════════════════════════════════════

[RouteGroup("/tasks")]
public static class TaskDefinitionHandlers
{
    [MapPost("/validate")]
    public static IResult Validate(
        ValidateTaskDefinitionRequest request,
        TaskService svc)
        => Results.Ok(svc.ValidateDefinition(request.SourceText));

    [MapPost]
    public static async Task<IResult> Create(
        CreateTaskDefinitionRequest request, TaskService svc)
        => Results.Ok(await svc.CreateDefinitionAsync(request));

    [MapGet]
    public static async Task<IResult> List(TaskService svc)
        => Results.Ok(await svc.ListDefinitionsAsync());

    [MapGet("/{taskId:guid}")]
    public static async Task<IResult> GetById(Guid taskId, TaskService svc)
    {
        var def = await svc.GetDefinitionAsync(taskId);
        return def is not null ? Results.Ok(def) : Results.NotFound();
    }

    [MapPut("/{taskId:guid}")]
    public static async Task<IResult> Update(
        Guid taskId, UpdateTaskDefinitionRequest request, TaskService svc)
    {
        var def = await svc.UpdateDefinitionAsync(taskId, request);
        return def is not null ? Results.Ok(def) : Results.NotFound();
    }

    [MapDelete("/{taskId:guid}")]
    public static async Task<IResult> Delete(Guid taskId, TaskService svc)
        => await svc.DeleteDefinitionAsync(taskId) ? Results.NoContent() : Results.NotFound();

    /// <summary>
    /// Run a preflight check against a task definition without creating an instance.
    /// Query-string parameters (<c>?param.Key=value</c>) are forwarded as parameter values.
    /// </summary>
    [MapGet("/{taskId:guid}/preflight")]
    public static async Task<IResult> Preflight(
        Guid taskId,
        HttpRequest httpRequest,
        TaskService svc,
        TaskPreflightChecker preflight,
        SessionService session,
        CancellationToken ct)
    {
        var definition = await svc.GetDefinitionAsync(taskId, ct);
        if (definition is null) return Results.NotFound();

        var requirements = await svc.GetRequirementsAsync(taskId, ct);
        if (requirements is null) return Results.NotFound();

        // Collect ?param.Key=value query parameters
        var paramValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in httpRequest.Query)
        {
            if (key.StartsWith("param.", StringComparison.OrdinalIgnoreCase))
                paramValues[key["param.".Length..]] = value.ToString();
        }

        var result = await preflight.CheckRuntimeAsync(
            requirements, paramValues, callerAgentId: null, ct);

        return Results.Ok(ToPreflightResponse(result));
    }

    private static TaskPreflightResponse ToPreflightResponse(TaskPreflightResult result)
        => TaskPreflightHelpers.ToResponse(result);
}

internal static class TaskPreflightHelpers
{
    internal static TaskPreflightResponse ToResponse(TaskPreflightResult result)
        => new(result.IsBlocked, result.Findings.Select(f => new TaskPreflightFindingResponse(
            f.RequirementKind, f.Severity.ToString(), f.Passed, f.Message, f.ParameterName)).ToList());
}

// ═══════════════════════════════════════════════════════════════════
// Task instances   /tasks/{taskId}/instances
// ═══════════════════════════════════════════════════════════════════

[RouteGroup("/tasks/{taskId:guid}/instances")]
public static class TaskInstanceHandlers
{
    [MapPost]
    public static async Task<IResult> CreateInstance(
        Guid taskId,
        StartTaskInstanceRequest request,
        TaskService svc,
        SessionService session,
        TaskOrchestrator orchestrator)
    {
        if (request.ChannelId is null && request.ContextId is null)
            return Results.BadRequest("Either a ChannelId or a ContextId is required to start a task instance.");

        TaskInstanceDetailResponse created;
        try
        {
            created = await svc.CreateInstanceAsync(
                request with { TaskDefinitionId = taskId },
                callerUserId: session.UserId);
        }
        catch (TaskPreflightBlockedException ex)
        {
            return Results.UnprocessableEntity(TaskPreflightHelpers.ToResponse(ex.PreflightResult));
        }

        if ((request with { TaskDefinitionId = taskId }).StartImmediately)
        {
            await orchestrator.StartAsync(created.Id);
            var started = await svc.GetInstanceAsync(created.Id);
            return started is not null ? Results.Ok(started) : Results.Ok(created);
        }

        return Results.Ok(created);
    }

    [MapGet]
    public static async Task<IResult> List(
        Guid taskId,
        TaskService svc,
        string? cursor = null,
        int take = 50,
        CancellationToken ct = default) =>
        Results.Ok(await svc.ListInstancesAsync(taskId, cursor, take, ct));

    [MapGet("/{instanceId:guid}")]
    public static async Task<IResult> GetById(
        Guid taskId, Guid instanceId, TaskService svc, ChatService chatSvc)
    {
        var inst = await GetScopedInstanceAsync(taskId, instanceId, svc);
        if (inst is null)
            return Results.NotFound();
        if (inst.ChannelId is { } chId)
        {
            var cost = await chatSvc.GetChannelCostAsync(chId);
            inst = inst with { ChannelCost = cost };
        }
        return Results.Ok(inst);
    }

    [MapPost("/{instanceId:guid}/cancel")]
    public static async Task<IResult> Cancel(
        Guid taskId, Guid instanceId, TaskService svc)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        return await svc.CancelInstanceAsync(instanceId)
            ? Results.NoContent()
            : Results.NotFound();
    }

    [MapPost("/{instanceId:guid}/stop")]
    public static async Task<IResult> Stop(
        Guid taskId,
        Guid instanceId,
        TaskOrchestrator orchestrator,
        TaskService svc)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        await orchestrator.StopAsync(instanceId);
        return Results.NoContent();
    }

    [MapPost("/{instanceId:guid}/pause")]
    public static async Task<IResult> Pause(
        Guid taskId,
        Guid instanceId,
        TaskOrchestrator orchestrator,
        TaskService svc)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        return await orchestrator.PauseAsync(instanceId)
            ? Results.NoContent()
            : Results.NotFound();
    }

    [MapPost("/{instanceId:guid}/resume")]
    public static async Task<IResult> Resume(
        Guid taskId,
        Guid instanceId,
        TaskOrchestrator orchestrator,
        TaskService svc)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        return await orchestrator.ResumeAsync(instanceId)
            ? Results.NoContent()
            : Results.NotFound();
    }

    [MapPost("/{instanceId:guid}/start")]
    public static async Task<IResult> StartExecution(
        Guid taskId,
        Guid instanceId,
        TaskOrchestrator orchestrator,
        TaskService svc)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        await orchestrator.StartAsync(instanceId);
        return Results.NoContent();
    }

    [MapGet("/{instanceId:guid}/outputs")]
    public static async Task<IResult> GetOutputs(
        Guid taskId,
        Guid instanceId,
        TaskService svc,
        string? cursor = null,
        int take = 100,
        int maxBytes = 262_144,
        CancellationToken ct = default)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        return Results.Ok(await svc.GetOutputsAsync(
            instanceId,
            cursor,
            take,
            maxBytes,
            ct));
    }

    [MapGet("/{instanceId:guid}/outputs/latest")]
    public static async Task<IResult> GetLatestOutput(
        Guid taskId,
        Guid instanceId,
        TaskService svc,
        CancellationToken ct)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        var output = await svc.GetLatestOutputAsync(instanceId, ct);
        return output is null ? Results.NoContent() : Results.Ok(output);
    }

    [MapGet("/{instanceId:guid}/logs")]
    public static async Task<IResult> GetLogs(
        Guid taskId,
        Guid instanceId,
        TaskService svc,
        string? cursor = null,
        int take = 200,
        int maxBytes = 262_144,
        string? minimumLevel = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? contains = null,
        long maxScanBytes = 16 * 1024 * 1024,
        CancellationToken ct = default)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        return Results.Ok(await svc.ReadLogsAsync(
            instanceId,
            cursor,
            new DurableLogQuery(
                take,
                maxBytes,
                minimumLevel,
                from,
                to,
                contains,
                maxScanBytes),
            ct));
    }

    [MapGet("/{instanceId:guid}/audit")]
    public static async Task<IResult> GetAudit(
        Guid taskId,
        Guid instanceId,
        TaskService svc,
        string? cursor = null,
        int take = 50,
        CancellationToken ct = default)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        return Results.Ok(await svc.ReadAuditAsync(instanceId, cursor, take, ct));
    }

    [MapGet("/{instanceId:guid}/artifacts/{artifactId:guid}")]
    public static async Task<IResult> GetArtifact(
        Guid taskId,
        Guid instanceId,
        Guid artifactId,
        TaskService svc,
        IExecutionArtifactStore artifacts,
        HttpContext context,
        CancellationToken ct)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
            return Results.NotFound();
        var handle = await artifacts.OpenReadAsync(
            artifactId,
            ExecutionOwnerKind.TaskInstance,
            instanceId,
            cancellationToken: ct);
        if (handle is null)
            return Results.NotFound();
        context.Response.RegisterForDisposeAsync(handle);
        context.Response.Headers.ETag = $"\"{handle.Descriptor.Sha256}\"";
        return Results.Stream(
            handle.Content,
            handle.Descriptor.MediaType,
            enableRangeProcessing: true);
    }

    [MapGet("/{instanceId:guid}/stream")]
    public static async Task StreamEvents(
        Guid taskId, Guid instanceId,
        TaskOrchestrator orchestrator,
        TaskService svc,
        HttpContext httpContext)
    {
        if (await GetScopedInstanceAsync(taskId, instanceId, svc) is null)
        {
            httpContext.Response.StatusCode = 404;
            return;
        }

        await TaskStreamHandlers.Stream(httpContext, taskId, instanceId, orchestrator);
    }

    private static async Task<TaskInstanceDetailResponse?> GetScopedInstanceAsync(
        Guid taskId,
        Guid instanceId,
        TaskService svc)
    {
        var instance = await svc.GetInstanceAsync(instanceId);
        return instance?.TaskDefinitionId == taskId ? instance : null;
    }
}

// ═══════════════════════════════════════════════════════════════════
// Trigger sources   /tasks/trigger-sources
// ═══════════════════════════════════════════════════════════════════

[RouteGroup("/tasks")]
public static class TaskTriggerHandlers
{
    /// <summary>
    /// Lists all registered trigger sources, including both built-in and
    /// custom module-provided sources.
    /// </summary>
    [MapGet("/trigger-sources")]
    public static IResult ListTriggerSources(
        ITaskTriggerSourceRegistry sourceRegistry) =>
        ListTriggerSources(sourceRegistry.Sources);

    public static IResult ListTriggerSources(
        IEnumerable<ITaskTriggerSource> sources)
    {
        var sourceList = sources.Select(s => new TaskTriggerSourceResponse(
            s.TriggerKey,
            s.TriggerKeys.ToList(),
            s.GetType().Name,
            true)).ToList();

        return Results.Ok(sourceList);
    }

    /// <summary>
    /// Enables all trigger bindings for a specific task definition.
    /// </summary>
    [MapPost("/{taskId:guid}/triggers/enable")]
    public static async Task<IResult> EnableTriggers(
        Guid taskId,
        TaskService svc,
        TaskTriggerHostService hostService,
        CancellationToken ct)
    {
        var definition = await svc.GetDefinitionAsync(taskId, ct);
        if (definition is null) return Results.NotFound();

        var count = await svc.SetTriggersEnabledAsync(taskId, enabled: true, ct);
        
        // Reload trigger sources to pick up the newly enabled bindings
        await hostService.NotifyBindingsChangedAsync();

        return Results.Ok(new { Enabled = count });
    }

    /// <summary>
    /// Disables all trigger bindings for a specific task definition.
    /// </summary>
    [MapPost("/{taskId:guid}/triggers/disable")]
    public static async Task<IResult> DisableTriggers(
        Guid taskId,
        TaskService svc,
        TaskTriggerHostService hostService,
        CancellationToken ct)
    {
        var definition = await svc.GetDefinitionAsync(taskId, ct);
        if (definition is null) return Results.NotFound();

        var count = await svc.SetTriggersEnabledAsync(taskId, enabled: false, ct);
        
        // Reload trigger sources to remove the disabled bindings
        await hostService.NotifyBindingsChangedAsync();

        return Results.Ok(new { Disabled = count });
    }
}
