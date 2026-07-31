using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

/// One `ConversationViewModel` per chat, and the decision about which of them a state change actually has to
/// re-project.
///
/// A background chat's panel is alive but off-screen, so its projection is still needed - its scroll position
/// has to be right the moment you switch to it. Re-projecting every chat on every tick would be wasteful
/// though (the turn list is not virtualized), so a change is diffed by **reference**: the pure update rebuilds
/// only the `Chat` records it touched, leaving the rest reference-identical, and only those get projected.
///
/// The dictionary is an instance field, never a static registry, so the plugin's load context can unload.
public sealed class ChatViewModels
{
    private readonly Dictionary<Guid, ConversationViewModel> _byChatId = [];

    /// Panels whose chat does not exist yet, one per workspace. Keyed by workspace rather than pooled under a
    /// single "unbound" slot: switching to a workspace materialises its panel immediately but its session is
    /// obtained asynchronously, so several panels can be waiting at once - and a shared slot would hand every
    /// one of them the same view model, which is precisely the "every workspace shows the same chat" fault.
    private readonly Dictionary<Guid, ConversationViewModel> _unboundByWorkspace = [];

    private readonly Action<string, string> _publishPermission;

    // Prompt availability and the session's permission mode are still application-wide facts (one session
    // catalog, one bridge) but they are rendered per chat panel, so they are remembered here and applied to
    // every view model - including one created after the fact, which would otherwise miss them.
    private bool _promptAvailable;
    private string _promptMode = "";
    private string _promptModeDisplayName = "";

    public ChatViewModels(Action<string, string> publishPermission) =>
        _publishPermission = publishPermission;

    /// The view model for a chat, created on first ask. Called by the chat panel's view factory, so a panel
    /// that opens later still lands on the same view model as the one already projecting that chat.
    ///
    /// `workspaceId` is only consulted when there is no chat to bind to yet - it decides which workspace the
    /// resulting placeholder belongs to, so the chat that eventually appears for that workspace adopts this
    /// panel and no other.
    public ConversationViewModel ForChat(Chat? chat, Guid chatId, Guid workspaceId)
    {
        if (chat is not null && _byChatId.TryGetValue(chat.ChatId, out var bound))
        {
            return bound;
        }

        if (chat is null)
        {
            if (_unboundByWorkspace.TryGetValue(workspaceId, out var waiting))
            {
                return waiting;
            }

            var placeholder = New(null);
            _unboundByWorkspace[workspaceId] = placeholder;
            return placeholder;
        }

        var created = New(chat);
        _byChatId[chatId] = created;
        return created;
    }

    private ConversationViewModel New(Chat? chat)
    {
        var created = new ConversationViewModel(chat, _publishPermission) { IsPromptAvailable = _promptAvailable };
        created.SetPromptMode(_promptMode, _promptModeDisplayName);
        return created;
    }

    public void SetPromptAvailable(bool available)
    {
        _promptAvailable = available;
        foreach (var viewModel in _byChatId.Values)
        {
            viewModel.IsPromptAvailable = available;
        }
    }

    public void SetPromptMode(string mode, string displayName)
    {
        _promptMode = mode;
        _promptModeDisplayName = displayName;
        foreach (var viewModel in _byChatId.Values)
        {
            viewModel.SetPromptMode(mode, displayName);
        }
    }

    /// Re-project every chat regardless of what changed. Used when something outside the state moves - a
    /// status-line template edit, which every turn's stats column renders from.
    public void RefreshAll(ConversationState current)
    {
        foreach (var chat in current.Chats)
        {
            if (_byChatId.TryGetValue(chat.ChatId, out var viewModel))
            {
                viewModel.Update(chat);
            }
        }
    }

    /// Push the chats that actually changed onto their view models. A chat whose record is reference-identical
    /// to the previous state's is skipped; a chat with no view model yet (no panel has opened it) is skipped
    /// too, since `ForChat` will seed it from the live state when one does.
    public void Project(ConversationState previous, ConversationState current)
    {
        AdoptUnbound(current);

        foreach (var chat in current.Chats)
        {
            if (!_byChatId.TryGetValue(chat.ChatId, out var viewModel))
            {
                continue;
            }

            if (Unchanged(previous, chat))
            {
                continue;
            }

            viewModel.Update(chat);
        }
    }

    /// Forget the view models of chats that no longer exist, so a closed chat's projection is not held alive.
    public void DropClosed(ConversationState current)
    {
        var live = current.Chats.Select(chat => chat.ChatId).ToHashSet();
        foreach (var chatId in _byChatId.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _byChatId.Remove(chatId);
        }
    }

    // A chat panel can resolve before its chat exists: a workspace's session is obtained asynchronously, and a
    // restored panel is materialised on its own schedule. Such a panel waits behind a placeholder that no chat
    // matches, so the chat that does appear has to adopt it - otherwise the panel stays blank for ever behind a
    // view model nothing projects onto.
    private void AdoptUnbound(ConversationState current)
    {
        foreach (var chat in current.Chats)
        {
            if (_byChatId.ContainsKey(chat.ChatId))
            {
                // That chat already has its own view model, so any placeholder for it is simply stale.
                _unboundByWorkspace.Remove(chat.WorkspaceId);
                continue;
            }

            if (_unboundByWorkspace.Remove(chat.WorkspaceId, out var waiting))
            {
                _byChatId[chat.ChatId] = waiting;
            }
        }

        AdoptWorkspaceless(current);
    }

    // A panel created before workspaces existed carries no workspace of its own, so nothing will ever match it
    // by workspace. The visible chat adopts it, which is what happened before workspaces were a thing.
    private void AdoptWorkspaceless(ConversationState current)
    {
        if (!_unboundByWorkspace.TryGetValue(Guid.Empty, out var legacy))
        {
            return;
        }

        if ((current.VisibleChat ?? current.Chats.FirstOrDefault()) is not { } adopting)
        {
            return;
        }

        _unboundByWorkspace.Remove(Guid.Empty);
        if (!_byChatId.ContainsKey(adopting.ChatId))
        {
            _byChatId[adopting.ChatId] = legacy;
        }
    }

    private static bool Unchanged(ConversationState previous, Chat chat) =>
        previous.Chats.Any(before => ReferenceEquals(before, chat));
}
