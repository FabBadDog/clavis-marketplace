namespace FabioSoft.Nucleus.Plugins.KeyMap;

/// Source of truth for key bindings. Owns the YAML config (load/seed/persist), detects same-scope
/// duplicate gestures (warning, not blocking), and broadcasts the current binding set as KeymapChanged.
/// It does NOT capture keys or resolve gestures at runtime - the WpfHost holds the broadcast snapshot
/// and resolves synchronously so it can swallow the key event. This plugin is the impure shell; the
/// gesture/file/binding logic is pure (KeyGesture, KeymapFile, KeymapBindings).
public sealed class KeyMapPlugin : IPlugin<KeyMapConfig>
{
    public string Id => "KeyMap";

    public KeyMapConfig DefaultConfig => new();

    private volatile IReadOnlyList<KeyBinding> _bindings = KeymapBindings.Defaults;

    // Defaults declared by other plugins for their own commands, and the persisted set as last read. Both are
    // needed to re-merge: a declaration can arrive after the config has already been loaded (plugins activate in
    // the background), so the merge has to be repeatable rather than a one-shot at load time.
    private readonly Dictionary<string, IReadOnlyList<KeyBinding>> _declared = [];
    private readonly object _declaredGate = new();
    private volatile IReadOnlyList<KeyBinding> _persisted = [];

    public Task<ConfigValidationResult> ValidateConfigAsync(KeyMapConfig config) =>
        Task.FromResult<ConfigValidationResult>(
            KeyGesture.TryNormalize(config.SummonGesture) is not null
                ? new ConfigValid()
                : new ConfigInvalid([$"SummonGesture '{config.SummonGesture}' is not a valid gesture"]));

    public Task<IDisposable> ActivateAsync(IBus bus, KeyMapConfig config)
    {
        var configSubscription = bus.Subscribe<ConfigResult>(result =>
        {
            if (result is ConfigFound found && found.PluginId == Id)
            {
                LoadAndBroadcast(bus, found.RawConfig);
            }
            else if (result is ConfigNotFound notFound && notFound.PluginId == Id)
            {
                _persisted = [];
                _bindings = KeymapBindings.Merge(_persisted, DeclaredBindings());
                bus.Send(new SaveConfig(Id, KeymapFile.SerializeStarter()));
                Broadcast(bus);
            }

            return Task.CompletedTask;
        });

        var changedSubscription = bus.Subscribe<ConfigChanged>(changed =>
        {
            if (changed.PluginId == Id)
            {
                LoadAndBroadcast(bus, changed.RawConfig);
            }

            return Task.CompletedTask;
        });

        var requestSubscription = bus.Subscribe<RequestKeymap>(_ =>
        {
            Broadcast(bus);
            return Task.CompletedTask;
        });

        // A plugin declares the gestures for its own commands, so a shortcut ships with the feature that owns
        // it. Re-merged and re-broadcast on arrival, since plugins activate in the background long after the
        // keymap loaded its config.
        var declaredSubscription = bus.Subscribe<DefaultBindingsDeclared>(message =>
        {
            lock (_declaredGate)
            {
                _declared[message.PluginId] = message.Bindings;
            }

            ReMerge(bus);
            return Task.CompletedTask;
        });

        var setSubscription = bus.Subscribe<SetKeyBinding>(message =>
        {
            Persist(bus, KeymapBindings.Set(_bindings, message.Command, message.Scope, message.PanelKind, message.Gesture));
            return Task.CompletedTask;
        });

        var removeSubscription = bus.Subscribe<RemoveKeyBinding>(message =>
        {
            Persist(bus, KeymapBindings.Remove(_bindings, message.Gesture, message.Scope, message.PanelKind));
            return Task.CompletedTask;
        });

        // Ask plugins to declare their own default gestures, so activation order does not matter.
        bus.Send(new RequestDefaultBindings());
        bus.Send(new GetConfig(Id));
        bus.LogInfo(Id, "Key map plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(
            configSubscription, changedSubscription, requestSubscription, declaredSubscription,
            setSubscription, removeSubscription));
    }

    private void LoadAndBroadcast(IBus bus, string rawConfig)
    {
        try
        {
            var parsed = KeymapFile.Parse(rawConfig);
            _bindings = parsed.Count > 0 ? KeymapBindings.Merge(parsed) : KeymapBindings.Defaults;
        }
        catch (Exception exception)
        {
            bus.LogWarn(Id, $"Failed to parse keymap config, keeping current bindings: {exception.Message}");
        }

        Broadcast(bus);
    }

    // Re-run the merge over the current persisted set and every declaration seen so far.
    private void ReMerge(IBus bus)
    {
        _bindings = KeymapBindings.Merge(_persisted, DeclaredBindings());
        WarnOnConflicts(bus, _bindings);
        Broadcast(bus);
    }

    private IReadOnlyList<KeyBinding> DeclaredBindings()
    {
        lock (_declaredGate)
        {
            // Ordered by plugin id so "the first declaration wins" is deterministic rather than depending on
            // which plugin happened to finish compiling first.
            return [.. _declared.OrderBy(entry => entry.Key, StringComparer.Ordinal).SelectMany(entry => entry.Value)];
        }
    }

    private void Persist(IBus bus, IReadOnlyList<KeyBinding> bindings)
    {
        // Update in memory so a rapid follow-up edit sees the fresh state; the SaveConfig echo
        // (ConfigChanged) reloads and broadcasts, keeping one broadcast path.
        _bindings = bindings;
        WarnOnConflicts(bus, bindings);
        bus.Send(new SaveConfig(Id, KeymapFile.Serialize(bindings)));
    }

    private void WarnOnConflicts(IBus bus, IReadOnlyList<KeyBinding> bindings)
    {
        foreach (var gesture in KeymapBindings.Conflicts(bindings))
        {
            bus.LogWarn(Id, $"Gesture '{gesture}' is bound to multiple commands in the same scope; the first match wins");
        }
    }

    private void Broadcast(IBus bus) => bus.Send(new KeymapChanged(_bindings));

    private sealed class PluginDisposable(params ISubscription[] subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }
        }
    }
}
