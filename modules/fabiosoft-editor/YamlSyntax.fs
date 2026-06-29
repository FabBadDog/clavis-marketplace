namespace FabioSoft.Editor

/// A YAML highlighting definition for AvalonEdit, inlined as a string. AvalonEdit ships no YAML definition,
/// and Clavis is YAML-heavy (configuration.yaml, state.yaml, themes, the marketplace registry, frontmatter),
/// so this fills a real gap. Loaded the same way as the F# definition (from a StringReader, no embedded
/// resource). Colours follow the dark Clavis palette: blue keys, orange strings, green comments.
[<RequireQualifiedAccess>]
module internal YamlSyntax =

    [<Literal>]
    let definition =
        """<?xml version="1.0"?>
<SyntaxDefinition name="YAML" extensions=".yaml;.yml"
                  xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">

  <Color name="Comment" foreground="#6A9955" />
  <Color name="String" foreground="#CE9178" />
  <Color name="Key" foreground="#9FD5F0" />
  <Color name="Constant" foreground="#C586C0" />
  <Color name="Number" foreground="#B5CEA8" />
  <Color name="Anchor" foreground="#E4C47E" />
  <Color name="Marker" foreground="#9A9AA4" />

  <RuleSet ignoreCase="false">

    <!-- Strings before the comment span so a quoted value containing '#' (e.g. a "#9FD5F0" colour) is not
         mistaken for a comment. -->
    <Span color="String" begin="&quot;" end="&quot;">
      <RuleSet>
        <Span begin="\\" end="." />
      </RuleSet>
    </Span>
    <Span color="String" begin="'" end="'" />

    <Span color="Comment" begin="#" />

    <Rule color="Marker">^(---|\.\.\.)\s*$</Rule>

    <!-- A mapping key: a token immediately followed by a colon and whitespace / end of line. -->
    <Rule color="Key">[\w\-.]+(?=\s*:(\s|$))</Rule>

    <!-- Anchors (&name), aliases (*name) and tags (!name). -->
    <Rule color="Anchor">[&amp;*!][\w\-]+</Rule>

    <Keywords color="Constant">
      <Word>true</Word>
      <Word>false</Word>
      <Word>True</Word>
      <Word>False</Word>
      <Word>null</Word>
      <Word>yes</Word>
      <Word>no</Word>
      <Word>on</Word>
      <Word>off</Word>
    </Keywords>

    <Rule color="Number">
      \b\d+(\.\d+)?\b
    </Rule>

  </RuleSet>
</SyntaxDefinition>"""
