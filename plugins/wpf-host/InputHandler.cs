using System.Windows.Controls;
using System.Windows.Input;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

internal sealed class InputHandler
{
    private readonly IBus _bus;
    private readonly TextBox _inputBox;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private string _historyDraft = "";

    public InputHandler(IBus bus, TextBox inputBox)
    {
        _bus = bus;
        _inputBox = inputBox;
        _inputBox.PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _bus.Send(new UserCancelledQueued());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _bus.Send(new UserAborted());
            e.Handled = true;
        }
        else if (e.Key == Key.Up && !_inputBox.Text.Contains('\n'))
        {
            NavigateHistoryUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && !_inputBox.Text.Contains('\n'))
        {
            NavigateHistoryDown();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            Submit();
            e.Handled = true;
        }
    }

    private void Submit()
    {
        var trimmed = _inputBox.Text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        _history.Add(trimmed);
        _historyIndex = -1;
        _historyDraft = "";
        _inputBox.Text = "";
        _bus.Send(new UserSubmittedPrompt(trimmed));
    }

    private void NavigateHistoryUp()
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex == -1)
        {
            _historyDraft = _inputBox.Text;
            _historyIndex = _history.Count - 1;
        }
        else if (_historyIndex > 0)
        {
            _historyIndex--;
        }

        _inputBox.Text = _history[_historyIndex];
        _inputBox.CaretIndex = _inputBox.Text.Length;
    }

    private void NavigateHistoryDown()
    {
        if (_historyIndex < 0)
        {
            return;
        }

        _historyIndex++;
        if (_historyIndex >= _history.Count)
        {
            _historyIndex = -1;
            _inputBox.Text = _historyDraft;
        }
        else
        {
            _inputBox.Text = _history[_historyIndex];
        }

        _inputBox.CaretIndex = _inputBox.Text.Length;
    }

    public void Detach() => _inputBox.PreviewKeyDown -= OnPreviewKeyDown;

    public void Focus() => _inputBox.Focus();
}
