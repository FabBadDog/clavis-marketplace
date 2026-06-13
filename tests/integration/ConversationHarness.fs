namespace FabioSoft.Clavis.IntegrationTests

open System
open System.IO
open System.Threading.Tasks
open FabioSoft.Claude
open FabioSoft.Clavis.TestKit
open FabioSoft.Nucleus.Plugins.ClaudeBridge
open FabioSoft.Nucleus.Plugins.Conversation

/// Boots the real ClaudeBridge + Conversation plugins on a headless bus with a mocked agent injected, so a
/// test drives the full agent loop (prompt -> session -> streamed events -> effects) and asserts on bus
/// traffic. `AttachClavisMcp` is forced off and a temp working directory is used so no real ~/.clavis is read.
[<RequireQualifiedAccess>]
module ConversationHarness =

    let private emptyUsage =
        Func<Task<UsageWindow[]>>(fun () -> Task.FromResult Array.empty<UsageWindow>)

    // Long init timeout so it never fires mid-test; a temp working dir so no real folder is touched.
    let private defaultConfig =
        ConversationConfig(InitTimeoutSeconds = 600, WorkingDirectory = Path.GetTempPath())

    let bootWith (config: ConversationConfig) (usage: Func<Task<UsageWindow[]>>) (agent: MockAgent) : Task<Harness> =
        let bridge = ClaudeBridgePlugin()
        bridge.SessionFactory <- agent.SessionFactory
        bridge.UsageFetcher <- usage
        let bridgeConfig = ClaudeBridgeConfig(AttachClavisMcp = false)
        let conversation = ConversationPlugin()

        Harness.boot
            [ (fun bus -> bridge.ActivateAsync(bus, bridgeConfig))
              (fun bus -> conversation.ActivateAsync(bus, config)) ]

    let start (agent: MockAgent) : Task<Harness> = bootWith defaultConfig emptyUsage agent

    let startWithUsage (windows: UsageWindow[]) (agent: MockAgent) : Task<Harness> =
        bootWith defaultConfig (Func<Task<UsageWindow[]>>(fun () -> Task.FromResult windows)) agent
