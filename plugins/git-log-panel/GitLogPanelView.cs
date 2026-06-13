using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Controls;

namespace FabioSoft.Nucleus.Plugins.GitLogPanel;

/// Builds the live git-log view: a scrolling list of commit rows refreshed on a timer. The timer is tied
/// to Loaded/Unloaded so it survives docking-surface rebuilds (which re-parent the view) yet stops when
/// the panel is truly removed. git-log is stateless, so the panel context is unused.
[ExcludeFromCodeCoverage] // WPF construction + process-driven refresh
internal static class GitLogPanelView
{
    public static FrameworkElement Create(GitLogPanelConfig config, PanelInstanceContext context)
    {
        var container = new StackPanel();
        var scroller = new ScrollViewer
        {
            Content = container,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 10, 12, 10)
        };

        var workingDirectory = Directory.GetCurrentDirectory();
        var lastHash = "";
        var refreshing = false;
        DispatcherTimer? timer = null;

        // Run git off the UI thread so a slow or wedged process never freezes the window (the view factory
        // and the refresh timer both fire on the dispatcher). The continuation runs back on the UI thread
        // to touch the visual tree. A re-entrancy guard skips a tick while a refresh is still in flight, so
        // a slow git cannot pile up overlapping processes.
        void Refresh()
        {
            if (refreshing)
            {
                return;
            }

            refreshing = true;
            Task.Run(() => GitLogParse.Parse(GitProcess.Run(workingDirectory, config.MaxCommits)))
                .ContinueWith(
                    task =>
                    {
                        refreshing = false;
                        if (task.IsFaulted)
                        {
                            return; // a failed git call leaves the last good list in place
                        }

                        ApplyRows(task.Result);
                    },
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ApplyRows(IReadOnlyList<CommitRow> rows)
        {
            var topHash = rows.Count > 0 ? rows[0].Hash : "";
            if (topHash == lastHash)
            {
                return;
            }

            lastHash = topHash;
            container.Children.Clear();
            foreach (var row in rows)
            {
                container.Children.Add(CreateRow(row));
            }
        }

        scroller.Loaded += (_, _) =>
        {
            Refresh();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(config.RefreshSeconds) };
            timer.Tick += (_, _) => Refresh();
            timer.Start();
        };

        scroller.Unloaded += (_, _) =>
        {
            timer?.Stop();
            timer = null;
        };

        return scroller;
    }

    private static FrameworkElement CreateRow(CommitRow commit)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // A DockPanel (not a horizontal StackPanel) so the message fills the remaining width and trims with
        // an ellipsis rather than overflowing the panel when it is narrow (horizontal scrolling is off).
        var header = new DockPanel { LastChildFill = true };

        var dot = StatusDot.sized("ClavisBrush", 4);
        dot.Opacity = 0.6;
        dot.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(dot, Dock.Left);
        header.Children.Add(dot);

        var hash = MetadataText.accentSized(commit.Hash, 11);
        hash.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(hash, Dock.Left);
        header.Children.Add(hash);

        var message = new TextBlock
        {
            Text = commit.Message,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        message.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        message.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        header.Children.Add(message);

        row.Children.Add(header);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 2, 0, 0) };

        var author = MetadataText.sized(commit.Author, 9.5);
        author.Margin = new Thickness(0, 0, 6, 0);
        meta.Children.Add(author);

        var time = MetadataText.sized($"· {commit.RelativeTime}", 9.5);
        meta.Children.Add(time);

        row.Children.Add(meta);

        return row;
    }
}
