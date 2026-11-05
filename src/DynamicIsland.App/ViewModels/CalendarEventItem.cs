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
        StartTimeText = isAllDay ? "全天" : start.ToString("HH:mm");
        EndTimeText = isAllDay ? string.Empty : end.ToString("HH:mm");
        HasEndTime = !isAllDay;
    }

    public string Subject { get; }

    public DateTime Start { get; }

    public bool IsAllDay { get; }

    public string TimeText { get; }

    public string StartTimeText { get; }

    public string EndTimeText { get; }

    public bool HasEndTime { get; }

    public string LocationText { get; }

    public bool HasLocation => !string.IsNullOrWhiteSpace(LocationText);
}
