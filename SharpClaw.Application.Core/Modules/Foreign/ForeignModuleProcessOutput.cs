namespace SharpClaw.Application.Core.Modules.Foreign;

internal sealed record ForeignModuleProcessOutput(
    string StandardOutput,
    string StandardError)
{
    public string Combined =>
        string.IsNullOrEmpty(StandardError)
            ? StandardOutput
            : StandardOutput + Environment.NewLine + StandardError;
}
