using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace SharpClaw.VS2026
{
    /// <summary>
    /// VS package that auto-loads when a solution opens and starts the
    /// SharpClaw editor bridge WebSocket connection.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuidString)]
    public sealed class SharpClawVS2026Package : AsyncPackage
    {
        public const string PackageGuidString = "73aaf517-6c51-4dc9-bc84-38996aa139c0";

        private const string DefaultWsUrl = "ws://127.0.0.1:48923/editor/ws";

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            string solutionDir = null;
            if (dte?.Solution?.FullName != null)
                solutionDir = Path.GetDirectoryName(dte.Solution.FullName);

            var vsVersion = dte?.Version ?? "18.0";

            var handler = new VisualStudioActionHandler
            {
                SolutionDirectory = solutionDir,
                DTE = dte,
                JoinableTaskFactory = this.JoinableTaskFactory
            };

            // Run the bridge on a background thread so VS stays responsive
            _ = Task.Run(async () =>
            {
                var url = Environment.GetEnvironmentVariable("SHARPCLAW_EDITOR_WS")
                    ?? DefaultWsUrl;

                using (var client = new EditorBridgeClient(handler))
                {
                    try
                    {
                        await client.ConnectAsync(url, vsVersion, solutionDir,
                            DisposalToken);
                    }
                    catch (OperationCanceledException) { /* VS shutting down */ }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"SharpClaw: Bridge connection failed: {ex.Message}",
                            "SharpClaw.VS");
                    }
                }
            }, DisposalToken);
        }
    }
}
