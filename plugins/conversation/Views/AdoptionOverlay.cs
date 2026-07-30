using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FabioSoft.Clavis.Controls;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

/// What a chat panel shows while its workspace is taking an agent over: the shared empty-state treatment saying
/// what is being waited for, plus the one gesture that ends the wait early.
///
/// It covers the chat rather than sitting beside it, because during a take-over there is no conversation yet -
/// the agent has not let go, so nothing has been resumed. An uncovered blank chat would read as broken.
[ExcludeFromCodeCoverage] // WPF composition; the decision of whether to show it is AdoptionNotices
public static class AdoptionOverlay
{
    public static FrameworkElement Create(IBus bus, Guid panelWorkspaceId)
    {
        var notice = EmptyState.create(AdoptionNotices.Headline, AdoptionNotices.Detail);
        notice.MaxWidth = 420;

        // Which workspace the visible notice belongs to. Closed over rather than held statically: a panel that
        // follows the active workspace cannot know it up front, and two chat panels would overwrite each other's
        // answer, sending the force to whichever workspace happened to update last.
        var noticeWorkspaceId = Guid.Empty;

        // Named for what it does to the agent, not for what it does to the dialog: it discards a running turn,
        // and the label is the last chance to convey that.
        var takeOver = ActionButton.create(
            "TAKE OVER NOW", new Action(() => bus.Send(new ForceTakeOver(noticeWorkspaceId))));
        takeOver.HorizontalAlignment = HorizontalAlignment.Center;
        takeOver.Margin = new Thickness(0, 18, 0, 0);
        notice.Children.Add(takeOver);

        // Opaque, not translucent: there is nothing underneath worth showing through, and a scrim over an empty
        // chat just looks like a rendering fault.
        var overlay = new Border { Child = notice, Visibility = Visibility.Collapsed };
        overlay.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");

        ISubscription? subscription = null;

        void Apply(WorkspaceListChanged message)
        {
            var current = AdoptionNotices.For(panelWorkspaceId, message.Workspaces, message.ActiveWorkspaceId);
            noticeWorkspaceId = current.IsVisible ? current.WorkspaceId : Guid.Empty;
            overlay.Visibility = current.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        overlay.Loaded += (_, _) =>
        {
            subscription ??= bus.Subscribe<WorkspaceListChanged>(message =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() => Apply(message));
                return Task.CompletedTask;
            });

            bus.Send(new RequestWorkspaces());
        };

        overlay.Unloaded += (_, _) =>
        {
            subscription?.Dispose();
            subscription = null;
        };

        return overlay;
    }
}
