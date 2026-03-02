using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace SharpClaw.VS2026
{
    /// <summary>
    /// VS 2026 implementation of <see cref="IEditorActionHandler"/>.
    /// Uses EnvDTE2 for IDE-integrated operations, with System.IO
    /// fallbacks for file operations.
    /// </summary>
    public class VisualStudioActionHandler : IEditorActionHandler
    {
        /// <summary>
        /// Set by the package on initialization from <c>DTE.Solution.FullName</c>.
        /// </summary>
        public string SolutionDirectory { get; set; }

        /// <summary>
        /// Set by the package to enable IDE-integrated actions.
        /// </summary>
        public DTE2 DTE { get; set; }

        /// <summary>
        /// Set by the package so handler methods can switch to the UI thread.
        /// </summary>
        public JoinableTaskFactory JoinableTaskFactory { get; set; }

        public Task<string> HandleAsync(
            string action,
            Dictionary<string, object> parameters,
            CancellationToken ct)
        {
            switch (action)
            {
                case "read_file": return ReadFileAsync(parameters, ct);
                case "get_open_files": return GetOpenFilesAsync(ct);
                case "get_selection": return GetSelectionAsync(ct);
                case "get_diagnostics": return GetDiagnosticsAsync(parameters, ct);
                case "apply_edit": return ApplyEditAsync(parameters, ct);
                case "create_file": return CreateFileAsync(parameters, ct);
                case "delete_file": return DeleteFileAsync(parameters, ct);
                case "show_diff": return ShowDiffAsync(parameters, ct);
                case "run_build": return RunBuildAsync(ct);
                case "run_terminal": return RunTerminalAsync(parameters, ct);
                default:
                    throw new NotSupportedException($"Unknown action: {action}");
            }
        }

        // ── File operations (System.IO based) ─────────────────────

        private Task<string> ReadFileAsync(
            Dictionary<string, object> p, CancellationToken ct)
        {
            var filePath = ResolvePath(GetString(p, "filePath")
                ?? throw new ArgumentException("filePath is required."));

            var content = File.ReadAllText(filePath);

            var startLine = GetInt(p, "startLine");
            var endLine = GetInt(p, "endLine");

            if (startLine.HasValue || endLine.HasValue)
            {
                var lines = content.Split('\n');
                var start = (startLine ?? 1) - 1;
                var end = Math.Min(endLine ?? lines.Length, lines.Length);
                content = string.Join("\n", lines.Skip(start).Take(end - start));
            }

            return Task.FromResult(content);
        }

        private async Task<string> GetOpenFilesAsync(CancellationToken ct)
        {
            if (DTE == null)
                return "[]";

            await JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var files = new List<string>();
            foreach (Document doc in DTE.Documents)
            {
                files.Add(doc.FullName);
            }
            return JsonSerializer.Serialize(files);
        }

        private async Task<string> GetSelectionAsync(CancellationToken ct)
        {
            if (DTE == null || DTE.ActiveDocument == null)
                return JsonSerializer.Serialize(
                    new Dictionary<string, object> { ["activeFile"] = null });

            await JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var doc = DTE.ActiveDocument;
            var selection = doc.Selection as TextSelection;

            var result = new Dictionary<string, object>
            {
                ["activeFile"] = doc.FullName,
                ["language"] = doc.Language ?? "",
                ["selectionStartLine"] = selection?.TopLine ?? 0,
                ["selectionEndLine"] = selection?.BottomLine ?? 0,
                ["selectedText"] = selection?.Text ?? ""
            };

            return JsonSerializer.Serialize(result);
        }

        private async Task<string> GetDiagnosticsAsync(
            Dictionary<string, object> p, CancellationToken ct)
        {
            if (DTE == null)
                return "[]";

            await JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var errorList = new List<Dictionary<string, object>>();
            var errorItems = DTE.ToolWindows.ErrorList.ErrorItems;

            for (int i = 1; i <= errorItems.Count; i++)
            {
                var item = errorItems.Item(i);
                var filePath = GetString(p, "filePath");

                // If filePath is specified, filter to just that file
                if (filePath != null && !item.FileName.EndsWith(filePath,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                errorList.Add(new Dictionary<string, object>
                {
                    ["file"] = item.FileName ?? "",
                    ["line"] = item.Line,
                    ["severity"] = item.ErrorLevel.ToString(),
                    ["message"] = item.Description ?? ""
                });
            }

            return JsonSerializer.Serialize(errorList);
        }

        private Task<string> ApplyEditAsync(
            Dictionary<string, object> p, CancellationToken ct)
        {
            var filePath = ResolvePath(GetString(p, "filePath")
                ?? throw new ArgumentException("filePath is required."));
            var startLine = GetInt(p, "startLine") ?? 1;
            var endLine = GetInt(p, "endLine") ?? startLine;
            var newText = GetString(p, "newText") ?? "";

            var lines = File.ReadAllLines(filePath).ToList();
            var start = startLine - 1;
            var end = Math.Min(endLine, lines.Count);
            lines.RemoveRange(start, end - start);
            lines.InsertRange(start, newText.Split('\n'));
            File.WriteAllLines(filePath, lines);

            return Task.FromResult("Edit applied successfully.");
        }

        private Task<string> CreateFileAsync(
            Dictionary<string, object> p, CancellationToken ct)
        {
            var filePath = ResolvePath(GetString(p, "filePath")
                ?? throw new ArgumentException("filePath is required."));
            var content = GetString(p, "content") ?? "";

            var dir = Path.GetDirectoryName(filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, content);

            return Task.FromResult($"File created: {filePath}");
        }

        private Task<string> DeleteFileAsync(
            Dictionary<string, object> p, CancellationToken ct)
        {
            var filePath = ResolvePath(GetString(p, "filePath")
                ?? throw new ArgumentException("filePath is required."));
            File.Delete(filePath);
            return Task.FromResult($"File deleted: {filePath}");
        }

        private Task<string> ShowDiffAsync(
            Dictionary<string, object> p, CancellationToken ct)
        {
            // IVsDifferenceService requires additional VS interop
            return Task.FromResult(
                "Diff display not yet implemented in VS extension.");
        }

        private async Task<string> RunBuildAsync(CancellationToken ct)
        {
            if (DTE == null)
                return "DTE not available — cannot build.";

            await JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            DTE.Solution.SolutionBuild.Build(true);

            var info = DTE.Solution.SolutionBuild.LastBuildInfo;
            return info == 0
                ? "Build succeeded."
                : $"Build completed with {info} failure(s).";
        }

        private Task<string> RunTerminalAsync(
            Dictionary<string, object> p, CancellationToken ct)
        {
            var command = GetString(p, "command")
                ?? throw new ArgumentException("command is required.");
            return Task.FromResult(
                $"Terminal command '{command}' not yet implemented in VS extension.");
        }

        // ── Helpers ───────────────────────────────────────────────

        private string ResolvePath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
                return relativePath;

            if (SolutionDirectory != null)
                return Path.Combine(SolutionDirectory, relativePath);

            return Path.GetFullPath(relativePath);
        }

        private static string GetString(Dictionary<string, object> p, string key)
        {
            if (p == null || !p.TryGetValue(key, out var value)) return null;
            return value?.ToString();
        }

        private static int? GetInt(Dictionary<string, object> p, string key)
        {
            var s = GetString(p, key);
            if (int.TryParse(s, out var v)) return v;
            return null;
        }
    }
}
