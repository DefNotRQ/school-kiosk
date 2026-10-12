using Android.App.Admin;
using Android.Content;

namespace SchoolKiosk;

[BroadcastReceiver(Name = "com.ahura.schoolkiosk.KioskAdminReceiver", Permission = "android.permission.BIND_DEVICE_ADMIN", Exported = true)]
[IntentFilter(new[] { "android.app.action.DEVICE_ADMIN_ENABLED" })]
[MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
public sealed class KioskAdminReceiver : DeviceAdminReceiver
{
}
