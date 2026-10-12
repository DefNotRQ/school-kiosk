using Android.App;
using Android.Content;
using Android.OS;

namespace SchoolKiosk;

// Android invokes the selected Home activity every time Home is pressed. Keeping
// this activity displayless prevents the browser from flashing before Honor Home
// is shown outside scheduled kiosk hours.
[Activity(Name = "com.ahura.schoolkiosk.HomeRouterActivity", Label = "School Kiosk Home", Exported = true,
    NoHistory = true, ExcludeFromRecents = true, Theme = "@android:style/Theme.NoDisplay")]
[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryHome, Intent.CategoryDefault })]
public sealed class HomeRouterActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Route();
        Finish();
    }

    private void Route()
    {
        var config = new KioskConfig(this);
        ScheduleReceiver.ScheduleNext(this);

        if (config.SetupComplete && config.IsKioskTime(DateTime.Now))
        {
            var kiosk = new Intent(this, typeof(MainActivity));
            kiosk.SetAction("com.ahura.schoolkiosk.HOME_DURING_SCHEDULE");
            kiosk.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            StartActivity(kiosk);
            return;
        }

        try
        {
            var honorHome = new Intent(Intent.ActionMain);
            honorHome.AddCategory(Intent.CategoryHome);
            honorHome.SetComponent(new ComponentName("com.hihonor.android.launcher", "com.hihonor.android.launcher.unihome.UniHomeLauncher"));
            honorHome.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            StartActivity(honorHome);
        }
        catch
        {
            // This build targets Honor Pad 10. If a launcher update renames its
            // component, leave the current app instead of recursively resolving Home.
            MoveTaskToBack(true);
        }
    }
}
