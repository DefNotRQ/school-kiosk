namespace SchoolKiosk;

// Local tablet time. Start is inclusive; end is exclusive.
internal static class SchoolSchedule
{
    public const int StartMinutes = 8 * 60 + 30;
    public const int EndMinutes = 15 * 60 + 30;
    public static bool IsWeekday(DayOfWeek day) => day >= DayOfWeek.Monday && day <= DayOfWeek.Friday;
    public static bool IsActive(DateTime now) => IsWeekday(now.DayOfWeek)
        && now.TimeOfDay >= TimeSpan.FromMinutes(StartMinutes)
        && now.TimeOfDay < TimeSpan.FromMinutes(EndMinutes);

    public static DateTime NextChange(DateTime now)
    {
        if (IsActive(now)) return now.Date.AddMinutes(EndMinutes);
        var next = now.Date.AddMinutes(StartMinutes);
        if (next <= now) next = next.AddDays(1);
        while (!IsWeekday(next.DayOfWeek)) next = next.AddDays(1);
        return next;
    }
}
