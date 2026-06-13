namespace FabioSoft.Editor

open System
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Reflection
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Xml
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Highlighting
open ICSharpCode.AvalonEdit.Highlighting.Xshd

/// Resolves a file path to the AvalonEdit highlighting definition to use. The only custom case is F#
/// (AvalonEdit ships no F# definition, so we load our own bundled .xshd once into the shared
/// HighlightingManager); everything else defers to AvalonEdit's built-in extension table. languageForExtension
/// is the pure, testable seam; the rest touches AvalonEdit's static manager and is excluded from coverage.
[<RequireQualifiedAccess>]
module CodeEditorSyntax =

    [<Literal>]
    let private fsharpResourceName = "FabioSoft.Editor.FSharp.xshd"

    [<Literal>]
    let FSharp = "F#"

    /// A human-readable language label for a file path - used for the status display and to decide
    /// F#-specific highlighting. "Text" when the extension is unknown.
    let languageForExtension (path: string) =
        match (Path.GetExtension(path)).ToLowerInvariant() with
        | ".fs" | ".fsi" | ".fsx" -> FSharp
        | ".cs" -> "C#"
        | ".xaml" | ".xml" | ".xshd" | ".csproj" | ".fsproj" | ".props" | ".targets" | ".config" -> "XML"
        | ".json" -> "JSON"
        | ".js" -> "JavaScript"
        | ".html" | ".htm" -> "HTML"
        | ".css" -> "CSS"
        | ".md" -> "Markdown"
        | ".ps1" | ".psm1" | ".psd1" -> "PowerShell"
        | ".py" -> "Python"
        | ".sql" -> "SQL"
        | ".yaml" | ".yml" -> "YAML"
        | _ -> "Text"

    [<ExcludeFromCodeCoverage>] // loads an embedded resource into AvalonEdit's static HighlightingManager
    let private loadFSharpDefinition () =
        use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fsharpResourceName)
        use reader = XmlReader.Create(stream)
        HighlightingLoader.Load(reader, HighlightingManager.Instance)

    [<ExcludeFromCodeCoverage>]
    let private fsharpDefinition = lazy (loadFSharpDefinition ())

    [<ExcludeFromCodeCoverage>] // delegates to AvalonEdit's static HighlightingManager
    let forPath (path: string) : IHighlightingDefinition =
        if languageForExtension path = FSharp then
            fsharpDefinition.Value
        else
            HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path))

/// Editor theme defaults, baked as constants rather than pulled from a shared theme assembly so this
/// control stays an application-neutral library depending only on AvalonEdit and WPF. The font follows
/// the host's "MonoFont" resource (if present) so it matches the surrounding chrome; the colours are
/// sensible dark-editor defaults.
[<ExcludeFromCodeCoverage>]
[<RequireQualifiedAccess>]
module internal EditorTheme =

    let private freeze (brush: SolidColorBrush) =
        brush.Freeze()
        brush

    let private brushFromRgb red green blue =
        SolidColorBrush(Color.FromRgb(red, green, blue)) |> freeze

    let background  = brushFromRgb 0x14uy 0x14uy 0x1Cuy
    let foreground  = brushFromRgb 0xC8uy 0xC8uy 0xD0uy
    let lineNumbers = brushFromRgb 0xB0uy 0xB0uy 0xBAuy

    [<Literal>]
    let MonoFontKey = "MonoFont"

/// Hosts AvalonEdit's TextEditor. AvalonEdit is built on DependencyProperty/static registries, which root
/// their owner types forever, so it must live in a module (Default ALC, never unloaded) rather
/// than in a collectible plugin. Plugins talk to it only through this plain-CLR surface (no new dependency
/// property), so they need no AvalonEdit reference and stay reloadable.
[<ExcludeFromCodeCoverage>] // WPF construction
type CodeEditor() as this =
    inherit ContentControl()

    let editor = TextEditor()
    let textChanged = Event<EventHandler, EventArgs>()
    let caretOrSelectionChanged = Event<EventHandler, EventArgs>()
    let mutable currentPath = ""

    do
        editor.ShowLineNumbers <- true
        editor.WordWrap <- false
        editor.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
        editor.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto
        editor.FontSize <- 12.5
        editor.Background <- EditorTheme.background
        editor.Foreground <- EditorTheme.foreground
        editor.LineNumbersForeground <- EditorTheme.lineNumbers
        editor.Options.HighlightCurrentLine <- true
        editor.Options.EnableHyperlinks <- false
        editor.Options.EnableEmailHyperlinks <- false
        editor.Options.IndentationSize <- 4
        editor.Padding <- Thickness(6.0, 4.0, 6.0, 4.0)
        editor.SetResourceReference(Control.FontFamilyProperty, EditorTheme.MonoFontKey)
        editor.TextChanged.Add(fun args -> textChanged.Trigger(this, args))
        editor.TextArea.Caret.PositionChanged.Add(fun _ -> caretOrSelectionChanged.Trigger(this, EventArgs.Empty))
        editor.TextArea.SelectionChanged.Add(fun _ -> caretOrSelectionChanged.Trigger(this, EventArgs.Empty))
        this.HorizontalContentAlignment <- HorizontalAlignment.Stretch
        this.VerticalContentAlignment <- VerticalAlignment.Stretch
        this.Content <- editor

    member _.Text
        with get () = editor.Text
        and set (value: string) = editor.Text <- value

    member _.IsReadOnly
        with get () = editor.IsReadOnly
        and set (value: bool) = editor.IsReadOnly <- value

    member _.SourcePath = currentPath

    member _.Language = CodeEditorSyntax.languageForExtension currentPath

    member _.CaretLine = editor.TextArea.Caret.Line

    member _.CaretColumn = editor.TextArea.Caret.Column

    member _.CaretOffset = editor.CaretOffset

    member _.SelectionStart = editor.SelectionStart

    member _.SelectionLength = editor.SelectionLength

    member _.SelectedText = editor.SelectedText

    member _.SelectionStartLine = editor.Document.GetLocation(editor.SelectionStart).Line

    member _.SelectionStartColumn = editor.Document.GetLocation(editor.SelectionStart).Column

    member _.SelectionEndLine = editor.Document.GetLocation(editor.SelectionStart + editor.SelectionLength).Line

    member _.SelectionEndColumn = editor.Document.GetLocation(editor.SelectionStart + editor.SelectionLength).Column

    /// Move the caret to a 1-based line and reveal it (used when a file is opened at a target line).
    member _.RevealLine(line: int) =
        let safeLine = max 1 (min line editor.Document.LineCount)
        let documentLine = editor.Document.GetLineByNumber(safeLine)
        editor.CaretOffset <- documentLine.Offset
        editor.ScrollToLine(safeLine)

    member _.SetSourcePath(path: string) =
        currentPath <- path
        editor.SyntaxHighlighting <- CodeEditorSyntax.forPath path

    member _.FocusEditor() = editor.Focus() |> ignore

    [<CLIEvent>]
    member _.TextChanged = textChanged.Publish

    [<CLIEvent>]
    member _.CaretOrSelectionChanged = caretOrSelectionChanged.Publish
