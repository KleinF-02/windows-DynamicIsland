namespace DynamicIsland.App.ViewModels;

public sealed class TodoItemViewModel : ObservableObject
{
    private string _text;
    private bool _isCompleting;

    public TodoItemViewModel(string id, string text, DateTime createdAt)
    {
        Id = id;
        _text = text;
        CreatedAt = createdAt;
    }

    public string Id { get; }

    public DateTime CreatedAt { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    /// <summary>Set when the checkbox is checked, before the particle animation removes the row.</summary>
    public bool IsCompleting
    {
        get => _isCompleting;
        set => SetProperty(ref _isCompleting, value);
    }
}
