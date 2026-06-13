namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class ErrorItemViewModel : ObservableObject
{
    public ErrorItemViewModel(Guid errorId, string message)
    {
        ErrorId = errorId;
        Message = message;
    }

    public Guid ErrorId { get; }

    public string Message { get; }
}
