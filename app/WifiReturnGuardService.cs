using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views.Accessibility;

namespace SchoolKiosk;

// Optional and user-enabled. Receives window metadata only; never reads event
// text, window contents, passwords, screenshots, or browsing history.
[Service(Name = "com.ahura.schoolkiosk.WifiReturnGuardService", Label = "School Kiosk Wi-Fi return guard",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/wifi_return_guard")]
public sealed class WifiReturnGuardService : AccessibilityService
{
    internal static bool Connected { get; private set; }
    private static WifiReturnGuardService? instance;
    private readonly Handler handler = new(Looper.MainLooper!);
    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        Connected = true;
        instance = this;
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (!WifiMaintenance.Pending || e?.EventType != EventTypes.WindowStateChanged) return;
        var package = e.PackageName?.ToString();
        if (string.IsNullOrEmpty(package)) return;
        if (package == "com.android.settings") { WifiMaintenance.EnteredSettings = true; return; }
        if (package == PackageName) return;
        // Honor briefly exposes its launcher while unpinning, before Settings
        // has opened. Arm the exit guard only once Settings is actually entered.
        if (!WifiMaintenance.EnteredSettings) return;
        // Let the current Android keyboard enter Wi-Fi passwords without reading it.
        var ime = Settings.Secure.GetString(ContentResolver, Settings.Secure.DefaultInputMethod);
        if (WifiReturnPolicy.ShouldReturn(package, PackageName!, ime?.Split('/')[0]))
        {
            Android.Util.Log.Debug("SchoolKiosk", "Wi-Fi guard returned from package: " + package);
            ReturnToKiosk();
        }
    }

    internal static void ArmTimeout()
    {
        if (instance is null) return;
        instance.handler.RemoveCallbacksAndMessages(null);
        instance.handler.PostDelayed(() =>
        {
            if (WifiMaintenance.Pending && !WifiMaintenance.EnteredSettings) instance?.ReturnToKiosk();
        }, 2_000);
        instance.handler.PostDelayed(() =>
        {
            if (WifiMaintenance.Pending && !WifiMaintenance.Active) instance?.ReturnToKiosk();
        }, 5 * 60_000 + 50);
    }

    internal void ReturnToKiosk()
    {
        WifiMaintenance.End();
        var cfg = new KioskConfig(this);
        if (!cfg.IsKioskTime(DateTime.Now)) return;
        if (((KeyguardManager?)GetSystemService(KeyguardService))?.IsKeyguardLocked == true) return;
        if (((PowerManager?)GetSystemService(PowerService))?.IsInteractive != true) return;
        var launch = new Intent(this, typeof(MainActivity));
        launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        try { StartActivity(launch); }
        catch (Exception ex) { Android.Util.Log.Warn("SchoolKiosk", "Wi-Fi return denied: " + ex.GetType().Name); }
    }

    public override void OnInterrupt() { if (WifiMaintenance.Pending) ReturnToKiosk(); }
    public override void OnDestroy()
    {
        if (WifiMaintenance.Pending) ReturnToKiosk();
        Connected = false;
        instance = null;
        handler.RemoveCallbacksAndMessages(null);
        base.OnDestroy();
    }
}
