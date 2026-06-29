namespace FabioSoft.Editor

open System
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Xml
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Highlighting
open ICSharpCode.AvalonEdit.Highlighting.Xshd

/// Resolves a file path to the AvalonEdit highlighting definition to use. The only custom case is F#
/// (AvalonEdit ships no F# definition, so we load our own inlined definition once into the shared
/// HighlightingManager); everything else defers to AvalonEdit's built-in extension table. languageForExtension
/// is the pure, testable seam; the rest touches AvalonEdit's static manager and is excluded from coverage.
[<RequireQualifiedAccess>]
module CodeEditorSyntax =

    [<Literal>]
    let FSharp = "F#"

    [<Literal>]
    let Markdown = "Markdown"

    [<Literal>]
    let YAML = "YAML"

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
        | ".md" -> Markdown
        | ".ps1" | ".psm1" | ".psd1" -> "PowerShell"
        | ".py" -> "Python"
        | ".sql" -> "SQL"
        | ".yaml" | ".yml" -> YAML
        | _ -> "Text"

    /// Maps a language label to the name of AvalonEdit's built-in highlighting definition for it. Routing by
    /// the resolved label rather than the raw file extension makes the whole XML family (.xaml/.fsproj/.props/
    /// .targets/...) highlight uniformly - AvalonEdit registers only some of those extensions. None means the
    /// label has no built-in definition (handled by a custom one, or left as plain text).
    let builtInDefinitionName (label: string) : string option =
        match label with
        | "C#" -> Some "C#"
        | "XML" -> Some "XML"
        | "JSON" -> Some "Json"
        | "JavaScript" -> Some "JavaScript"
        | "HTML" -> Some "HTML"
        | "CSS" -> Some "CSS"
        | "PowerShell" -> Some "PowerShell"
        | "Python" -> Some "Python"
        | "SQL" -> Some "TSQL"
        | _ -> None

    [<ExcludeFromCodeCoverage>] // loads an inlined definition into AvalonEdit's static HighlightingManager
    let private loadDefinition (xshd: string) =
        use reader = XmlReader.Create(new StringReader(xshd))
        HighlightingLoader.Load(reader, HighlightingManager.Instance)

    [<ExcludeFromCodeCoverage>]
    let private fsharpDefinition = lazy (loadDefinition FSharpSyntax.definition)

    [<ExcludeFromCodeCoverage>]
    let private yamlDefinition = lazy (loadDefinition YamlSyntax.definition)

    [<ExcludeFromCodeCoverage>] // delegates to AvalonEdit's static HighlightingManager
    let forPath (path: string) : IHighlightingDefinition =
        match languageForExtension path with
        | FSharp -> fsharpDefinition.Value
        | YAML -> yamlDefinition.Value
        | label ->
            match builtInDefinitionName label with
            | Some name -> HighlightingManager.Instance.GetDefinition(name)
            | None -> HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path))

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

    let background  = brushFromRgb 0x00uy 0x00uy 0x00uy
    let foreground  = brushFromRgb 0xC8uy 0xC8uy 0xD0uy
    let lineNumbers = brushFromRgb 0xB0uy 0xB0uy 0xBAuy

    // A translucent neutral selection band, matching the app's text-selection convention (the gray
    // SecondaryBrush at partial opacity) rather than AvalonEdit's default green. The brush carries its own
    // alpha; selected text keeps its syntax colour because SelectionForeground is left unset.
    let selection = SolidColorBrush(Color.FromArgb(0x66uy, 0x9Auy, 0x9Auy, 0xA4uy)) |> freeze

    [<Literal>]
    let MonoFontKey = "MonoFont"

    [<Literal>]
    let AgentFontKey = "AgentFont"

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
    let mutable isMarkdown = false

    let resolveFamily key (fallback: string) =
        match Application.Current with
        | null -> FontFamily(fallback)
        | app ->
            match app.TryFindResource(key) with
            | :? FontFamily as family -> family
            | _ -> FontFamily(fallback)

    // Markdown files are not highlighted as code: this colorizer renders their source to mirror Clavis'
    // markdown rendering (headings, bold, italic, inline code), reading the body size and host fonts live.
    let markdownColorizer =
        MarkdownColorizer(
            (fun () -> isMarkdown),
            (fun () -> editor.FontSize),
            (fun () -> resolveFamily EditorTheme.AgentFontKey "Segoe UI"),
            (fun () -> resolveFamily EditorTheme.MonoFontKey "Consolas"))

    do
        editor.ShowLineNumbers <- true
        editor.WordWrap <- false
        editor.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
        editor.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto
        editor.FontSize <- 12.5
        editor.Background <- EditorTheme.background
        editor.Foreground <- EditorTheme.foreground
        editor.LineNumbersForeground <- EditorTheme.lineNumbers
        editor.TextArea.SelectionBrush <- EditorTheme.selection
        editor.TextArea.SelectionForeground <- null
        editor.TextArea.SelectionBorder <- null
        editor.TextArea.SelectionCornerRadius <- 0.0
        editor.Options.HighlightCurrentLine <- true
        editor.Options.EnableHyperlinks <- false
        editor.Options.EnableEmailHyperlinks <- false
        editor.Options.IndentationSize <- 4
        editor.Padding <- Thickness(6.0, 4.0, 6.0, 4.0)
        editor.SetResourceReference(Control.FontFamilyProperty, EditorTheme.MonoFontKey)
        editor.TextArea.TextView.LineTransformers.Add(markdownColorizer)
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
        isMarkdown <- CodeEditorSyntax.languageForExtension path = CodeEditorSyntax.Markdown
        if isMarkdown then
            // Let the markdown colorizer own rendering; AvalonEdit's built-in markdown highlighter is what
            // blows headings up into oversized coloured text.
            editor.SyntaxHighlighting <- null
        else
            editor.SyntaxHighlighting <- CodeEditorSyntax.forPath path
        editor.TextArea.TextView.Redraw()

    member _.FocusEditor() = editor.Focus() |> ignore

    [<CLIEvent>]
    member _.TextChanged = textChanged.Publish

    [<CLIEvent>]
    member _.CaretOrSelectionChanged = caretOrSelectionChanged.Publish
