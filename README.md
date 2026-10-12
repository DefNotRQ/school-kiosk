# School Kiosk

Open-source Android school browser, optimized for the Honor Pad 10. C# / .NET 10 for Android, ARM64, Android 8 or later. Source is MIT licensed. Download installable, signed APKs from [Releases](https://github.com/DefNotRQ/school-kiosk/releases).

## Schedule and controls

- Fixed **Monday–Friday, 08:30–15:30**, using the tablet's local time. Weekends are unlocked. Old saved custom hours are ignored.
- During school hours, tap the bottom-left gear **five times quickly**, then enter the administrator PIN. Outside school hours, one tap opens the PIN prompt.
- New installs require choosing a private PIN; there is no public default. Updating an existing installation preserves its PIN, URL and data.
- Five failed PIN attempts cause a one-minute lockout, persisted across app restarts.
- An authenticated administrator can start a ten-minute pinning test. It expires automatically unless normal school hours are active; it does not change the tablet clock.
- Bluetooth controls are removed. Wi-Fi opens Android's network/password settings. During a lock window this requires the optional **School Kiosk Wi-Fi return guard** to be enabled in Android Accessibility settings beforehand. Without it the app refuses to unpin. System-permission setup remains outside school hours.
- An active Wi-Fi maintenance session lasts at most five minutes. The guard lets Settings and the selected keyboard operate, and attempts to bring the kiosk back immediately when Home, Overview or another app appears. Android can briefly show the other app and require pinning confirmation again; this is a convenience guard, not a tamper-proof lockdown. Other Settings pages within the same Android Settings app are not distinguished.
- Screen-off/on reloads the saved dashboard URL in the first tab without closing the other live tabs. The inactivity timer does not clear tabs during sleep. A full process kill/restart is different: tab-session persistence across process death is not implemented.
- Up to 20 browser tabs, tab clearing, brightness controls, offline retry and WebView recovery.

## Background operation and limits

After setup, a **visible foreground-service notification** explains that schedule monitoring is enabled. The service checks the local schedule each minute while the CPU is awake. Exact boundary alarms supplement it during sleep. Boot/package-update receivers restart monitoring **after the first device unlock**; time/timezone changes reschedule alarms. No permanent wake lock is used.

Grant notifications, "Alarms & reminders" and "Display over other apps" in Android Settings outside school hours. Honor battery/autostart management may also require allowing this app to run. The overlay permission is used for permitted background activity launches; the app does not draw an overlay.

**An ordinary installation is not a tamper-proof kiosk.** Android screen pinning can require confirmation and can be manually exited. Force-stop, revoked permissions, power-off, OEM battery restrictions and lock-screen protection cannot be bypassed by this app. A foreground service does not guarantee execution every 60 seconds while Android is asleep. It never pins a background activity or attempts to bypass the keyguard.

For strong Home/Recents/notification-shade restrictions, use Android's **device-owner managed lock-task mode**, provisioned by an authorized device administrator. Provisioning may require a factory reset. Back up first; do not reset a tablet merely to install an update. On an eligible dedicated device, the standard command is:

```text
adb shell dpm set-device-owner com.ahura.schoolkiosk/.KioskAdminReceiver
```

The app allowlists only itself for managed lock task. Managed-device behavior needs separate testing on a provisioned device. This release was tested on an unmanaged Honor tablet; it does not claim to solve all Honor firmware gesture bugs.

## Build and test

Install .NET SDK 10.0.401, its Android workload, Android SDK platform/build tools 36 and JDK 17.

```powershell
dotnet workload install android
dotnet run --project tests -c Release
dotnet build app/SchoolKiosk.csproj -c Release -p:AndroidSdkDirectory=C:/Android/Sdk
```

The framework's development signing key is not the published release key. Keep a private release key outside the repository; updates must use the same certificate as the installed APK. Never commit passwords or signing keys.

For subsequent releases, commit reviewed source first, authenticate GitHub CLI, set `KIOSK_SIGN_PASSWORD` privately in the environment, then run:

```powershell
./build-and-publish.ps1 -AndroidSdk C:/Android/Sdk -SigningKeyPath C:/private/release.p12
```

This runs tests, builds, signs, verifies, hashes and publishes the APK with its exact source commit in a GitHub release. It refuses a dirty source tree. A failed build is not published. Ordinary `dotnet build` is only a local development build and does not publish. CI independently tests and compiles every push without access to the private signing key.

## Privacy and security

No advertising, analytics SDK, tracking service, remote command channel, camera, microphone, location or contact access is included. Internet access is for the configured website and its content; third-party websites have their own privacy practices. Links are not restricted to one origin, so this is **not a school-domain allowlist**. Downloads are disabled and HTTP cleartext traffic is disallowed. Android WebView and .NET runtime are dependencies and need security updates.

The PIN and browser session are app-private local data, not an encrypted credential vault. Do not reuse a banking/account password as the administrator PIN. APK signature/checksum verification checks integrity, **not a guarantee of absence of malware**. Review the public code and dependency reports; no software can honestly promise absolute security.

Permission reasons: Internet/network state for browsing; boot receiver for schedule restoration; exact alarms for boundaries; overlay access for background launches; notifications and special-use foreground service for visible schedule monitoring; device-admin binding for optional managed kiosk mode. The optional Accessibility service observes foreground window package metadata only during Wi-Fi maintenance; window-content access and gesture injection are disabled. It never reads event text, passwords or screen contents and sends nothing to a server. Enable/disable it using Android Accessibility settings. Guard behavior is not guaranteed if Android kills/disables it; device-owner mode remains necessary for strong enforcement.

See [test report](docs/verification.md) for checks actually performed and unresolved cases.

Android references: [lock task](https://developer.android.com/work/dpc/dedicated-devices/lock-task-mode), [background activity limits](https://developer.android.com/guide/components/activities/background-starts), [foreground service types](https://developer.android.com/develop/background-work/services/fgs/service-types).
