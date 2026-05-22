using System.Text.Json;

namespace SharpClaw.Application.Core.Modules.Foreign;

internal sealed record ForeignModuleConfigGetRequest
{
    public string Key { get; init; } = string.Empty;
}

internal sealed record ForeignModuleConfigSetRequest
{
    public string Key { get; init; } = string.Empty;
    public string? Value { get; init; }
}

internal sealed record ForeignModuleConfigGetResponse(string? Value);

internal sealed record ForeignModuleConfigAllResponse(IReadOnlyDictionary<string, string> Values);

internal sealed record ForeignModuleLogRequest
{
    public string Message { get; init; } = string.Empty;
    public string Level { get; init; } = "Info";
}

internal sealed record ForeignModuleJobLogRequest
{
    public Guid JobId { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Level { get; init; } = "Info";
}

internal sealed record ForeignModuleJobCompleteRequest
{
    public Guid JobId { get; init; }
    public string? ResultData { get; init; }
    public string? Message { get; init; }
}

internal sealed record ForeignModuleJobFailRequest
{
    public Guid JobId { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
}

internal sealed record ForeignModuleJobCancelRequest
{
    public Guid JobId { get; init; }
    public string? Message { get; init; }
}

internal sealed record ForeignModuleCapabilityAck(bool Accepted = true, string? Message = null);

internal sealed record ForeignModuleProtocolContractsListResponse(
    IReadOnlyList<ForeignModuleProtocolContractExport> Contracts);

internal sealed record ForeignModuleProtocolContractInvokeRequest
{
    public string ContractName { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public JsonElement Parameters { get; init; }
}

internal sealed record ForeignModuleProtocolContractInvokeResponse(JsonElement Result);
