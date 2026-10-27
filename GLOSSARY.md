# Glossary

**Tweak**: one described adjustment to Windows, with its own switch, that the app can apply and, unless it is labelled destructive, undo.
_Avoid_: setting, optimization

**Change**: one thing the app did to Windows, saved with exactly what was there before it: a tweak switched on, a startup item switched, a service's start type set, or a background app closed by a per-game profile. Each change is a row in the Undo history. Cleaning, driver installs and tuning aren't changes.
_Avoid_: global setting, journal entry

**Undo**: putting back exactly what was there before a change. The app only undoes its own changes, and refuses when something else has changed the same setting since.
_Avoid_: revert, which is kept for tuning

**Whole-PC change**: a change whose every part affects every account on the PC (machine-wide settings, services, boot settings, power plans); a change that also touches one account's own settings is an account change. It belongs to the PC, not to the account that made it, so any administrator account can see and undo it.

**Account change**: a change to one Windows account's own settings. Only that account sees and undoes it.
_Avoid_: user setting, per-user change

**Undo history**: this account's changes plus every whole-PC change, newest first, each with its own Undo. **Restore everything** undoes every change in it that can be undone, newest first.

**Interrupted change**: a change that stopped part-way, because the service stopped in the middle of it or putting it back after a failure didn't work. What was there before is still saved, so it stays in the Undo history and can be undone like any other change.

**Administrator account**: a Windows account in the Administrators group. Only administrator accounts can use the app; anyone else is told to ask one.

**Already set**: a tweak that is on because something other than the app set it (Windows, the user or another tool). It counts as on, but the app has no change of its own to undo, so it can't be switched off from the app.

**Instant change**: a change that takes effect at once, with no restart or sign-out. Only tweaks that make instant changes can go in a per-game profile, and never destructive or Security ones.

**Per-game profile**: what the user sets for one game, all of it instant: tweaks, background apps to close, the game's priority, the timer resolution and a tuning profile. It goes on when the game starts and comes off 15 seconds after it closes. One runs at a time across the PC: the first game started stays in charge, and another game's profile is skipped meanwhile. Not a preset; presets don't exist.

**Per-game tweak**: a tweak set for one game's .exe or folder (fullscreen optimizations off, a Defender scan exclusion) that stays until it is undone, whether the game is running or not. A separate thing from a per-game profile.

**Temporary change**: a change made by a per-game profile. It is undone when the game closes, or at the next start after a crash or reboot. Switching the same tweak on yourself while the game runs keeps it, so it stays on after the game.

**Feature update**: the yearly Windows 11 version upgrade (e.g. 24H2 to 25H2). Can be skipped by pinning a version.

**Quality update**: the monthly cumulative and security update. Can be deferred, never blocked.

**App list**: a user's own saved selection of apps (catalogue apps, plus any not-reviewed winget apps they chose), saved to a file and loaded on another PC to reinstall them. Not a bundle or preset; the app ships none.

**Not reviewed**: a winget app outside the curated catalogue. Search shows it with this mark, and it installs only after the user accepts that.

**Startup item**: anything that launches at sign-in (Run keys, Startup folders, sign-in tasks, Store app startup). Turning one off is reversible; deleting it is destructive.

**Reviewed service**: a service on the app's list of ones that are safe to change. It can be set to Automatic or Manual; Disabled, and any service off the list, need Advanced.

**Protected service**: a service the app never changes, even in Advanced (anti-cheat, Windows Update core, boot drivers).

**Advanced**: a switch on one page that unlocks that page's riskier choices (on Startup & Services, Disabled and services off the reviewed list; in CPU tuning, Curve Optimizer down to -30). Each page has its own, it starts off every time the app opens, and it never unlocks a never-touch rule.
_Avoid_: Advanced mode, expert mode

