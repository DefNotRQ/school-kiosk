using Android.App;
using Android.Content;
using Android.OS;

namespace SchoolKiosk;

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ScheduleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        var cfg = new KioskConfig(context);
        ScheduleMonitorService.EnsureRunning(context);
        if (cfg.SetupComplete && ((KeyguardManager?)context.GetSystemService(Context.KeyguardService))?.IsKeyguardLocked != true)
        {
            var launch = new Intent(context, typeof(MainActivity));
            launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            try { context.StartActivity(launch); } catch { }
        }
        ScheduleNext(context);
    }

    public static void ScheduleNext(Context context)
    {
        var cfg = new KioskConfig(context);
        if (!cfg.SetupComplete) return;
        var now = DateTime.Now;
        var candidates = Enumerable.Range(0, 9)
            .SelectMany(offset =>
            {
                var day = now.Date.AddDays(offset);
                if (!cfg.IsDayEnabled(day.DayOfWeek)) return Array.Empty<(DateTime Time, bool IsStart)>();
                var start = day.AddMinutes(cfg.StartMinutes);
                var endDay = cfg.EndMinutes > cfg.StartMinutes ? day : day.AddDays(1);
                var end = endDay.AddMinutes(cfg.EndMinutes);
                return new[] { (Time: start, IsStart: true), (Time: end, IsStart: false) };
            })
            .Concat(cfg.TestLockUntilUtcMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                ? new[] { (Time: DateTimeOffset.FromUnixTimeMilliseconds(cfg.TestLockUntilUtcMs).LocalDateTime, IsStart: false) }
                : Array.Empty<(DateTime Time, bool IsStart)>())
            .Where(item => item.Time > now)
            .OrderBy(item => item.Time)
            .ToList();
        if (candidates.Count == 0) return;
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarm is null) return;

        var receiverIntent = new Intent(context, typeof(ScheduleReceiver));
        var receiverPending = PendingIntent.GetBroadcast(context, 40, receiverIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var activityIntent = new Intent(context, typeof(MainActivity));
        activityIntent.SetAction("com.ahura.schoolkiosk.SCHEDULE_START");
        activityIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        Bundle? launchOptions = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            // Android 15+ requires PendingIntent creators to explicitly opt in to
            // background activity starts. Without this, the exact alarm fires but
            // the OS silently blocks the kiosk from appearing.
            var options = ActivityOptions.MakeBasic();
            options!.SetPendingIntentCreatorBackgroundActivityStartMode(OperatingSystem.IsAndroidVersionAtLeast(36)
                ? BackgroundActivityStartMode.AllowAlways : BackgroundActivityStartMode.Allowed);
            launchOptions = options.ToBundle();
        }
        var activityPending = PendingIntent.GetActivity(context, 41, activityIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable, launchOptions);
        var next = candidates[0];
        var pending = next.IsStart ? activityPending : receiverPending;
        if (pending is null || alarm is null) return;
        if (next.IsStart && receiverPending is not null) alarm.Cancel(receiverPending);
        if (!next.IsStart && activityPending is not null) alarm.Cancel(activityPending);
        var trigger = new DateTimeOffset(next.Time).ToUnixTimeMilliseconds();
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarm.CanScheduleExactAlarms())
            alarm.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, pending);
        else
            alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, pending);
    }
}
