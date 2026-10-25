# Production readiness

## Release configuration still needed

- `manifestUrl` is configured in `src/TLBB.Launcher/Config/appsettings.json` as `https://github.com/echne/tlbb-setup-storage/releases/download/client-v1.0.0/manifest.json`. Keep the hosted manifest synchronized with the release assets. The URL and trusted hosts are embedded in the EXE, so changes require a rebuild.
- The manifest's `registerUrl` is `null` because no registration URL has been supplied.
- The GitHub release hosts `github.com` and `release-assets.githubusercontent.com` are allowlisted. The launcher follows only HTTPS redirects to exact allowlisted hosts.

## Release data recorded

- Release `client-v1.0.0` contains `TLBB.z01`–`TLBB.z07` and `TLBB.zip` with the public URLs, byte sizes, and SHA-256 values recorded in the manifest.
- SHA-256 values from the local archive directory match GitHub's release asset digests.
- The ZIP central directory contains 75 entries, identifies `Launch.exe` at the archive root, and declares 668,326,388 uncompressed bytes.
- The production SharpCompress extraction service successfully extracted the supplied split archive to 70 files totaling 668,326,388 bytes, including the root `Launch.exe`.

## Before distribution

- Verify install commit/rollback, the desktop shortcut target, and launching `Launch.exe` on Windows 10/11 x64.
- Verify cancellation/resume behavior with the GitHub release redirects.
- Manually verify that the selected game folder is created, Defender UAC runs before download, cancellation stops before download, and an already-present exclusion is handled.
- Verify checkbox availability and the inline hint with Defender active, a third-party provider registered, and provider data unavailable. Confirm a saved selection is cleared if Defender exclusions cannot be used. Windows Security provider data can be stale or incomplete.
- Confirm `.staging` is preserved after pause/failure and absent from the final installed folder after a successful commit.
- Confirm path, disk-space, Defender, network, and installation failures appear in an error dialog with actionable wording.
- Code-sign the release when the server supplies a signing certificate. Update delivery and structured logs are not configured yet.

The current single-file EXE was published after the UI, antivirus-status, and staging changes. The split archive was not rechecked after those changes; archive operations were intentionally not run during this update.
