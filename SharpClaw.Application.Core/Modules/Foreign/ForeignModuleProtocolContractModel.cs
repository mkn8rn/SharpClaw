using System.Text.Json;

namespace SharpClaw.Application.Core.Modules.Foreign;

public sealed record ForeignModuleProtocolContractOperation(
    string Name,
    JsonElement ParametersSchema,
    JsonElement ResultSchema,
    string? Description = null);

public sealed record ForeignModuleProtocolContractExport(
    string ContractName,
    JsonElement Schema,
    IReadOnlyList<ForeignModuleProtocolContractOperation> Operations,
    string? Description = null);

public sealed record ForeignModuleProtocolContractRequirement(
    string ContractName,
    JsonElement? Schema = null,
    bool Optional = false,
    string? Description = null);

public interface IForeignModuleProtocolContractInvoker
{
    string ContractName { get; }
    IReadOnlyList<ForeignModuleProtocolContractOperation> Operations { get; }

    Task<JsonElement> InvokeAsync(
        string operation,
        JsonElement parameters,
        CancellationToken ct = default);
}

public interface IForeignModuleProtocolContractModule
{
    IReadOnlyList<ForeignModuleProtocolContractExport> ExportedProtocolContracts { get; }
    IReadOnlyList<ForeignModuleProtocolContractRequirement> RequiredProtocolContracts { get; }
}

public interface IForeignModuleProtocolContractExporter : IForeignModuleProtocolContractModule
{
    IForeignModuleProtocolContractInvoker GetProtocolContractInvoker(string contractName);
}

public interface IForeignModuleProtocolContractResolver
{
    IForeignModuleProtocolContractInvoker? Resolve(string contractName);
    IReadOnlyList<ForeignModuleProtocolContractExport> GetAllExports();
}
