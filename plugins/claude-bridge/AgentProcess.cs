using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using FabioSoft.Claude;

// The enclosing FabioSoft namespace contains a Process *namespace*, which name resolution reaches before the
// imported System.Diagnostics.Process type; the alias keeps the type reachable.
using DiagnosticsProcess = System.Diagnostics.Process;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

/// The two provider invocations that are not a session: asking what is running, and handing one back. Both are
/// pure process I/O - the argument lists and the parsing they feed live in FabioSoft.Claude.AgentInstances,
/// where they are tested.
[ExcludeFromCodeCoverage]
internal static class AgentProcess
{
    private const string Executable = "claude";

    /// Run the discovery command and return its raw output, or "" when it could not be run. Discovery failing
    /// must read as "no instances known" rather than as an error: the caller is answering a request and a
    /// missing answer would hang it.
    public static async Task<string> ListAsync(TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in AgentInstances.listArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = DiagnosticsProcess.Start(startInfo);
            if (process is null)
            {
                return "";
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            using var expiry = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(expiry.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { /* best-effort */ }
                return "";
            }

            return process.ExitCode == 0 ? await stdout : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// Ask the CLI to stop a running background agent, and wait for it to have happened. Adoption depends on the
    /// waiting: while the agent runs, resuming its session is refused outright ("currently running as a
    /// background agent"), so returning before it let go would just move the failure downstream. False means the
    /// agent may still hold the session and the caller must not resume it.
    public static async Task<bool> StopAsync(string agentId, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in AgentInstances.stopArguments(agentId))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = DiagnosticsProcess.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var expiry = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(expiry.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { /* best-effort */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// Hand a session back to a durable background agent and walk away. Deliberately unlike every other spawn
    /// here: no streams are redirected and the process is never waited on or killed, because the entire point is
    /// that it outlives Clavis. Holding a pipe we never drain would block it; holding the handle would tie its
    /// life to ours.
    public static bool HandOff(string instanceId, string name, string workingDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            foreach (var argument in AgentInstances.handOffArguments(instanceId, name ?? ""))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = DiagnosticsProcess.Start(startInfo);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
