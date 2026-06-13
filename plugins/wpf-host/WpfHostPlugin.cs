using System.Windows;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

public sealed class WpfHostPlugin : IPlugin<WpfHostConfig>
{
    public string Id => "WpfHost";

    public WpfHostConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(WpfHostConfig config)
    {
        var errors = new List<string>();

        if (config.UiScaleFactor is < 0.5 or > 4.0)
        {
            errors.Add("UiScaleFactor must be between 0.5 and 4.0");
        }

        if (config.DefaultWidth < config.MinWidth)
        {
            errors.Add("DefaultWidth must be >= MinWidth");
        }

        if (config.DefaultHeight < config.MinHeight)
        {
            errors.Add("DefaultHeight must be >= MinHeight");
        }

        return Task.FromResult<ConfigValidationResult>(
            errors.Count > 0 ? new ConfigInvalid(errors) : new ConfigValid());
    }

    public Task<IDisposable> ActivateAsync(IBus bus, WpfHostConfig config) =>

        // The Kernel activates plugins on a thread-pool thread, but WPF window creation must run on the
        // UI thread. Marshal the whole activation onto the dispatcher.
        Task.FromResult(Application.Current.Dispatcher.Invoke(() => Activate(bus, config)));

    private static IDisposable Activate(IBus bus, WpfHostConfig config)
    {
        var styles = ResourceLoader.Load<ResourceDictionary>("Theme/Styles.xaml");
        Application.Current.Resources.MergedDictionaries.Add(styles);

        return new WindowManager(bus, config);
    }
}
