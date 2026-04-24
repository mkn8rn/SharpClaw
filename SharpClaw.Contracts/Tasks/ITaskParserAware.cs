namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Optional interface for modules that extend the task script parser.
/// Implement alongside <c>ISharpClawModule</c> and the host will call
/// <c>TaskScriptParser.RegisterModule</c> automatically during startup.
/// </summary>
public interface ITaskParserAware
{
    /// <summary>The parser extension this module contributes.</summary>
    ITaskParserModuleExtension ParserExtension { get; }
}
