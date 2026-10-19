# Glossary

**Tweak**: one described change to Windows that the app can apply and undo.

**Per-game profile**: a set of instant tweaks the user builds for one game, applied while that game runs and undone when it closes. Not a preset; presets don't exist.

**Temporary change**: a change made by a per-game profile, journaled as temporary so it is undone after the game closes, the app crashes, or the PC reboots.

**Global setting**: a tweak that stays applied until the user undoes it, including every tweak that needs a reboot.

**Feature update**: the yearly Windows 11 version upgrade (e.g. 24H2 to 25H2). Can be skipped by pinning a version.

**Quality update**: the monthly cumulative and security update. Can be deferred, never blocked.

**App list**: a user's own saved selection of apps (catalogue apps, plus any not-reviewed winget apps they chose), saved to a file and loaded on another PC to reinstall them. Not a bundle or preset; the app ships none.

**Not reviewed**: a winget app outside the curated catalogue. Search shows it with this mark, and it installs only after the user accepts that.

**Startup item**: anything that launches at sign-in (Run keys, Startup folders, sign-in tasks, Store app startup). Turning one off is reversible; deleting it is destructive.

**Protected service**: a service the app never changes, even in Advanced (anti-cheat, Windows Update core, boot drivers).

**Drift**: a change the app made that something else (usually a Windows update) has since undone, or a removed app that came back.

**Evidence tier**: how well a tweak's benefit is backed: proven, situational or possible placebo.

**Reversibility label**: what undo can do for a tweak: reversible, needs a download, or destructive. A destructive tweak needs a separate confirmation, enforced by the Engine.

**Risk flag**: a risk shown on every tweak it applies to: security risk, needs reboot, can stop boot, or an anti-cheat conflict (which warns, never blocks).

**Pending reboot**: a reboot-needing tweak that was applied or undone since Windows last started.

**Not applicable**: a tweak that doesn't fit this PC's hardware, edition or build. It is shown greyed out or hidden, and the Engine refuses to apply it.

**Never-touch rule**: a hard rule the Engine enforces in every mode, Advanced included: no test signing, driver-signature or kernel-debugging changes, no Driver Verifier, no known-vulnerable drivers, no protected services, no user folders, no blocked quality updates, no Edge removal.

**BIOS guide**: step-by-step instructions, matched to the detected board vendor and CPU, for a change only the BIOS can make (XMP/EXPO, PBO, Curve Optimizer, Intel undervolting). The app reads these settings (a **readout**) but never changes them.

**Confirm-or-revert**: every GPU or CPU tuning change goes back on its own after a short window (15 s for GPUs, 60 s for CPUs) unless the user keeps it. Several unkept changes in a row go back to the last kept settings.

**Tuning profile**: GPU or CPU tuning inside a per-game profile. It goes on when the game starts without confirm-or-revert (nobody is watching) and comes off with the rest of the profile, back to the kept tune or defaults. While it holds the hardware, the Overclocking page can't change it.

**Vendor warning**: a GPU vendor's risk notice the user must accept before any tuning on that GPU unlocks (Intel's overclock waiver for IGCL). Accepted once per GPU and remembered; the service tells the driver again on every start.

**Unclean shutdown**: the service didn't stop normally (crash, freeze, power cut) while hardware was tuned. The next start puts that hardware on defaults and doesn't re-apply the kept tune, so a bad tune can't loop.

**Release channel**: beta (every build, Aden first) or stable (a beta promoted by hand, for friends). The app updates itself from the channel it follows.

**Lean install**: an NVIDIA driver install stripped, on the user's PC, to the display driver and what it requires (plus NVPCF and PPC on laptops), with the installer's consent entries and telemetry URLs removed. Optional; the full package is the default.

**SMU allowlist**: the only Ryzen SMU messages the app may send to one CPU codename (all-core Curve Optimizer, PPT, TDC, EDC). PawnIO's RyzenSMU module passes any message, so the Engine's allowlist is the only guard; a CPU not on it is never sent anything.

**Strike**: an unclean shutdown or WHEA hardware error while a CPU tune was on. Each one skips that start's re-apply and steps Curve Optimizer back by 5; the second turns the tune off until it passes the stress test again.
