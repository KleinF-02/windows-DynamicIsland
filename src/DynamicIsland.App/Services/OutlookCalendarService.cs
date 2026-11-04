using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using DynamicIsland.App.ViewModels;

namespace DynamicIsland.App.Services;

/// <summary>
/// Reads the signed-in user's default Outlook calendar through the Outlook COM
/// automation model. The call runs on a dedicated STA thread because Office COM
/// expects an STA apartment; the WPF UI thread never blocks on Outlook.
/// </summary>
public sealed class OutlookCalendarService
{
    private const int OlFolderCalendar = 9;

    public Task<CalendarLoadResult> LoadTodayAsync()
    {
        var completion = new TaskCompletionSource<CalendarLoadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var thread = new Thread(() =>
            {
                CalendarLoadResult result;
                try
                {
                    result = LoadTodayCore();
                }
                catch (Exception ex)
                {
                    result = Failure(ex);
                }

                completion.TrySetResult(result);
            })
            {
                IsBackground = true,
                Name = "OutlookCalendar"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch (Exception ex)
        {
            completion.TrySetResult(Failure(ex));
        }

        return completion.Task;
    }

    private static CalendarLoadResult LoadTodayCore()
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application");
        if (outlookType is null)
        {
            return CalendarLoadResult.Unavailable(
                "未检测到 Outlook",
                "此电脑没有安装或注册桌面版 Outlook。安装并登录 Outlook 后，点击刷新重试。");
        }

        object? application = null;
        object? mapi = null;
        object? calendarFolder = null;
        object? items = null;
        object? view = null;

        try
        {
            application = Activator.CreateInstance(outlookType)
                ?? throw new InvalidOperationException("无法创建 Outlook COM 实例。");

            dynamic outlook = application;
            mapi = outlook.GetNamespace("MAPI");
            dynamic session = mapi;
            calendarFolder = session.GetDefaultFolder(OlFolderCalendar);
            dynamic folder = calendarFolder;
            items = folder.Items;
            dynamic collection = items;

            try
            {
                collection.IncludeRecurrences = true;
            }
            catch (COMException)
            {
                // Some stores do not support recurrence expansion; continue without it.
            }

            try
            {
                collection.Sort("[Start]");
            }
            catch (COMException)
            {
                // Sorting is a convenience, not a requirement.
            }

            var dayStart = DateTime.Today;
            var dayEnd = dayStart.AddDays(1);

            // Date-only literals are the least ambiguous form for Outlook's
            // Restrict parser across system locales. Time-of-day filtering is
            // done again in managed code below.
            var filter =
                $"[Start] >= '{dayStart.ToString("M/d/yyyy", CultureInfo.InvariantCulture)}' AND " +
                $"[Start] < '{dayEnd.ToString("M/d/yyyy", CultureInfo.InvariantCulture)}'";

            try
            {
                view = collection.Restrict(filter);
            }
            catch (COMException)
            {
                // Fall back to a managed date filter over the collection.
                view = collection;
            }

            dynamic selected = view;
            var events = new List<CalendarEventItem>();
            object? current = null;

            try
            {
                current = selected.GetFirst();

                while (current is not null && events.Count < 200)
                {
                    object? next = null;

                    try
                    {
                        next = selected.GetNext();
                    }
                    catch (COMException)
                    {
                        // The collection may not support GetNext after a failure.
                    }

                    try
                    {
                        dynamic appointment = current;
                        var start = (DateTime)appointment.Start;
                        var end = (DateTime)appointment.End;
                        var allDay = Convert.ToBoolean(appointment.AllDayEvent, CultureInfo.InvariantCulture);

                        if (start < dayEnd && end > dayStart)
                        {
                            events.Add(new CalendarEventItem(
                                Convert.ToString(appointment.Subject, CultureInfo.CurrentCulture) ?? string.Empty,
                                start,
                                end,
                                allDay,
                                Convert.ToString(appointment.Location, CultureInfo.CurrentCulture)));
                        }
                    }
                    catch (COMException)
                    {
                        // Skip an appointment that cannot be read; keep the rest of the day.
                    }
                    finally
                    {
                        Release(current);
                        current = null;
                    }

                    current = next;
                }
            }
            finally
            {
                Release(current);
            }

            events.Sort((left, right) => left.Start.CompareTo(right.Start));
            return CalendarLoadResult.Success(events);
        }
        catch (Exception ex)
        {
            return Failure(ex);
        }
        finally
        {
            if (!ReferenceEquals(view, items))
            {
                Release(view);
            }

            Release(items);
            Release(calendarFolder);
            Release(mapi);
            Release(application);
        }
    }

    private static CalendarLoadResult Failure(Exception exception)
    {
        var message = exception is COMException
            ? "请确认 Outlook 已安装并登录，且 Office 位数与本应用一致（x64）。"
            : "读取 Outlook 日历时发生异常。";

        var detail = exception.Message?.Trim();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            if (detail.Length > 220)
            {
                detail = detail[..220] + "…";
            }

            message = $"{message}\n{detail}";
        }

        return CalendarLoadResult.Failure("无法读取 Outlook 日历", message);
    }

    private static void Release(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch (ArgumentException)
        {
            // Already released or not a valid RCW.
        }
    }
}
