using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

/// The chat's prompt box and the translucent row it floats in, with its submit/abort/recall keys. It lives
/// inside the chat panel rather than in the window's chrome, so each chat owns its own prompt: the input
/// travels, closes and (later) multiplies with the chat it belongs to.
///
/// The row starts collapsed and slides in once the chat reports a session that can accept prompts, so the
/// user is never offered an input that leads nowhere while the agent is still coming up.
[ExcludeFromCodeCoverage] // WPF construction, keyboard handling and animation; the recall rules are PromptHistory
internal sealed partial class PromptInput
{
    // Beyond this share of the chat's height the box stops growing and scrolls internally.
    private const double MaxHeightShare = 0.6;

    // The framing lines: frame grey at rest, clavis while the box holds keyboard focus (its focus cue,
    // instead of a focus ring).
    private static readonly Color RestLineColor = Color.FromRgb(0x4A, 0x4A, 0x52);
    private static readonly Color FocusedLineColor = Color.FromRgb(0x9F, 0xD5, 0xF0);
    private static readonly Duration LineTween = new(TimeSpan.FromMilliseconds(160));

    private readonly IBus _bus;
    private readonly Border _modeEdge;
    private readonly TextBlock _modeLabel;
    private PromptHistory _history = PromptHistory.Empty;

    public PromptInput(IBus bus)
    {
        _bus = bus;
        Box = CreateBox();
        (Row, _modeEdge, _modeLabel) = CreateRow(Box);

        // The row floats over the bottom edge of the chat so the box can grow up over the output without
        // pushing it; it starts collapsed until the session is ready.
        Row.VerticalAlignment = VerticalAlignment.Bottom;
        Row.Visibility = Visibility.Collapsed;

        Box.PreviewKeyDown += OnPreviewKeyDown;
        WireFocusLine();
    }

    public TextBox Box { get; }

    public Border Row { get; }

    public void Focus() => Box.Focus();

    /// Show the row (with its entrance, taking focus so typing can begin at once) or collapse it again.
    public void SetAvailable(bool available)
    {
        if (available && Row.Visibility != Visibility.Visible)
        {
            Row.Visibility = Visibility.Visible;
            Motion.enter(Row);
            Focus();
        }
        else if (!available)
        {
            Row.Visibility = Visibility.Collapsed;
        }
    }

    /// Let the box grow with its content but never past a share of the chat's height. Re-applied as the
    /// chat area resizes.
    public void CapHeightTo(FrameworkElement chatArea)
    {
        void Apply() => Box.MaxHeight = Math.Max(0.0, chatArea.ActualHeight * MaxHeightShare);

        chatArea.SizeChanged += (_, _) => Apply();
        chatArea.Loaded += (_, _) => Apply();
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
        else if (e.Key == Key.Up && !Box.Text.Contains('\n'))
        {
            Recall(_history.Up(Box.Text));
            e.Handled = true;
        }
        else if (e.Key == Key.Down && !Box.Text.Contains('\n'))
        {
            Recall(_history.Down());
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            Submit();
            e.Handled = true;
        }
    }

    private void Recall((PromptHistory History, string? Text) step)
    {
        _history = step.History;
        if (step.Text is null)
        {
            return;
        }

        Box.Text = step.Text;
        Box.CaretIndex = Box.Text.Length;
    }

    private void Submit()
    {
        var trimmed = Box.Text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        _history = _history.Added(trimmed);
        Box.Text = "";
        _bus.Send(new UserSubmittedPrompt(trimmed));
    }

    private void WireFocusLine()
    {
        var line = new SolidColorBrush(RestLineColor);
        Row.BorderBrush = line;

        void Recolor(bool focused) => line.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(focused ? FocusedLineColor : RestLineColor, LineTween));

        Box.GotKeyboardFocus += (_, _) => Recolor(true);
        Box.LostKeyboardFocus += (_, _) => Recolor(false);
    }

    private static TextBox CreateBox()
    {
        var box = new TextBox
        {
            FontSize = 10,
            Foreground = Frozen("#E8E8EC"),
            // A non-frozen caret brush so it can be tinted to the session's permission mode (animated).
            CaretBrush = new SolidColorBrush(Colors.White),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,

            // No fixed line cap: it grows with its content (capped by CapHeightTo) and scrolls internally
            // once it reaches that ceiling.
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(0)
        };
        box.SetResourceReference(Control.FontFamilyProperty, "AgentFont");
        return box;
    }

    private static (Border Row, Border ModeEdge, TextBlock ModeLabel) CreateRow(TextBox box)
    {
        // A thin vertical rule at the very left edge, tinted to the permission mode (transparent in the
        // default mode). A Border (a rule), not a dot - square corners like all chrome.
        var modeEdge = new Border
        {
            Width = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent)
        };

        // The mode tag: a small uppercase label at the right, shown only for a mode that has an accent.
        var modeLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 9,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(12, 0, 0, 0)
        };
        modeLabel.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");

        // Content sits inset from the edges (the 3px rule plus a gap makes up the left inset); the tag docks
        // right, the box fills the rest.
        var content = new DockPanel { Margin = new Thickness(25, 8, 28, 8) };
        DockPanel.SetDock(modeLabel, Dock.Right);
        content.Children.Add(modeLabel);
        content.Children.Add(box);

        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(modeEdge);

        // A translucent dark veil so the row floats over the chat output, which reads faintly through it.
        var row = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = Frozen("#D80A0A10"),
            Child = grid
        };
        return (row, modeEdge, modeLabel);
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
