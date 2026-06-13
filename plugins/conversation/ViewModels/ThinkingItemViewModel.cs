using System;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

// A reasoning (thinking) block, shown dimmed and collapsed by default with an expand toggle.
public sealed class ThinkingItemViewModel : ObservableObject
{
    private const int PreviewLength = 90;

    private bool _isExpanded;

    public ThinkingItemViewModel(Guid thinkingId, string text)
    {
        ThinkingId = thinkingId;
        Text = text;
    }

    public Guid ThinkingId { get; }

    public string Text { get; private set; }

    // A single-line teaser shown when collapsed: the first line, truncated.
    public string Preview
    {
        get
        {
            var firstLine = Text.Replace("\r", "").Split('\n', 2)[0].Trim();
            return firstLine.Length <= PreviewLength
                ? firstLine
                : firstLine[..PreviewLength].TrimEnd() + "...";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public void Update(string text)
    {
        if (Text == text)
        {
            return;
        }

        Text = text;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Preview));
    }
}
