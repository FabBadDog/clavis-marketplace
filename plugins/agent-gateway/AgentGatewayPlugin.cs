using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using FabioSoft.Nucleus.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// Hosts the in-process MCP server the in-Clavis agent connects back to. On activation it listens on a
/// per-launch named pipe (one process, served by the bus-backed ClavisTools), publishes the mcp-config and
/// system-prompt guide on the bus (ClavisMcpAvailable) so ClaudeBridge attaches them inline to each session,
/// and mirrors bus activity into a ring for the recent_activity tool.
///
/// The agent reaches the pipe only through a generic stdio bridge it spawns itself (the mcp-config points
/// at FabioSoft.NamedPipeStdioBridge): claude speaks MCP over that child's stdin/stdout, the bridge pumps
/// bytes to this pipe. There is no TCP surface, and the pipe is ACL'd to the current user. Because
/// System.IO.Pipes roots no statics (unlike Kestrel/ASP.NET), this plugin's AssemblyLoadContext stays
/// collectible, so it is a normal, unloadable, optional plugin.
[ExcludeFromCodeCoverage(Justification = "Integration shell: hosts a named-pipe MCP server and does file IO; the testable logic lives in SendableMessages and ActivityRing.")]
public sealed class AgentGatewayPlugin : IPlugin<AgentGatewayConfig>
{
    private const int ActivityRingCapacity = 2000;
    private const string McpServerName = "clavis";
    private const string BridgeExecutable = "FabioSoft.NamedPipeStdioBridge.exe";

    public string Id => "AgentGateway";

    public AgentGatewayConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(AgentGatewayConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, AgentGatewayConfig config)
    {
        var clavisHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clavis");
        var context = new GatewayContext(
            bus,
            new ActivityRing(ActivityRingCapacity),
            new SendableMessages(),
            new SelectionBroker(),
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            Path.Combine(clavisHome, "logs"));

        var activitySubscription = bus.Activity.Subscribe(new ActivityObserver(context.Activity));

        // The ask_user tool's answers come back as ordinary bus messages; route them onto the broker.
        var selectionSubscription = bus.Subscribe<SelectionCompleted>(answer =>
        {
            context.Selections.Complete(answer);
            return Task.CompletedTask;
        });

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddMcpServer().WithTools<ClavisTools>();
        var provider = services.BuildServiceProvider();
        var serverOptions = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        // A fresh, unguessable pipe name per launch; the DACL restricts connect to the current user, so no
        // other user's (or lower-integrity) process can reach it - the boundary loopback TCP cannot enforce.
        var pipeName = $"clavis-mcp-{Guid.NewGuid():N}";
        var cancellation = new CancellationTokenSource();
        var acceptLoop = Task.Run(() => AcceptLoopAsync(pipeName, serverOptions, provider, cancellation.Token));

        bus.Send(new ClavisMcpAvailable(BuildMcpConfigJson(pipeName), ClavisDocs.McpGuide));
        bus.LogInfo(Id, $"agent gateway MCP server listening on named pipe '{pipeName}' (mcp-config published on the bus)");

        return Task.FromResult<IDisposable>(
            new GatewayDisposable(cancellation, acceptLoop, provider, activitySubscription, selectionSubscription));
    }

    // One pipe-server instance is created per connection so concurrent agent sessions (multiple windows) can
    // each hold their own; a dropped or failed connection must never take down the loop.
    private static async Task AcceptLoopAsync(
        string pipeName, McpServerOptions serverOptions, IServiceProvider provider, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(pipeName);
            }
            catch when (token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await pipe.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch
            {
                await pipe.DisposeAsync();
                continue;
            }

            _ = HandleConnectionAsync(pipe, serverOptions, provider, token);
        }
    }

    private static async Task HandleConnectionAsync(
        NamedPipeServerStream pipe, McpServerOptions serverOptions, IServiceProvider provider, CancellationToken token)
    {
        try
        {
            await using (pipe)
            await using (var transport = new StreamServerTransport(pipe, pipe, McpServerName))
            await using (var server = McpServer.Create(transport, serverOptions, NullLoggerFactory.Instance, provider))
            {
                await server.RunAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch
        {
            // A dropped connection or protocol fault on one session must not affect the others.
        }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        // Grant the current user FullControl only - no other user or lower-integrity process can open the
        // pipe. ReadWrite alone is insufficient: a duplex async client also needs Synchronize, and the
        // server needs CreateNewInstance to spin up further instances for concurrent sessions.
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    // The agent reaches the in-process MCP server only by spawning the generic stdio bridge named here:
    // claude speaks MCP over that child's stdin/stdout and the bridge pumps bytes to our named pipe. The
    // result is the standard --mcp-config shape; ClaudeBridge passes it to claude inline (no file on disk).
    private static string BuildMcpConfigJson(string pipeName)
    {
        var bridgePath = Path.Combine(AppContext.BaseDirectory, BridgeExecutable);
        var mcpConfig = new
        {
            mcpServers = new Dictionary<string, object>
            {
                [McpServerName] = new { type = "stdio", command = bridgePath, args = new[] { pipeName } },
            },
        };
        return JsonSerializer.Serialize(mcpConfig);
    }

    private sealed class ActivityObserver(ActivityRing ring) : IObserver<BusActivity>
    {
        public void OnNext(BusActivity value) => ring.Record(value);

        public void OnError(Exception error) { }

        public void OnCompleted() { }
    }

    private sealed class GatewayDisposable(
        CancellationTokenSource cancellation,
        Task acceptLoop,
        IServiceProvider provider,
        IDisposable activitySubscription,
        IDisposable selectionSubscription) : IDisposable
    {
        public void Dispose()
        {
            try { cancellation.Cancel(); }
            catch { /* cleanup best-effort */ }

            try { acceptLoop.Wait(TimeSpan.FromSeconds(2)); }
            catch { /* the loop ends on cancellation; a slow exit must not block teardown */ }

            try { activitySubscription.Dispose(); }
            catch { /* cleanup best-effort */ }

            try { selectionSubscription.Dispose(); }
            catch { /* cleanup best-effort */ }

            try { (provider as IDisposable)?.Dispose(); }
            catch { /* cleanup best-effort */ }

            try { cancellation.Dispose(); }
            catch { /* cleanup best-effort */ }
        }
    }
}
