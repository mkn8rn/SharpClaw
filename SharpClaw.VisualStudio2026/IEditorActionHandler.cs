using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpClaw.VS2026
{
    /// <summary>
    /// Abstraction for editor-specific action handling. The VS 2026
    /// implementation calls EnvDTE / VS SDK APIs for each action.
    /// </summary>
    public interface IEditorActionHandler
    {
        Task<string> HandleAsync(
            string action,
            Dictionary<string, object> parameters,
            CancellationToken ct);
    }
}
