using Android.OS;

namespace SchoolKiosk;

internal static class WifiMaintenance
{
    private static long until;
    public static bool Pending => until != 0;
    public static bool Active => until > SystemClock.ElapsedRealtime();
    public static bool EnteredSettings { get; set; }
    public static void Begin()
    {
        until = SystemClock.ElapsedRealtime() + 5 * 60_000;
        EnteredSettings = false;
        WifiReturnGuardService.ArmTimeout();
    }
    public static void End() { until = 0; EnteredSettings = false; }
}
