# School Kiosk development

- Canonical public source lives here; do not publish the unrelated `general fixes` repository.
- Keep changes focused, inspect relevant files, and test before release.
- Never commit signing keys, PINs, school URLs, browser data, screenshots, device identifiers or credentials.
- Fixed school schedule: weekdays 08:30–15:30 local time. The only manual override is an authenticated, expiring ten-minute test.
- Never unpin to open external Settings during an active lock window. Never pin while backgrounded or keyguard-locked.
- No device reset/owner provisioning without explicit approval. Ordinary pinning is not managed kiosk mode.
- Every deliverable build must publish reviewed source and its matching signed APK with checksums using `build-and-publish.ps1`. Keep the private signing key outside this repository. Diagnostic local builds are not releases.
- Update `docs/verification.md` honestly; distinguish observed behavior, untested behavior and OS limitations. Do not claim universal malware-free or exhaustive bug-free guarantees.
