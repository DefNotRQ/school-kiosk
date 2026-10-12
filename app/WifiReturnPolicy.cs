namespace SchoolKiosk;

internal static class WifiReturnPolicy
{
    public static bool ShouldReturn(string? foregroundPackage, string ownPackage, string? keyboardPackage)
        => !string.IsNullOrEmpty(foregroundPackage)
            && foregroundPackage != ownPackage
            && foregroundPackage != "com.android.settings"
            // Android emits pin/unpin confirmation windows during the handoff.
            // Do not interpret these system dialogs as leaving Wi-Fi for Home.
            && foregroundPackage != "com.android.systemui"
            && foregroundPackage != keyboardPackage;
}
