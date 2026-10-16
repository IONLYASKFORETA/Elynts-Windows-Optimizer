# Installing Elynt's Windows Optimizer

The app is free and private, shared between friends. It isn't code-signed (that costs money), so Windows warns
about it once. This page shows the two things you may need to click through. It takes about two minutes.

You need **Windows 11**. On anything else the app tells you so and closes without changing anything.

## 1. Download

Go to the releases page: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer-releases/releases

Under the newest release that is **not** marked "Pre-release", download the file ending in `Setup.exe`.
(Pre-releases are beta builds that Aden tests first.)

## 2. "Windows protected your PC" (SmartScreen)

When you open the Setup file, Windows may show a blue box saying **"Windows protected your PC"**.

1. Click **More info** (the small link under the text).
2. Check that the app name says `ElyntOptimizer` and the publisher says "Unknown publisher".
3. Click **Run anyway**.

This happens because the app is new and unsigned, not because anything was found in it. It may come back for each
new version you download by hand; updates from inside the app don't ask.

## 3. If Windows blocks it outright (Smart App Control)

If instead you see **"Smart App Control blocked an app that may be unsafe"** (or "part of this app"), and there is no
Run anyway button, Smart App Control is on. It blocks every unsigned app, and there is no per-app exception, so the
only fix is to turn it off:

1. Open **Start**, type **Windows Security** and open it.
2. Go to **App & browser control**, then **Smart App Control settings**.
3. Choose **Off**.
4. Run the Setup file again.

If Smart App Control already says Off or Evaluation, this step isn't the problem. On Windows 11 with the April 2026
update or newer you can turn Smart App Control back on later from the same page; on older builds, turning it back on
needs a reinstall of Windows, so update Windows first if you want to keep that option.

## 4. First start

Setup installs the app for your account only and opens it. The first time, the app installs its small background
service, which is what makes the changes you choose. Windows asks **"Do you want to allow this app to make changes
to your device?"** once; choose **Yes**. The app window itself never runs as administrator.

If you said No by accident, press **Retry** at the bottom of the window.

## Updates

The app checks for updates when it starts. When one has downloaded, a **Restart to update** button appears at the
bottom of the window. After the restart, Windows asks once more for permission to update the background service.

The **Updates** box at the bottom right picks the channel: **Stable** (the default) or **Beta** (newer, less tested).

## Uninstalling

Use **Settings > Apps > Installed apps > Elynt's Windows Optimizer > Uninstall**. Windows asks for permission to
remove the background service too. Turn off any tweaks you want undone *before* uninstalling: the undo history is
kept in `C:\ProgramData\ElyntOptimizer`, so reinstalling later can still undo them.

## Something went wrong

Send Aden the log files from `%LocalAppData%\ElyntOptimizer\logs` (paste that into the Explorer address bar) and
`C:\ProgramData\ElyntOptimizer\logs`.
