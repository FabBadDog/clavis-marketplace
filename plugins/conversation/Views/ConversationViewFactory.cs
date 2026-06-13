using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FabioSoft.Nucleus.Contracts;
using FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

public static class ConversationViewFactory
{
    // How far Ctrl+Up/Down scrolls the chat, in device-independent pixels (the chat scrolls by pixel).
    private const double ScrollStep = 90;

    public static ResourceDictionary LoadTemplates()
    {
        var resources = new ResourceDictionary();

        string[] templatePaths =
        [
            "Views/Templates/TurnRowTemplate.xaml",
            "Views/Templates/ToolRowTemplate.xaml",
            "Views/Templates/TextRowTemplate.xaml",
            "Views/Templates/ThinkingRowTemplate.xaml",
            "Views/Templates/HookRowTemplate.xaml",
            "Views/Templates/StartupPhaseRowTemplate.xaml",
            "Views/Templates/PermissionRowTemplate.xaml",
            "Views/Templates/ErrorRowTemplate.xaml"
        ];

        var assemblyName = typeof(ConversationViewFactory).Assembly.GetName().Name;
        foreach (var path in templatePaths)
        {
            var uri = new Uri($"pack://application:,,,/{assemblyName};component/{path}");
            var dict = new ResourceDictionary { Source = uri };
            resources.MergedDictionaries.Add(dict);
        }

        return resources;
    }

    public static FrameworkElement CreateMainContent(ConversationViewModel viewModel, IBus bus)
    {
        var itemsControl = new ItemsControl
        {
            Margin = new Thickness(0, 10, 0, 18)
        };
        itemsControl.SetBinding(ItemsControl.ItemsSourceProperty, "Turns");
        itemsControl.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = itemsControl,

            // The chat history is read-only output, not an interactive control: keep it out of keyboard
            // focus traversal so Tab and the focus ring only ever land on real inputs. Mouse-wheel scrolling
            // still works without focus.
            Focusable = false,

            // The prompt input + status bar float over the bottom edge of the chat (translucent). Reserve
            // their resting height at the bottom of the scroll extent so the newest message, when scrolled
            // to the bottom, sits just above the input instead of behind it.
            Padding = new Thickness(0, 0, 0, 70)
        };
        scrollViewer.SetResourceReference(Control.BackgroundProperty, "BlackBrush");
        scrollViewer.DataContext = viewModel;

        // Stay pinned to the newest content while the user is at the bottom (or has never scrolled away).
        // A manual scroll up stops the auto-follow; scrolling back to the bottom re-arms it.
        var stickToBottom = true;
        scrollViewer.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange == 0)
            {
                stickToBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1.0;
            }
            else if (stickToBottom)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
            }
        };

        WireScrollCommands(scrollViewer, bus);
        return scrollViewer;
    }

    // Scroll the chat on the conversation's panel-local scroll commands (Ctrl+Up/Down by default; the host
    // routes them here even while the prompt input holds focus). Subscribed across the view's loaded
    // lifetime so it survives reparenting and stops when torn down. A manual scroll up naturally disarms the
    // auto-follow via the ScrollChanged handler above.
    private static void WireScrollCommands(ScrollViewer scrollViewer, IBus bus)
    {
        ISubscription? subscription = null;

        void Scroll(double delta) =>
            scrollViewer.ScrollToVerticalOffset(
                Math.Clamp(scrollViewer.VerticalOffset + delta, 0, scrollViewer.ScrollableHeight));

        void Dispatch(string command)
        {
            switch (command)
            {
                case "conversation.scroll.up": Scroll(-ScrollStep); break;
                case "conversation.scroll.down": Scroll(ScrollStep); break;
            }
        }

        scrollViewer.Loaded += (_, _) => subscription ??= bus.Subscribe<RunPanelCommand>(message =>
        {
            if (message.Command.StartsWith("conversation.scroll", StringComparison.Ordinal))
            {
                Application.Current?.Dispatcher.InvokeAsync(() => Dispatch(message.Command));
            }

            return Task.CompletedTask;
        });

        scrollViewer.Unloaded += (_, _) =>
        {
            subscription?.Dispose();
            subscription = null;
        };
    }

}
