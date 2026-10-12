using Android.Content;

namespace SchoolKiosk;

internal sealed class KioskConfig
{
    private readonly ISharedPreferences prefs;
    public KioskConfig(Context context) => prefs = context.GetSharedPreferences("kiosk", FileCreationMode.Private)!;

    public string Url { get => prefs.GetString("url", "https://example.org")!; set => Put("url", value); }
    public int StartMinutes => SchoolSchedule.StartMinutes;
    public int EndMinutes => SchoolSchedule.EndMinutes;
    public int ActiveDays => 0x3E;
    public string Pin { get => prefs.GetString("pin", "")!; set => Put("pin", value); }
    public bool SetupComplete { get => prefs.GetBoolean("setup", false); set => Put("setup", value); }
    public int Brightness { get => prefs.GetInt("brightness", 180); set => Put("brightness", Math.Clamp(value, 10, 255)); }
    public bool AdaptiveBrightness { get => prefs.GetBoolean("adaptiveBrightness", true); set => Put("adaptiveBrightness", value); }
    public int IdleResetMinutes { get => prefs.GetInt("idleResetMinutes", 10); set => Put("idleResetMinutes", Math.Clamp(value, 1, 60)); }
    public long TestLockUntilUtcMs { get => prefs.GetLong("testLockUntil", 0); set => prefs.Edit()!.PutLong("testLockUntil", value)!.Commit(); }
    public bool DashboardOnWake { get => prefs.GetBoolean("dashboardOnWake", false); set => prefs.Edit()!.PutBoolean("dashboardOnWake", value)!.Commit(); }
    public int FailedPinAttempts
    {
        get => prefs.GetInt("failedPinAttempts", 0);
        set => prefs.Edit()!.PutInt("failedPinAttempts", Math.Clamp(value, 0, 4))!.Commit();
    }
    public long PinLockedUntilUnixMs
    {
        get => prefs.GetLong("pinLockedUntil", 0);
        set => prefs.Edit()!.PutLong("pinLockedUntil", Math.Max(0, value))!.Commit();
    }

    public bool IsKioskTime(DateTime now)
    {
        return SchoolSchedule.IsActive(now) || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < TestLockUntilUtcMs;
    }

    public bool IsDayEnabled(DayOfWeek day) => (ActiveDays & (1 << (int)day)) != 0;

    public string ScheduleText => $"{SelectedDaysText()} {Format(StartMinutes)}–{Format(EndMinutes)}";

    public string SelectedDaysText()
    {
        if (ActiveDays == 0x7F) return "Every day";
        if (ActiveDays == 0) return "No days";
        var names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        return string.Join(", ", Enumerable.Range(0, 7).Where(i => (ActiveDays & (1 << i)) != 0).Select(i => names[i]));
    }

    public string NextChangeText(DateTime now)
    {
        var target = SchoolSchedule.NextChange(now);
        var remaining = target - now;
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }
    public static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
    private void Put(string key, string value) => prefs.Edit()!.PutString(key, value)!.Apply();
    private void Put(string key, int value) => prefs.Edit()!.PutInt(key, value)!.Apply();
    private void Put(string key, long value) => prefs.Edit()!.PutLong(key, value)!.Apply();
    private void Put(string key, bool value) => prefs.Edit()!.PutBoolean(key, value)!.Apply();
}
