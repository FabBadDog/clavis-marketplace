using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.CommandPalette;

/// The command palette's client of the shared SelectorWindow: free-text selection over the command
/// catalog with routing-backed validation, plus the palette-specific Alt+Enter shortcut capture. All
/// command logic stays in the pure router/suggestions; all popup interaction lives in the shared control.
[ExcludeFromCodeCoverage]
internal sealed class PaletteSelector
{
    private readonly Func<string, RouteOutcome> _route;
    private readonly Action<RouteOutcome> _perform;
    private readonly Action<string, string> _assignBinding;
    private readonly Action<string> _removeBinding;
    private readonly SelectorWindow _window;

    private const string LoadingText = "Loading skills…";
    private const string FooterHint = "Enter to run · Alt+Enter to bind · Alt+Backspace to unbind";

    private RouteOutcome? _pendingOutcome;
    private bool _capturing;
    private string _captureCommand = "";

    public PaletteSelector(
        double width,
        Func<string, IReadOnlyList<CommandItem>> getSuggestions,
        Func<string, RouteOutcome> route,
        Action<RouteOutcome> perform,
        Action<string, string> assignBinding,
        Action<string> removeBinding)
    {
        _route = route;
        _perform = perform;
        _assignBinding = assignBinding;
        _removeBinding = removeBinding;

        _window = new SelectorWindow(new SelectorOptions
        {
            Width = width,
            FreeText = true,
            ShowInputRule = false,
            GetSuggestions = input => getSuggestions(input),
            ItemTemplate = LoadTemplate("CommandItemTemplate"),
            DetailTemplate = LoadTemplate("CommandDetailTemplate"),
            FooterHint = FooterHint,
            Validate = Validate,
            OnAccept = (_, _) => Accept(),
            CompleteText = CompleteText,
            OnUnhandledKey = HandleKey,
        });
    }

    public bool IsVisible => _window.IsVisible;

    public void Show() => _window.ShowSelector();

    public void Hide() => _window.Dismiss();

    /// Toggles the footer's "loading skills" indicator. Agent commands and skills arrive asynchronously
    /// (AgentCommandsAvailable), so an early-opened palette shows this until they land.
    public void SetLoading(bool loading) => _window.SetBusy(loading, LoadingText);

    /// Re-query the suggestions in place (keeping the highlighted row), so a shortcut bound or removed
    /// while the palette is open updates the displayed shortcut without a close/reopen.
    public void RefreshSuggestions() => _window.Refresh();

    public void Close() => _window.Dispatcher.Invoke(_window.Close);

    /// Routes the submitted line. A partially typed name is not itself a recognised command (NoMatch);
    /// fall back to running the highlighted suggestion, keeping any typed arguments, so Enter executes
    /// the command the user sees selected - the completed line becomes the canonical text recorded to
    /// history. A complete command (or one with bad arguments) is judged as typed.
    private SelectorValidation Validate(string text, object? selected)
    {
        var outcome = _route(text);
        var canonical = text.Trim();

        if (outcome is NoMatch && selected is CommandItem item)
        {
            var parsed = CommandLineParser.Parse(text);
            canonical = parsed.ArgumentsText.Length == 0
                ? item.Name
                : $"{item.Name} {parsed.ArgumentsText}";
            outcome = _route(canonical);
        }

        switch (outcome)
        {
            case SendBusMessage or SendAgentPrompt:
                _pendingOutcome = outcome;
                return SelectorValidation.Valid(canonical);
            case RouteError error:
                return SelectorValidation.Invalid(error.Message);
            default:
                return SelectorValidation.Invalid("Unknown command");
        }
    }

    private void Accept()
    {
        if (_pendingOutcome is { } outcome)
        {
            _pendingOutcome = null;
            _perform(outcome);
        }
    }

    private static string CompleteText(object item, string currentText)
    {
        if (item is not CommandItem command)
        {
            return currentText;
        }

        var parsed = CommandLineParser.Parse(currentText);
        return parsed.ArgumentsText.Length == 0 ? command.Name + " " : $"{command.Name} {parsed.ArgumentsText}";
    }

    // Alt+Enter on a bindable command starts a one-key capture; the next chord is recorded as its
    // application-scope shortcut. Commands that need arguments are rejected with a message.
    private bool HandleKey(KeyEventArgs e)
    {
        if (_capturing)
        {
            CaptureGesture(e);
            return true;
        }

        // With Alt held, WPF delivers the key as Key.System with the real key in SystemKey, so resolve it
        // the same way CaptureGesture does - otherwise Alt+Enter never matches.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            BeginCapture();
            return true;
        }

        if (key == Key.Back && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            RemoveBinding();
            return true;
        }

        return false;
    }

    private void BeginCapture()
    {
        if (_window.SelectedItem is not CommandItem item)
        {
            return;
        }

        if (!item.IsBindable)
        {
            _window.ShowMessage($"'{item.DisplayName}' takes arguments and cannot be bound to a shortcut");
            return;
        }

        _capturing = true;
        _captureCommand = item.Name;
        _window.ShowMessage($"Press a shortcut for '{item.DisplayName}' - Esc to cancel");
    }

    // Alt+Backspace removes the highlighted command's shortcut (the application binding shown beside it).
    // A command with no shortcut reports so and is left untouched.
    private void RemoveBinding()
    {
        if (_window.SelectedItem is not CommandItem item)
        {
            return;
        }

        if (string.IsNullOrEmpty(item.Shortcut))
        {
            _window.ShowMessage($"'{item.DisplayName}' has no shortcut to remove");
            return;
        }

        _removeBinding(item.Shortcut);
        _window.ShowMessage($"Removed {item.Shortcut}");
    }

    private void CaptureGesture(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _capturing = false;
            _window.ClearMessage();
            return;
        }

        // Alt routes the real key through SystemKey; resolve it so combinations like Alt+L are captured.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = KeyGestureReader.canonical(Keyboard.Modifiers, key);
        if (gesture.Length == 0)
        {
            return; // modifier-only or unmappable; keep waiting for a real key
        }

        _assignBinding(_captureCommand, gesture);
        _capturing = false;
        _window.ShowMessage($"Bound {gesture}");
    }

    // Both row templates are loose XAML resources whose dictionary key matches the file name.
    private static DataTemplate LoadTemplate(string key)
    {
        var assemblyName = typeof(PaletteSelector).Assembly.GetName().Name;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assemblyName};component/Views/{key}.xaml")
        };
        return (DataTemplate)dictionary[key];
    }
}
