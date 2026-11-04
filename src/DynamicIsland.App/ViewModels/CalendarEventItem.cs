namespace DynamicIsland.App.ViewModels;

public sealed class CalendarEventItem
{
    public CalendarEventItem(
        string subject,
        DateTime start,
        DateTime end,
        bool isAllDay,
        string? location)
    {
        Subject = string.IsNullOrWhiteSpace(subject) ? "（无主题）" : subject.Trim();
        Start = start;
        IsAllDay = isAllDay;
        LocationText = location?.Trim() ?? string.Empty;

        TimeText = isAllDay
            ? "全天"
            : $"{start:HH:mm} – {end:HH:mm}";
    }

    public string Subject { get; }

    public DateTime Start { get; }

    public bool IsAllDay { get; }

    public string TimeText { get; }

    public string LocationText { get; }

    public bool HasLocation => !string.IsNullOrWhiteSpace(LocationText);
}
