using Android.App;
using Android.Content;
using Android.App;

namespace SchoolKiosk;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged })]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        // Credentials remain unavailable until the tablet is unlocked.
        var users = (Android.OS.UserManager?)context.GetSystemService(Context.UserService);
        if (users?.IsUserUnlocked != true) return;
        ScheduleMonitorService.EnsureRunning(context);
        ScheduleReceiver.ScheduleNext(context);
        var config = new KioskConfig(context);
        if (!config.SetupComplete || !config.IsKioskTime(DateTime.Now)) return;
        if (((KeyguardManager?)context.GetSystemService(Context.KeyguardService))?.IsKeyguardLocked == true) return;
        var launch = new Intent(context, typeof(MainActivity));
        launch.SetAction("com.ahura.schoolkiosk.BOOT_DURING_SCHEDULE");
        launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        try { context.StartActivity(launch); } catch { }
    }
}
