using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// The stand-in view shown while a panel's plugin is still compiling in the background.
internal sealed partial class WindowManager
{
    // A restore placeholder that, while its panel's plugin is still compiling in the background, shows the
    // kind plus a live tail of what the kernel is doing right now (which plugin is compiling / has come up).
    // PlacePanel disposes the subscriptions and fades in the real view once the panel materialises.
    private FrameworkElement CreatePlaceholderView(Guid instanceId, string kind)
    {
        var heading = new TextBlock
        {
            Text = $"loading {kind}…",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var log = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        log.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        log.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");

        var lines = new Queue<string>();
        void Append(string line) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lines.Enqueue(line);
            while (lines.Count > PlaceholderLogLines)
            {
                lines.Dequeue();
            }

            log.Text = string.Join("\n", lines);
        });

        _placeholderSubscriptions[instanceId] =
        [
            _bus.Subscribe<PluginDiscovered>(message =>
            {
                Append($"compiling {message.PluginId}…");
                return Task.CompletedTask;
            }),
            _bus.Subscribe<PluginActivated>(message =>
            {
                Append($"ready {message.PluginId}");
                return Task.CompletedTask;
            })
        ];

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(heading);
        stack.Children.Add(log);

        var border = new Border { Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, "BlackBrush");
        return border;
    }

    private void DisposePlaceholder(Guid instanceId)
    {
        if (_placeholderSubscriptions.Remove(instanceId, out var subscriptions))
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}
