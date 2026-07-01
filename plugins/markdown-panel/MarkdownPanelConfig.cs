namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// The kernel-supplied seed config for the plugin (IPlugin&lt;TConfig&gt; requires one). The plugin has no
/// tunable settings - the durable panel definitions are their own config section, loaded through the
/// Configuration round-trip rather than this seed - so this is intentionally empty.
public sealed record MarkdownPanelConfig;
