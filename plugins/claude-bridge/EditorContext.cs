using System.Text;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

/// Prepends the active-file path and current selection to a prompt, mirroring what Claude Code's IDE
/// integration attaches on each prompt. Pure: the cached editor state is supplied by the caller. Returns
/// the prompt unchanged when no file is open, so sessions without the code editor are untouched.
public static class EditorContext
{
    public static string Decorate(string prompt, EditorStateChanged? state)
    {
        if (state is null || string.IsNullOrEmpty(state.FilePath))
        {
            return prompt;
        }

        var builder = new StringBuilder();
        builder.Append("<editor-context>\n");
        builder.Append($"Active file: {state.FilePath}\n");
        builder.Append($"Cursor: line {state.CaretLine}, column {state.CaretColumn}\n");
        if (!string.IsNullOrEmpty(state.SelectedText))
        {
            builder.Append($"Selected lines {state.SelectionStartLine}-{state.SelectionEndLine}:\n");
            builder.Append("```\n");
            builder.Append(state.SelectedText);
            if (!state.SelectedText.EndsWith('\n'))
            {
                builder.Append('\n');
            }

            builder.Append("```\n");
        }

        builder.Append("</editor-context>\n\n");
        builder.Append(prompt);
        return builder.ToString();
    }
}
