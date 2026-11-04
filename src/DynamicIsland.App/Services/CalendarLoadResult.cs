using DynamicIsland.App.ViewModels;

namespace DynamicIsland.App.Services;

public sealed record CalendarLoadResult(
    IReadOnlyList<CalendarEventItem> Events,
    bool OutlookAvailable,
    string? ErrorTitle,
    string? ErrorMessage)
{
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public static CalendarLoadResult Success(IReadOnlyList<CalendarEventItem> events)
        => new(events, OutlookAvailable: true, ErrorTitle: null, ErrorMessage: null);

    public static CalendarLoadResult Unavailable(string title, string message)
        => new([], OutlookAvailable: false, ErrorTitle: title, ErrorMessage: message);

    public static CalendarLoadResult Failure(string title, string message)
        => new([], OutlookAvailable: true, ErrorTitle: title, ErrorMessage: message);
}
