namespace FabioSoft.Contracts.Editor

open System.ComponentModel

/// Published by the code editor whenever the open file, caret, or selection changes - the single source
/// of truth for "what is the user looking at". Consumed by the IdeBridge (translated into the agent's
/// /ide selection awareness) and by any UI that wants to show the open file. Line/column values are
/// 1-based editor coordinates; the bridge converts to the protocol's 0-based form. When nothing is
/// selected the selection start and end coincide with the caret.
[<Sealed>]
type EditorStateChanged
    (filePath: string,
     languageId: string,
     caretLine: int,
     caretColumn: int,
     selectionStartLine: int,
     selectionStartColumn: int,
     selectionEndLine: int,
     selectionEndColumn: int,
     selectedText: string) =

    member _.FilePath = filePath
    member _.LanguageId = languageId
    member _.CaretLine = caretLine
    member _.CaretColumn = caretColumn
    member _.SelectionStartLine = selectionStartLine
    member _.SelectionStartColumn = selectionStartColumn
    member _.SelectionEndLine = selectionEndLine
    member _.SelectionEndColumn = selectionEndColumn
    member _.SelectedText = selectedText

/// The code editor has no file open (e.g. its panel closed). Lets consumers clear "current selection".
[<Sealed>]
type EditorClosed() =
    do ()

/// Ask the code editor to open a file, optionally revealing a 1-based line (0 = no specific line). Sent by
/// the IdeBridge for the agent's /ide openFile, or by any plugin/command that wants to surface a file.
[<Sealed>]
[<Description("Open a file in the code editor")>]
type OpenFileInEditor(filePath: string, line: int) =
    member _.FilePath = filePath
    member _.Line = line
