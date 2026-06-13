using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace FabioSoft.Nucleus.Plugins.Selection;

/// The XAML row templates of the selection popups, loaded once from Views/SelectionTemplates.xaml.
[ExcludeFromCodeCoverage(Justification = "WPF resource loading only.")]
internal sealed class SelectionTemplates
{
    private readonly ResourceDictionary _dictionary;

    public SelectionTemplates()
    {
        var assemblyName = typeof(SelectionTemplates).Assembly.GetName().Name;
        _dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assemblyName};component/Views/SelectionTemplates.xaml")
        };
    }

    public DataTemplate Model => (DataTemplate)_dictionary["ModelItemTemplate"];

    public DataTemplate Effort => (DataTemplate)_dictionary["EffortItemTemplate"];

    public DataTemplate Mode => (DataTemplate)_dictionary["ModeItemTemplate"];

    public DataTemplate Panel => (DataTemplate)_dictionary["PanelItemTemplate"];

    public DataTemplate Option => (DataTemplate)_dictionary["OptionItemTemplate"];
}
