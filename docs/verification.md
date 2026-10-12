# Verification — 1.7.0

## 1.8.0 follow-up

- Bluetooth button/launch code removed.
- Screen-off/on restores the configured URL in the first live tab, without a tab-clearing operation. Device log confirmed wake restoration; multiple-tab and process-death behavior must not be conflated.
- Optional Wi-Fi Accessibility guard is explicitly user-approved, has no window-content or gesture capability, and is required before a school-hours Settings handoff.
- Seven pure Wi-Fi return policy assertions pass alongside all 60,483 schedule assertions.
- Removed timed pin-confirmation retry, allowed system pin/unpin windows, and added a two-second initial Settings-launch grace period. Wi-Fi maintenance expires after five minutes.
- Added an authenticated Stop pinning test control.
- Initial Home-return test observed the kiosk foreground within the next 600 ms check, with Android asking to pin again. Final signed-build device checks are recorded below when complete. An unmanaged device is not instant/tamper-proof confinement.
- Android disabled Accessibility during the update/force-stop test sequence; re-enable the guard through system settings after installing updates if needed. Do not infer enabled status from an earlier build.

Test device: Honor Pad 10, Android API 36, unmanaged (no device owner). Updated in place; no factory reset, data clearing, ownership change or security software changes.

## Passed so far

- Release build and signed update installation.
- 60,483 pure schedule assertions: every minute across two weeks, inclusive 08:30 start, exclusive 15:30 end and weekend skipping.
- Live clock simulation: 08:30 brings School Kiosk forward from Home; Android's ordinary pinning confirmation still requires acceptance.
- Home and Recents stay blocked after accepting pinning.
- Five rapid gear taps open administrator access. Correct existing PIN still opens settings.
- Fixed weekday schedule text appears; day/hour selectors are removed.
- Wi-Fi and Bluetooth both show an in-app warning while pinned; foreground activity remains School Kiosk and lock state remains PINNED. No Android Settings handoff.
- Simulated 15:30 releases pinning and returns to Honor Home automatically.
- Automatic date/time restored after clock simulation; original Honor Home remains the default launcher.
- Foreground schedule service remains running outside the browser; Android reports foreground notification ID 90 and special-use service type.
- Published release APK verifies with Android v2/v3 signatures. Release includes its exact source commit and SHA-256 checksum; signing key is not in GitHub.
- NuGet vulnerability query returned no known vulnerable packages in its current source database. This does not cover Android WebView/OS vulnerabilities or constitute antivirus certification.

## Pending / not established

- User-reported multi-touch wallpaper-only failure: foreground/keyguard guards and reduced pinning retries mitigate suspect transitions, but the exact failure has not been reproduced or proven fixed. User is testing a ten-minute pinning window.
- Reboot/autostart, background recovery over long sleep, and final-APK regression checks still pending. Rebuilt release APK is not byte-identical to the installed test APK, though app source is the same; do not treat that as binary verification.
- Device-owner mode not tested: current device has no owner. Ordinary pinning is not equivalent to managed kiosk mode.
- No claim of comprehensive malware certification. Source/dependency, signature and checksum checks do not prove absence of all vulnerabilities.

## Safe manual pinning test

After authenticated administrator access, “Test pinning for 10 minutes” creates a persisted, expiring test window without changing tablet time. It automatically releases at expiry unless the normal school schedule is active. This is an explicit administrator test override, not an editable school schedule. It also disables external Settings handoffs during the test.
