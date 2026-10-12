using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;

namespace SchoolKiosk;

// User-visible schedule watchdog. No network polling, wake lock, or tracking.
[Service(Name = "com.ahura.schoolkiosk.ScheduleMonitorService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public sealed class ScheduleMonitorService : Service
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private bool wasActive;
    private ClockReceiver? clockReceiver;
    public static void EnsureRunning(Context context)
    {
        if (!new KioskConfig(context).SetupComplete) return;
        try { context.StartForegroundService(new Intent(context, typeof(ScheduleMonitorService))); }
        catch (Exception ex) { Log.Warn("SchoolKiosk", "Schedule service start denied: " + ex.GetType().Name); }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("schedule", "School schedule", NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 90, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var notification = new Notification.Builder(this, "schedule")
            .SetSmallIcon(Resource.Drawable.app_icon)
            .SetContentTitle("School schedule is monitoring")
            .SetContentText("Weekdays 08:30–15:30 · checks every minute while awake")
            .SetContentIntent(open).SetOngoing(true).Build();
        StartForeground(90, notification);
        clockReceiver = new ClockReceiver(this);
        var filter = new IntentFilter(Intent.ActionTimeChanged);
        filter.AddAction(Intent.ActionTimezoneChanged);
        filter.AddAction(Intent.ActionUserPresent);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            RegisterReceiver(clockReceiver, filter, ReceiverFlags.NotExported);
        else RegisterReceiver(clockReceiver, filter);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Tick();
        return StartCommandResult.Sticky;
    }

    private void Tick()
    {
        handler.RemoveCallbacksAndMessages(null);
        var config = new KioskConfig(this);
        var active = config.SetupComplete && config.IsKioskTime(DateTime.Now);
        var keyguard = (KeyguardManager?)GetSystemService(KeyguardService);
        if ((active || wasActive) && !MainActivity.IsVisible && keyguard?.IsKeyguardLocked != true)
        {
            var launch = new Intent(this, typeof(MainActivity));
            launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            try { StartActivity(launch); }
            catch (Exception ex) { Log.Warn("SchoolKiosk", "Schedule launch denied: " + ex.GetType().Name); }
        }
        wasActive = active;
        ScheduleReceiver.ScheduleNext(this);
        Log.Debug("SchoolKiosk", "Schedule checked: " + (active ? "school hours" : "unlocked hours"));
        handler.PostDelayed(Tick, 60_000 - DateTimeOffset.Now.ToUnixTimeMilliseconds() % 60_000);
    }

    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnDestroy()
    {
        handler.RemoveCallbacksAndMessages(null);
        if (clockReceiver is not null) UnregisterReceiver(clockReceiver);
        base.OnDestroy();
    }

    private sealed class ClockReceiver(ScheduleMonitorService owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => owner.Tick();
    }
}
