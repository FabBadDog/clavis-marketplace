using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// An editable markdown note panel: a rendered view (the shared MarkdownPresenter) that flips to a plain
/// editor on double-click or the pencil affordance. Ctrl+Enter saves and pushes the new template through
/// the panel context so the host persists it; Esc cancels. The per-instance template is the panel's
/// state - seeded from the saved blob, written back through OnStateChanged.
[ExcludeFromCodeCoverage] // WPF construction
internal static class MarkdownPanelView
{
    public static FrameworkElement Create(MarkdownPanelConfig config, PanelInstanceContext context)
    {
        var template = string.IsNullOrEmpty(context.SavedState) ? config.DefaultTemplate : context.SavedState;

        var presenter = new MarkdownPresenter();
        var rendered = new ScrollViewer
        {
            Content = presenter,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12, 10, 12, 10)
        };

        var editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 12, 10),
            Visibility = Visibility.Collapsed
        };
        editor.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        editor.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        editor.SetResourceReference(TextBox.CaretBrushProperty, "ClavisBrush");

        var editHint = new TextBlock
        {
            Text = "Ctrl+Enter save · Esc cancel",
            FontSize = 9.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 12, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        editHint.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        editHint.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var editGlyph = new TextBlock
        {
            Text = "✎",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 6),
            Opacity = 0,
            Cursor = Cursors.Hand
        };
        editGlyph.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        void Render() => presenter.Markdown = template;
        Render();

        void EnterEdit()
        {
            editor.Text = template;
            editor.Visibility = Visibility.Visible;
            rendered.Visibility = Visibility.Collapsed;
            editHint.Visibility = Visibility.Visible;
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
        }

        void ExitEdit(bool save)
        {
            if (save)
            {
                template = editor.Text;
                Render();
                context.OnStateChanged.Invoke(template);
            }

            editor.Visibility = Visibility.Collapsed;
            rendered.Visibility = Visibility.Visible;
            editHint.Visibility = Visibility.Collapsed;
        }

        rendered.PreviewMouseDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                EnterEdit();
                e.Handled = true;
            }
        };

        editGlyph.MouseLeftButtonDown += (_, e) =>
        {
            EnterEdit();
            e.Handled = true;
        };

        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ExitEdit(save: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ExitEdit(save: true);
                e.Handled = true;
            }
        };

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "BlackBrush");
        root.Children.Add(rendered);
        root.Children.Add(editor);
        root.Children.Add(editHint);
        root.Children.Add(editGlyph);
        root.MouseEnter += (_, _) => editGlyph.Opacity = 0.6;
        root.MouseLeave += (_, _) => editGlyph.Opacity = 0;

        return root;
    }
}
