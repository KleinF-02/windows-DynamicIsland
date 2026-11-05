using System.Windows;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DynamicIsland.App.ViewModels;

public sealed class AgentMessageViewModel
{
    public AgentMessageViewModel(string text, bool isUser)
    {
        Text = text.Trim();
        IsUser = isUser;
        CreatedAt = DateTime.Now;
    }

    public string Text { get; }

    public bool IsUser { get; }

    public DateTime CreatedAt { get; }

    public string TimeText => CreatedAt.ToString("HH:mm");

    public HorizontalAlignment Alignment => IsUser
        ? HorizontalAlignment.Right
        : HorizontalAlignment.Left;
}
