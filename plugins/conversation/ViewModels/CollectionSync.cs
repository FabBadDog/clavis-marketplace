using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public static class CollectionSync
{
    public static void Reconcile<TSource, TTarget>(
        ObservableCollection<TTarget> targets,
        IReadOnlyList<TSource>        sources,
        Func<TSource, string>         sourceKey,
        Func<TTarget, string>         targetKey,
        Func<TSource, TTarget>        create,
        Action<TTarget, TSource>      update)
    {
        // Forward pass: make targets[i] match sources[i]. Search for an existing match only at
        // indices strictly greater than i, so targets[0..i-1] stay the already-aligned prefix and a
        // Move target index is always valid. Searching from 0 instead would, when two sources share
        // a key, match an element already placed in the prefix and move it past the end of the list.
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var key = sourceKey(source);

            if (i < targets.Count && targetKey(targets[i]) == key)
            {
                update(targets[i], source);
                continue;
            }

            var existingIndex = -1;
            for (var j = i + 1; j < targets.Count; j++)
            {
                if (targetKey(targets[j]) != key)
                {
                    continue;
                }

                existingIndex = j;
                break;
            }

            if (existingIndex >= 0)
            {
                update(targets[existingIndex], source);
                targets.Move(existingIndex, i);
            }
            else
            {
                targets.Insert(i, create(source));
            }
        }

        // Anything still beyond the sources is stale (removed or bubbled to the end); trim it.
        while (targets.Count > sources.Count)
        {
            targets.RemoveAt(targets.Count - 1);
        }
    }

    public static string GetItemKey(TurnItem item) => item switch
    {
        PhaseItem pi => $"phase-{pi.Phase.DisplayName}",
        HookItem hi => $"hook-{hi.Hook.HookId}",
        ToolItem ti => $"tool-{ti.Tool.ToolUseId}",
        PermissionItem pi => $"permission-{pi.Permission.PermissionId}",
        ErrorItem ei => $"error-{ei.ErrorId}",
        TextItem ti => $"text-{ti.TextId}",
        ThinkingItem ti => $"thinking-{ti.ThinkingId}",
        _ => item.GetHashCode().ToString()
    };

    public static INotifyPropertyChanged CreateItemViewModel(TurnItem item, Action<string, string> publishPermission)
        => item switch
        {
            PhaseItem pi => new StartupPhaseViewModel(pi.Phase),
            HookItem hi => new HookItemViewModel(hi.Hook),
            ToolItem ti => new ToolItemViewModel(ti.Tool),
            PermissionItem pi => new PermissionViewModel(pi.Permission, publishPermission),
            ErrorItem ei => new ErrorItemViewModel(ei.ErrorId, ei.Message),
            TextItem ti => new TextItemViewModel(ti.TextId, ti.Markdown),
            ThinkingItem ti => new ThinkingItemViewModel(ti.ThinkingId, ti.Text),
            _ => throw new InvalidOperationException($"Unknown turn item type: {item.GetType().Name}")
        };

    public static void UpdateItemViewModel(INotifyPropertyChanged viewModel, TurnItem item)
    {
        switch (viewModel, item)
        {
            case (StartupPhaseViewModel vm, PhaseItem pi): vm.Update(pi.Phase); break;
            case (HookItemViewModel vm, HookItem hi): vm.Update(hi.Hook); break;
            case (ToolItemViewModel vm, ToolItem ti): vm.Update(ti.Tool); break;
            case (PermissionViewModel vm, PermissionItem pi): vm.Update(pi.Permission); break;
            case (TextItemViewModel vm, TextItem ti): vm.Update(ti.Markdown); break;
            case (ThinkingItemViewModel vm, ThinkingItem ti): vm.Update(ti.Text); break;
        }
    }

    public static string GetItemViewModelKey(INotifyPropertyChanged viewModel) => viewModel switch
    {
        StartupPhaseViewModel vm => $"phase-{vm.DisplayName}",
        HookItemViewModel vm => $"hook-{vm.HookId}",
        ToolItemViewModel vm => $"tool-{vm.ToolUseId}",
        PermissionViewModel vm => $"permission-{vm.PermissionId}",
        ErrorItemViewModel vm => $"error-{vm.ErrorId}",
        TextItemViewModel vm => $"text-{vm.TextId}",
        ThinkingItemViewModel vm => $"thinking-{vm.ThinkingId}",
        _ => viewModel.GetHashCode().ToString()
    };
}
