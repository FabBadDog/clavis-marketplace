using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// The prompt input's ambient mode accent: a coloured left rule, caret, and a small tag that follow the
// session's permission mode. The default/none mode has no accent, so the input returns to neutral - the
// absence of colour is itself the signal for that mode. Every change animates (colour tween + tag fade)
// per the design language's everything-animates rule. The mode -> accent-key decision is the shared,
// unit-tested ModeAccent map, so this file carries only the WPF projection. (The [ExcludeFromCodeCoverage]
// covering this WPF-only class sits on the primary WindowHost partial - it may not be repeated here.)
internal sealed partial class WindowHost
{
    private static readonly Color NeutralCaret = Colors.White;
    private static readonly Duration ModeTween = new(TimeSpan.FromMilliseconds(250));

    // The mode last applied, so a redundant relay (a re-broadcast capabilities snapshot carrying the same
    // mode) does not re-animate an unchanged accent.
    private string? _currentMode;

    /// Dress the prompt input in the session's permission-mode accent. Mode is the provider-neutral id;
    /// displayName is the tag text. Called on the dispatcher by the WindowManager when the conversation
    /// relays PromptModeChanged.
    public void SetSessionMode(string mode, string displayName)
    {
        if (_inputBox is null || _modeEdge is null || _modeLabel is null)
        {
            return;
        }

        if (string.Equals(mode, _currentMode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentMode = mode;

        var accentKey = ModeAccent.resourceKeyOrNull(mode);
        if (accentKey is null)
        {
            // Default/none mode: fade the accent away and hide the tag - a neutral prompt is the signal.
            AnimateEdge(Colors.Transparent);
            AnimateCaret(NeutralCaret);
            FadeOutModeLabel();
            return;
        }

        var accent = AccentColor(accentKey);
        AnimateEdge(accent);
        AnimateCaret(accent);
        ShowModeLabel(displayName, accent);
    }

    private Color AccentColor(string resourceKey)
    {
        var brush = (Window.TryFindResource(resourceKey)
            ?? Application.Current?.TryFindResource(resourceKey)) as SolidColorBrush;
        return brush?.Color ?? NeutralCaret;
    }

    private void AnimateEdge(Color target)
    {
        if (_modeEdge!.Background is not SolidColorBrush brush)
        {
            brush = new SolidColorBrush(Colors.Transparent);
            _modeEdge.Background = brush;
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty, ColorTween(target));
    }

    private void AnimateCaret(Color target)
    {
        if (_inputBox!.CaretBrush is not SolidColorBrush brush)
        {
            brush = new SolidColorBrush(NeutralCaret);
            _inputBox.CaretBrush = brush;
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty, ColorTween(target));
    }

    // Set the tag text/colour and fade it in. Fading in on every change (not only from hidden) gives the
    // switch a visible acknowledgement when moving straight from one accented mode to another.
    private void ShowModeLabel(string displayName, Color accent)
    {
        _modeLabel!.Text = displayName.ToUpperInvariant();
        _modeLabel.Foreground = new SolidColorBrush(accent);
        _modeLabel.Visibility = Visibility.Visible;
        Motion.appear(_modeLabel);
    }

    private void FadeOutModeLabel()
    {
        if (_modeLabel!.Visibility != Visibility.Visible)
        {
            return;
        }

        Motion.disappear(_modeLabel, () => _modeLabel.Visibility = Visibility.Collapsed);
    }

    private static ColorAnimation ColorTween(Color target) =>
        new(target, ModeTween) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
}
