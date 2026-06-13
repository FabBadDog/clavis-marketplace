using System;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

// An assistant text block rendered as markdown, interleaved with the tool/hook rows in arrival order.
public sealed class TextItemViewModel : ObservableObject
{
    public TextItemViewModel(Guid textId, string markdown)
    {
        TextId = textId;
        Markdown = markdown;
    }

    public Guid TextId { get; }

    public string Markdown { get; private set; }

    public void Update(string markdown)
    {
        if (Markdown == markdown)
        {
            return;
        }

        Markdown = markdown;
        OnPropertyChanged(nameof(Markdown));
    }
}
