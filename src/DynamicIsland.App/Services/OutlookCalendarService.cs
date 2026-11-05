using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using DynamicIsland.App.ViewModels;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;

namespace DynamicIsland.App.Services;

/// <summary>
/// Reads today's Outlook calendar from either:
/// 1. A published Outlook web calendar ICS URL (Microsoft 365 web), or
/// 2. The desktop Outlook COM automation model when no URL is configured.
/// The UI thread never performs either network or Office COM work.
/// </summary>
public sealed class OutlookCalendarService
{
    private const int OlFolderCalendar = 9;
    private const int MaxIcsBytes = 8 * 1024 * 1024;

    private static readonly HttpClient CalendarHttpClient = CreateHttpClient();

    public Task<CalendarLoadResult> LoadTodayAsync(string? webCalendarUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(webCalendarUrl))
        {
            return LoadWebTodayAsync(webCalendarUrl.Trim());
        }

        return LoadDesktopTodayAsync();
    }

    private static Task<CalendarLoadResult> LoadDesktopTodayAsync()
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
                "未配置日历来源",
                "没有检测到桌面版 Outlook。若使用 Microsoft 365 网页版，请在设置中粘贴 Outlook 发布的 ICS 日历链接。");
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

    private static async Task<CalendarLoadResult> LoadWebTodayAsync(string source)
    {
        try
        {
            var ics = await ReadCalendarTextAsync(source).ConfigureAwait(false);
            if (!ics.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("日历链接没有返回 iCalendar 内容。");
            }

            return ParseIcs(ics);
        }
        catch (Exception ex)
        {
            return WebFailure(ex);
        }
    }

    private static async Task<string> ReadCalendarTextAsync(string source)
    {
        if (File.Exists(source))
        {
            return await File.ReadAllTextAsync(source).ConfigureAwait(false);
        }

        if (source.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            source = "https://" + source["webcal://".Length..];
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("请填写 http(s)、webcal 或本地 .ics 文件路径。");
        }

        using var response = await CalendarHttpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"日历服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
        }

        if (response.Content.Headers.ContentLength > MaxIcsBytes)
        {
            throw new InvalidDataException("日历 ICS 文件超过 8 MB 限制。");
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (body.Length > MaxIcsBytes)
        {
            throw new InvalidDataException("日历 ICS 文件超过 8 MB 限制。");
        }

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("链接返回的是网页而不是 ICS，可能已过期或需要重新发布。");
        }

        return body;
    }

    private static CalendarLoadResult ParseIcs(string ics)
    {
        var calendar = IcalCalendar.Load(ics);
        var dayStart = DateTime.Today;
        var dayEnd = dayStart.AddDays(1);
        var events = new List<CalendarEventItem>();

        foreach (var occurrence in calendar.GetOccurrences(
                     new CalDateTime(dayStart),
                     new CalDateTime(dayEnd)))
        {
            if (events.Count >= 200)
            {
                break;
            }

            if (occurrence.Source is not CalendarEvent source)
            {
                continue;
            }

            if (string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = occurrence.Period.StartTime.AsSystemLocal;
            var end = occurrence.Period.EndTime.AsSystemLocal;
            if (start >= dayEnd || end <= dayStart)
            {
                continue;
            }

            var subject = source.Summary;
            if (string.IsNullOrWhiteSpace(subject))
            {
                subject = FirstMeaningfulLine(source.Description);
            }

            events.Add(new CalendarEventItem(
                subject ?? string.Empty,
                start,
                end,
                source.IsAllDay,
                source.Location));
        }

        events.Sort((left, right) => left.Start.CompareTo(right.Start));
        return CalendarLoadResult.Success(events);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/0.1");
        return client;
    }

    private static CalendarLoadResult WebFailure(Exception exception)
    {
        var message = exception switch
        {
            HttpRequestException => "无法下载日历。请确认 ICS 链接仍然有效、未过期。",
            TaskCanceledException => "下载日历超时，请检查网络或稍后重试。",
            InvalidDataException => exception.Message,
            ArgumentException => "日历链接格式不正确。",
            _ => "读取网页日历时发生异常。"
        };

        var detail = exception.Message?.Trim();
        if (!string.IsNullOrWhiteSpace(detail) && detail != message)
        {
            if (detail.Length > 220)
            {
                detail = detail[..220] + "…";
            }

            message = $"{message}\n{detail}";
        }

        return CalendarLoadResult.Failure("无法读取网页日历", message);
    }

    private static string? FirstMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return line.Length > 120 ? line[..120] + "…" : line;
    }

    private static CalendarLoadResult Failure(Exception exception)
    {
        var message = exception is COMException
            ? "请确认 Outlook 已安装并登录，且 Office 位数与本应用一致（x64）。"
            : "读取 Calendar 时发生异常。";

        var detail = exception.Message?.Trim();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            if (detail.Length > 220)
            {
                detail = detail[..220] + "…";
            }

            message = $"{message}\n{detail}";
        }

        return CalendarLoadResult.Failure("无法读取 Calendar", message);
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
