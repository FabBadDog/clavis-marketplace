using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FabioSoft.Nucleus.Contracts;
using FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

/// One chat panel instance: the chat history with its own prompt input floating over the bottom edge. This
/// is the whole of the "chat" panel kind - the window host contributes nothing to it, which is what lets the
/// chat be docked, torn off, closed and reopened like any other panel.
///
/// The panel also owns the permission prompt's Left/Right/Enter keys while a decision is pending. They are
/// handled here, at the panel root (so they tunnel ahead of the prompt box's own Enter-to-submit), rather
/// than as keymap bindings: a binding on bare Enter for kind "chat" would fire unconditionally and there is
/// no keymap scope for "only while this instance is blocked", so submitting a prompt would break.
[ExcludeFromCodeCoverage] // WPF composition and keyboard routing; the state blob is ChatPanelState
public static class ChatPanelView
{
    public static FrameworkElement Create(IBus bus, ChatPanelBinding binding, PanelInstanceContext context)
    {
        // The plugin already resolved the saved blob against the live state (a restored panel re-attaches to
        // the chat it named, anything else lands on the visible chat). Persist the resolved identity back, so
        // a hand-opened panel gains a concrete chat id and returns to the same chat next launch.
        var viewModel = binding.ViewModel;
        context.OnStateChanged?.Invoke(binding.Identity.Serialize());

        var content = ConversationViewFactory.CreateMainContent(viewModel, bus);
        var prompt = new PromptInput(bus);

        var panel = new Grid();
        panel.Children.Add(content);
        panel.Children.Add(prompt.Row);
        prompt.CapHeightTo(panel);

        // Added last so it covers the chat and the prompt both: while a take-over is waiting there is no session
        // to send a prompt to, so accepting one would silently drop it.
        panel.Children.Add(AdoptionOverlay.Create(bus, binding.Identity.WorkspaceId));

        panel.PreviewKeyDown += (_, args) => HandlePermissionKeys(bus, viewModel, args);
        ApplyViewModel(viewModel, prompt);
        WirePromptFocusRequests(bus, panel, prompt);

        return panel;
    }

    // While a permission prompt is awaiting a decision it owns the bare Left/Right (move the choice) and
    // Enter (confirm) keys, even though the prompt box holds focus. Tunnelling at the panel root gets them
    // ahead of the box's Enter-to-submit; when nothing is pending every key falls through untouched.
    private static void HandlePermissionKeys(IBus bus, ConversationViewModel viewModel, KeyEventArgs args)
    {
        if (!viewModel.IsPermissionPending || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (args.Key)
        {
            case Key.Left:
                bus.Send(new UserNavigatedPermission(-1));
                break;
            case Key.Right:
                bus.Send(new UserNavigatedPermission(1));
                break;
            case Key.Enter:
                bus.Send(new UserConfirmedPermission());
                break;
            default:
                return;
        }

        args.Handled = true;
    }

    // Follow the two chat facts the prompt renders: whether prompts can be accepted yet (the row slides in)
    // and the session's permission mode (its ambient accent). Both setters are idempotent, so the view model's
    // refresh-everything notification is safe to act on directly.
    private static void ApplyViewModel(ConversationViewModel viewModel, PromptInput prompt)
    {
        void Apply()
        {
            prompt.SetAvailable(viewModel.IsPromptAvailable);
            prompt.SetMode(viewModel.PromptMode, viewModel.PromptModeDisplayName);
        }

        void OnChanged(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is null or "" or nameof(ConversationViewModel.IsPromptAvailable)
                or nameof(ConversationViewModel.PromptMode))
            {
                Apply();
            }
        }

        // Bound across the view's loaded lifetime so a torn-off or re-docked panel keeps following, and a
        // closed one stops holding the view model.
        prompt.Row.Loaded += (_, _) =>
        {
            viewModel.PropertyChanged -= OnChanged;
            viewModel.PropertyChanged += OnChanged;
            Apply();
        };
        prompt.Row.Unloaded += (_, _) => viewModel.PropertyChanged -= OnChanged;
    }

    // FocusInputRequested is keyboard-first navigation back to the prompt: the prompt lives here now, so this
    // panel answers it. Subscribed across the view's loaded lifetime, like the scroll commands.
    private static void WirePromptFocusRequests(IBus bus, FrameworkElement panel, PromptInput prompt)
    {
        ISubscription? subscription = null;

        panel.Loaded += (_, _) => subscription ??= bus.Subscribe<FocusInputRequested>(_ =>
        {
            Application.Current?.Dispatcher.InvokeAsync(prompt.Focus);
            return Task.CompletedTask;
        });

        panel.Unloaded += (_, _) =>
        {
            subscription?.Dispose();
            subscription = null;
        };
    }
}