**Drift**: a change the app made that something else (usually a Windows update or an app's own update) has since undone, including a removed app that came back and a startup item switched back on. A change that ends on its own, like the 35-day update pause, never drifts.

**Drift check**: looking for drift: quick each time the app starts, and full when the Windows build has changed, which also re-checks that every change still fits this build and PC.

**Unsupported change**: a change whose target this Windows build no longer has. Drift skips it quietly, after saying so once, until the build or the app changes.

**Evidence tier**: how well a tweak's benefit is backed: proven, situational or possible placebo.

**Reversibility label**: what undo can do for a tweak: reversible, needs a download, or destructive. A destructive tweak needs a separate confirmation, enforced by the Engine, and its change is never offered for undo.

**Risk flag**: a risk shown on every tweak it applies to: security risk, needs reboot, can stop boot, or an anti-cheat conflict (which warns, never blocks).

**Pending reboot**: a reboot-needing tweak that was applied or undone since Windows last started.

**Not applicable**: a tweak that doesn't fit this PC's hardware, edition or build. It is shown greyed out or hidden, and the Engine refuses to apply it.

**Never-touch rule**: a hard rule the Engine enforces in every mode, Advanced included: no test signing, driver-signature or kernel-debugging changes, no Driver Verifier, no known-vulnerable drivers, no protected services, no user folders, no blocked quality updates, no Edge removal.

**BIOS guide**: step-by-step instructions, matched to the detected board vendor and CPU, for a change only the BIOS can make (XMP/EXPO, PBO, Curve Optimizer, Intel undervolting). The app reads these settings (a **readout**) but never changes them.

**Confirm-or-revert**: every new GPU or CPU tune goes back on its own after a short window (15 s for GPUs, 60 s for CPUs) unless the user keeps it. Several unkept tunes in a row go back to the last kept settings.

**Revert**: GPU or CPU tuning going back on its own, as confirm-or-revert does. Only tuning reverts; a change is undone.

**Tuning profile**: GPU or CPU tuning inside a per-game profile. It goes on when the game starts without confirm-or-revert (nobody is watching) and comes off with the rest of the profile, back to the kept tune or defaults. While it holds the hardware, the Overclocking page can't change it.

**Vendor warning**: a GPU vendor's risk notice the user must accept before any tuning on that GPU unlocks (Intel's overclock waiver for IGCL). Accepted once per GPU and remembered; the service tells the driver again on every start.

**Unclean shutdown**: the service didn't stop normally (crash, freeze, power cut) while hardware was tuned. The next start puts that hardware on defaults and doesn't re-apply the kept tune, so a bad tune can't loop.

**Release channel**: beta (every build, Aden first) or stable (a beta promoted by hand, for friends). The app updates itself from the channel it follows.

**Lean install**: an NVIDIA driver install stripped, on the user's PC, to the display driver and what it requires (plus NVPCF and PPC on laptops), with the installer's consent entries and telemetry URLs removed. Optional; the full package is the default.

**SMU allowlist**: the only Ryzen SMU messages the app may send to one CPU codename (all-core Curve Optimizer, PPT, TDC, EDC). PawnIO's RyzenSMU module passes any message, so the Engine's allowlist is the only guard; a CPU not on it is never sent anything.

**Strike**: an unclean shutdown or WHEA hardware error while a CPU tune was on. Each one skips that start's re-apply and steps Curve Optimizer back by 5; the second turns the tune off until it passes the stress test again.

**Accent wave**: the slow red-to-orange colour wave on the app's accents (the top strip, the selected page's bar, switches that are on, main buttons, meters). It moves only while the window is in front, Windows' animation effects are on and the user hasn't turned off Animated accents on the Settings page; otherwise it holds still. Animated accents starts off when Windows' animation effects are off.

**Detail pane**: the panel on the right of the window that explains the selected tweak or repair tool in full: its labels, warnings, what it does, the evidence, the exact change and how to undo it. It is hidden on pages with nothing selected.

**Home page**: the page the app opens on, first in the sidebar. It shows this PC at a glance (board and CPU, GPU, memory, Windows, live temperatures), how many tweaks are on, what is waiting (restarts, driver updates, drift), Clean up and Check drivers, and a card per section. Each waiting item and section card opens the page that deals with it.

**PC overview**: the one Engine query behind the Home page: the PC's hardware and Windows, the tweaks that are on out of those that fit this PC, pending reboots, and each section's on and total counts. Not-applicable tweaks are left out of every count.
