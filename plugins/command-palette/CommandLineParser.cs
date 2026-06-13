using System.Text;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// A parsed palette input line: the command name, positional argument values, and named (name=value)
/// argument values. ArgumentsText is the raw remainder after the command name, used verbatim when the
/// command is passed through to Claude as a slash command.
public sealed record ParsedCommandLine(
    string Name,
    IReadOnlyList<string> Positional,
    IReadOnlyDictionary<string, string> Named,
    string ArgumentsText);

/// Splits a palette line into a command name plus arguments. Arguments are separated by one or more
/// spaces; double quotes group a value that contains spaces; an unquoted '=' makes an argument named
/// (name=value), otherwise it is positional. Quotes are stripped from the resulting values.
public static class CommandLineParser
{
    private readonly record struct Token(string Text, int EqualsIndex);

    public static ParsedCommandLine Parse(string? input)
    {
        var text = input ?? "";
        var tokens = Tokenize(text);

        if (tokens.Count == 0)
        {
            return new ParsedCommandLine("", [], new Dictionary<string, string>(), "");
        }

        var positional = new List<string>();
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.EqualsIndex >= 0)
            {
                var key = token.Text[..token.EqualsIndex];
                var value = token.Text[(token.EqualsIndex + 1)..];
                named[key] = value;
            }
            else
            {
                positional.Add(token.Text);
            }
        }

        return new ParsedCommandLine(tokens[0].Text, positional, named, ArgumentsAfterName(text));
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var builder = new StringBuilder();
        var equalsIndex = -1;
        var inQuote = false;
        var started = false;

        void Flush()
        {
            if (started)
            {
                tokens.Add(new Token(builder.ToString(), equalsIndex));
                builder.Clear();
                equalsIndex = -1;
                started = false;
            }
        }

        foreach (var character in input)
        {
            if (character == '"')
            {
                inQuote = !inQuote;
                started = true;
            }
            else if (char.IsWhiteSpace(character) && !inQuote)
            {
                Flush();
            }
            else
            {
                started = true;
                if (character == '=' && !inQuote && equalsIndex < 0)
                {
                    equalsIndex = builder.Length;
                }

                builder.Append(character);
            }
        }

        Flush();
        return tokens;
    }

    private static string ArgumentsAfterName(string input)
    {
        var index = 0;
        var inQuote = false;

        // Skip leading whitespace.
        while (index < input.Length && char.IsWhiteSpace(input[index]))
        {
            index++;
        }

        // Skip the name token (honouring quotes), then the whitespace that follows it.
        while (index < input.Length && (inQuote || !char.IsWhiteSpace(input[index])))
        {
            if (input[index] == '"')
            {
                inQuote = !inQuote;
            }

            index++;
        }

        return index >= input.Length ? "" : input[index..].Trim();
    }
}
