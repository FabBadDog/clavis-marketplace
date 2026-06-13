namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Documents

/// Attaches a "copy to clipboard" context menu to a TextBlock, extracting text from its Inlines.
[<ExcludeFromCodeCoverage>]
[<RequireQualifiedAccess>]
module internal CopyMenu =

    let add header (textBlock: TextBlock) =

        let menu = ContextMenu()
        let item = MenuItem(Header = header)
        item.Click.Add(fun _ ->
            let text =
                textBlock.Inlines
                |> Seq.cast<Inline>
                |> Seq.choose (fun inline' ->
                    match inline' with
                    | :? Run as run -> Some run.Text
                    | _ -> None)
                |> String.concat ""
            let text =
                if String.IsNullOrEmpty text then textBlock.Text
                else text
            if not (String.IsNullOrEmpty text) then
                try
                    Clipboard.SetText(text)
                with ex ->
                    // The clipboard is a shared OS resource another process can hold open (clipboard
                    // managers, RDP sessions); a failed copy is cosmetic and must never crash the app.
                    Trace.TraceWarning($"CopyMenu: clipboard copy failed: {ex.Message}"))
        menu.Items.Add(item) |> ignore
        textBlock.ContextMenu <- menu
