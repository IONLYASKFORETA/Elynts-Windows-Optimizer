# Build the gaming tweak catalogue

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/5

Researched 2026-10-15. Scope: Windows 11 only, gaming first. Verdicts follow the owner's rule. **Proven** and **situational** tweaks go in the main section. **Possible placebo** tweaks still ship, but in the secondary section. Every tweak below has an undo path.

## Answer

- **Proven, measured or documented benefit (4):**
  - Turning off **Memory Integrity (HVCI) / VBS** gains about 4–6% average FPS in CPU-bound games, more in some titles. Tom's Hardware and ComputerBase measured this, and Microsoft acknowledges it. It is a real security trade-off, so the app must show a warning and make re-enabling easy.
  - Keeping **Optimizations for windowed games** on (flip model) gives lower latency plus VRR and Auto HDR, per Microsoft.
  - Turning **Enhance pointer precision** off gives deterministic 1:1 aim in games that do not use raw input. This is a consistency benefit, not an FPS one.
  - Making sure **useplatformclock is not forced on**, i.e. deleting the BCD value, keeps QPC on the TSC. Microsoft documents a QPC cost of about 0.8–1 µs plus a system call on HPET.
- **Situational (5):** power plan, timer resolution, HAGS, Game Mode, and per-game "Disable fullscreen optimizations". Each helps or matters only on some hardware or in some games, and can hurt elsewhere. Examples: HAGS is required for DLSS Frame Generation but gives no FPS gain on its own. Game Mode plus the Balanced plan are required for core parking on AMD dual-CCD X3D CPUs.
- **Possible placebo (12 in the table, plus the extra WinUtil/Hone tweaks listed under #22):**
  - Disabling core parking, forcing MSI mode, the Nagle/TCPNoDelay/TcpAckFrequency registry tweaks, and NetworkThrottlingIndex.
  - NIC interrupt moderation off, and SystemResponsiveness/MMCSS edits. Note that Microsoft documents that values below 10 are **clamped to 20**, so the popular "0" does not do what people think.
  - Win32PrioritySeparation, disabledynamictick, disabling HPET in Device Manager, standby-list clearing, and Game Bar/DVR removal beyond turning off background recording.
  - The generic debloat items in WinUtil/Hone: services set to manual, telemetry, visual effects, IPv6 and similar.
- **Key Windows 11 change:** since Windows 10 2004, `timeBeginPeriod` is **no longer global**. On Windows 11, a minimized or occluded window-owning process also loses its raised resolution. So "global timer resolution" tools only work through the undocumented `GlobalTimerResolutionRequests` kernel value, which is described as for debugging.
- **Evidence quality warning:** reputable, methodical benchmarks exist only for VBS/HVCI, HAGS, and (thinly) Game Mode. Almost all "latency" tweaks are backed only by forum anecdotes. The app should label them honestly. Where possible it should also offer a before/after measurement, such as PresentMon frametimes or MeasureSleep, rather than claim gains.

## Summary table

| # | Tweak | What it changes | Verdict | Risk | Undo |
|---|---|---|---|---|---|
| 1 | Memory Integrity (HVCI) / VBS off | `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled`=0 (or the Windows Security toggle); optionally turn off the Virtual Machine Platform feature | **Proven** (~4–6% average, up to ~12% in some games) | **High (security)**. Breaks some anti-cheat/feature dependencies; X3D setup guides expect VBS running | Set Enabled=1 and reboot; re-enable VMP |
| 2 | Optimizations for windowed games | Settings > System > Display > Graphics > "Optimizations for windowed games" (stored in `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` → `DirectXUserGlobalSettings` `SwapEffectUpgradeEnable=1;`, unverified, see notes) | **Proven** (on) | Low. Rare per-game issues | Toggle off globally or per app |
| 3 | Enhance pointer precision off | `HKCU\Control Panel\Mouse` `MouseSpeed`=0, `MouseThreshold1`=0, `MouseThreshold2`=0 (or the Control Panel checkbox) | **Proven** (consistency, not FPS) | Low. Desktop cursor feels slower | Restore 1 / 6 / 10 (Windows defaults) |
| 4 | useplatformclock not forced | `bcdedit /deletevalue useplatformclock` | **Proven** (as "restore default"); forcing it to `yes` is harmful | Low | Re-set the previous value from backup |
| 5 | Power plan (High / Ultimate Performance) | `powercfg /setactive SCHEME_MIN` or `/duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61` | **Situational** | Low–medium. More idle power and heat; **breaks X3D core parking** | `powercfg /setactive SCHEME_BALANCED` (or the saved previous GUID) |
| 6 | Timer resolution (0.5–1 ms) | Background process calling `NtSetTimerResolution`/`timeBeginPeriod`; optionally `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel` `GlobalTimerResolutionRequests`=1 | **Situational** | Low–medium. Higher power use; registry value is "for debugging" | Stop the process; delete the value; reboot |
| 7 | Hardware-accelerated GPU scheduling | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` `HwSchMode`=2 (on) / 1 (off) | **Situational** | Low. Isolated game/driver regressions | Restore previous HwSchMode; reboot |
| 8 | Game Mode | `HKCU\Software\Microsoft\GameBar` `AutoGameModeEnabled`=1/0 | **Situational** (keep **on** by default) | Low. Mixed per-game results; off breaks X3D parking | Restore previous value |
| 9 | Disable fullscreen optimizations (per exe) | `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers` `<exe path>`=`~ DISABLEDXMAXIMIZEDWINDOWEDMODE` | **Situational** (per game only, never global) | Low. Loses fast alt-tab and overlays | Delete the Layers value |
| 10 | Disable core parking | `powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100` | **Possible placebo** (harmful on X3D/hybrid) | Medium. Defeats AMD X3D CCD steering; more power | Restore the previous CPMINCORES value |
| 11 | MSI mode (force) | `HKLM\SYSTEM\CurrentControlSet\Enum\PCI\<dev>\<inst>\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties` `MSISupported`=1 | **Possible placebo** | **High**. Forcing on an unsupported device can cause BSOD or a failed boot | Delete/restore MSISupported; boot Safe Mode if needed |
| 12 | Nagle / TCPNoDelay / TcpAckFrequency | `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` `TcpAckFrequency`=1, `TCPNoDelay`=1 | **Possible placebo** | Low. More small packets and ACKs | Delete both values |
| 13 | NetworkThrottlingIndex | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` `NetworkThrottlingIndex`=0xFFFFFFFF | **Possible placebo** | Low. Can bring back audio glitches it was designed to prevent | Restore 10 (0xA) |
| 14 | NIC interrupt moderation off | `Set-NetAdapterAdvancedProperty -Name <nic> -DisplayName "Interrupt Moderation" -DisplayValue Disabled` | **Possible placebo** | Low–medium. Higher CPU/DPC load | Restore the previous DisplayValue (usually Enabled/Adaptive) |
| 15 | SystemResponsiveness / MMCSS Games task | `...\Multimedia\SystemProfile` `SystemResponsiveness`=0/10; `...\SystemProfile\Tasks\Games` `Priority`, `GPU Priority`, `Scheduling Category` | **Possible placebo** | Low. "0" is clamped to 20 by Windows | Restore 20 and the original Tasks\Games values |
| 16 | Win32PrioritySeparation | `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl` `Win32PrioritySeparation` (e.g. 0x26 / 0x28) | **Possible placebo** | Low | Restore 2 (default) |
| 17 | disabledynamictick | `bcdedit /set disabledynamictick yes` | **Possible placebo** | Low–medium. More idle power; "debugging only" | `bcdedit /deletevalue disabledynamictick` |
| 18 | HPET disabled in Device Manager / useplatformtick | Device Manager "High precision event timer" disable; `bcdedit /set useplatformtick yes` | **Possible placebo** | Medium | Re-enable the device; `bcdedit /deletevalue useplatformtick` |
| 19 | Xbox Game Bar / Game DVR | `HKCU\System\GameConfigStore` `GameDVR_Enabled`=0; `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR` `AppCaptureEnabled`=0; policy `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` `AllowGameDVR`=0 | Background recording off: **situational**. Removing Game Bar: **possible placebo** (and breaks X3D parking) | Low–medium | Restore values; reinstall Game Bar from the Store |
| 20 | Standby list clearing | Periodic `NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList)` (ISLC, Process Lasso, RAMMap "Empty Standby List") | **Possible placebo** | Low. Brief cache-refill cost | Stop the scheduled task or process |
| 21 | Visual effects "best performance" | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects` `VisualFXSetting`=2 | **Possible placebo** (for in-game FPS) | Low. Uglier desktop | Restore 0 (let Windows choose) |
| 22 | WinUtil/Hone "debloat" bundle (services to Manual, telemetry off, background apps off, IPv6 off, Teredo off, hibernation off, DNS change) | Various services, registry and netsh changes | **Possible placebo** for FPS (privacy/storage reasons are separate) | Medium. IPv6/Teredo off breaks some Xbox Live/Game Pass features | Each needs its own captured original state |

## Per-tweak details

### 1. Memory Integrity (HVCI) / Virtualization-Based Security: **proven**
- **Change:** Windows Security > Device security > Core isolation > Memory integrity. Registry: `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity` `Enabled` (DWORD 0/1). Microsoft also suggests turning off the "Virtual Machine Platform" optional feature. *(The registry path is from widely used practice. Microsoft's gaming page describes only the UI.)*
- **Evidence:**
  - Microsoft says "there may be a performance impact with Memory Integrity and VMP on" and suggests gamers may turn them off while playing, then back on. Source: the Microsoft Support page, as quoted by ElevenForum. The original URL now redirects. https://www.elevenforum.com/t/options-to-optimize-gaming-performance-in-windows-11.9492/ , original: https://prod.support.services.microsoft.com/en-us/windows/options-to-optimize-gaming-performance-in-windows-11-a255f612-2949-4373-a566-ff6f3f474613
  - Tom's Hardware measured a geometric-mean loss with VBS + HVCI of −5.6% (i7-11700K), −5.7% (i7-10700K), −3.3% (R7 5800X) and −4.1% (R7 3800X). Setup: RTX 3090, 1080p, five games. https://www.tomshardware.com/news/windows-11-gaming-benchmarks-performance-vbs-hvci-security/
  - ComputerBase measured HVCI on vs off with VBS on: about −6% average FPS and about −8% 1% lows on an R7 5800X with RX 7900 XTX at 720p (CPU-bound). The range was 0% (Forspoken) to about −12% (Diablo II R, Ghostwire). https://www.computerbase.de/2023-03/windows-11-vbs-hvci-spiele-benchmarks/
- **Caveats:** the effect shrinks when GPU-bound. Some vendor setups expect VBS on; for example, HWBusters' 7950X3D core-parking checklist says to confirm VBS is running. https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/
- **Risk:** this is a real security reduction (kernel code-injection protection). It must be opt-in, carry a clear warning, and offer a one-click re-enable.
- **Undo:** set `Enabled`=1, or use the toggle, then reboot.

### 2. Optimizations for windowed games (flip model): **proven**
- **Change:** Settings > System > Display > Graphics > Default settings > "Optimizations for windowed games", or per app. It upgrades DX10/DX11 games in windowed or borderless mode from blt-model to flip-model presentation. *(The registry storage `DirectXUserGlobalSettings` / `SwapEffectUpgradeEnable` is community-observed and not documented by Microsoft. Prefer verifying it on a test machine.)*
- **Evidence:** Microsoft lists the benefits as "lower frame latency" plus Auto HDR and VRR support. Turning on Auto HDR forces this setting on. https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952 . Background is in the DirectX "Updates in Graphics and Gaming" post: https://devblogs.microsoft.com/directx/?p=5822
- **Risk:** low. The app should recommend it **on**, not off.
- **Undo:** toggle it back, globally or per app.

### 3. Mouse acceleration ("Enhance pointer precision") off: **proven** (for consistency)
- **Change:** `HKCU\Control Panel\Mouse` `MouseSpeed`="0", `MouseThreshold1`="0", `MouseThreshold2`="0". Windows defaults are 1/6/10. Apply live via `SystemParametersInfo(SPI_SETMOUSE)`.
- **Evidence:** Microsoft documents that `WM_MOUSEMOVE` has pointer acceleration ("ballistics") applied, while raw input `WM_INPUT` "has no ballistics applied". So the setting only affects games that read cursor messages rather than raw input. https://learn.microsoft.com/en-us/windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement . The acceleration is velocity-dependent, so the same hand movement maps to different on-screen distances, which hurts muscle memory. https://www.howtogeek.com/321763/what-is-enhance-pointer-precision-in-windows/
- **Risk:** low. Desktop pointer feel changes.
- **Undo:** restore the captured original values (default 1/6/10).

### 4. HPET / `useplatformclock`: **proven** that forcing it on is bad; restoring the default is the tweak
- **Change:** `bcdedit /deletevalue useplatformclock`, which restores the default where Windows picks TSC. The popular harmful variant is `bcdedit /set useplatformclock true`.
- **Evidence:**
  - Microsoft: when QPC uses HPET or the ACPI PM timer, "a system call is required" and the per-call cost "is frequently in the vicinity of 0.8 – 1.0 microseconds". By contrast, the TSC costs tens to hundreds of cycles with no kernel transition. Windows picks the TSC automatically when it is suitable. https://learn.microsoft.com/en-ie/Windows/Win32/sysinfo/acquiring-high-resolution-time-stamps
  - BCDEdit docs say `useplatformclock` "should only be used for debugging". https://learn.microsoft.com/sv-se/windows-hardware/drivers/devtest/bcdedit--set
  - Community reports are mixed and uncontrolled. https://www.overclockers.at/printable/number-crunching/the-hpet-bug-what-it-is-and-what-it-isnt_251222/page_3 , https://forums.blurbusters.com/viewtopic.php?p=93173
- **App behaviour:** detect whether `useplatformclock` is set (via `bcdedit /enum {current}`) and offer to remove it. Do not offer to force it on.
- **Undo:** restore the captured previous BCD value.

### 5. Power plan: Balanced vs High / Ultimate Performance: **situational**
- **Change:** `powercfg /setactive SCHEME_MIN` (High Performance). For Ultimate, use `powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61` and then activate the returned GUID.
- **Evidence:**
  - Microsoft introduced Ultimate Performance for Windows 10 for Workstations, to remove "micro-latencies associated with fine-grained power management techniques". It targets workstations, not games. https://blogs.windows.com/windowsexperience/2018/02/14/announcing-windows-10-insider-preview-build-17101-fast-build-17604-skip-ahead/ (quoted at https://www.winhelponline.com/blog/what-is-ultimate-performance-plan-in-windows-10-power-options/)
  - No reputable, methodical Windows 11 gaming benchmark was found that shows a consistent FPS gain over Balanced on modern CPUs. Evetech's write-up says Ultimate "rarely improves game frame rate by itself", but it publishes no data. https://evezone.evetech.co.za/quick-bytes/windows-power-plans-desktops-does-ultimate-performance-matter
  - **Counter-evidence:** AMD dual-CCD X3D CPUs (7950X3D/7900X3D/9950X3D) rely on Balanced-plan core parking, driven by the AMD 3D V-Cache Performance Optimizer plus Game Bar/Game Mode, to keep games on the V-Cache CCD. https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/
- **Risk:** higher idle power and heat, and laptop battery drain. It can *reduce* performance on X3D parts.
- **Recommendation:** offer it, but auto-skip on dual-CCD X3D CPUs and on laptops on battery, and label it "may help 1% lows on some desktops; measure."
- **Undo:** reactivate the captured previous scheme GUID, and delete a duplicated Ultimate scheme if the app created it.

### 6. Timer resolution (and the Windows 11 change): **situational**
- **Change:** a resident process calls `timeBeginPeriod(1)` or `NtSetTimerResolution(5000)` (0.5 ms). Optionally set `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel` `GlobalTimerResolutionRequests`=1 (DWORD) to restore pre-2004 global behaviour (Windows 11 / Server 2022+).
- **Windows 11 behaviour (primary source):** "Starting with Windows 10, version 2004, this function no longer affects global timer resolution… For processes which have not called this function, Windows does not guarantee a higher resolution than the default." Also: "Starting with Windows 11, if a window-owning process becomes fully occluded, minimized, or otherwise invisible or inaudible to the end user, Windows does not guarantee a higher resolution." https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod
- **Evidence for benefit:** the measurable effect is `Sleep()` precision, which matters for software frame limiters and some game loops. valleyofdoom's TimerResolution repo ships MeasureSleep plus a micro-adjust benchmark script to measure Sleep(1) delta and standard deviation. It notes the `GlobalTimerResolutionRequests` value and that it "should only be used for debugging purposes". https://github.com/valleyofdoom/TimerResolution . No reputable outlet benchmark shows FPS gains, and Blur Busters forum claims are anecdotal. https://forums.blurbusters.com/viewtopic.php?p=108333 . Most games already raise their own resolution, which is per-process under the new rules.
- **Risk:** higher power and wakeups. The registry value is undocumented apart from the debugging remark.
- **Recommendation:** ship it with a built-in MeasureSleep-style before/after reading so users see the actual effect.
- **Undo:** stop the resident process or task, delete `GlobalTimerResolutionRequests`, and reboot.

### 7. Hardware-Accelerated GPU Scheduling (HAGS): **situational**
- **Change:** Settings > Display > Graphics > Change default graphics settings. Registry: `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` `HwSchMode` (2=on, 1=off), then reboot.
- **Evidence:**
  - Microsoft's DirectX blog describes HAGS as moving scheduling to a GPU-side processor to reduce overhead, and does not promise FPS gains. https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/
  - GamersNexus found differences mostly within variance (under about 2%). There was a small repeatable loss in F1 2019 on an i3-10100 and a frametime regression in RDR2 that looked like a bug. https://gamersnexus.net/guides/3599-windows-10-hardware-accelerated-gpu-scheduling-benchmarks
  - HAGS is **required** for NVIDIA DLSS 3 Frame Generation, as reported in igor'sLAB's DLSS 3 enablement article. https://www.igorslab.de/en/dlss-3-frame-generierung-in-windows-10-aktivieren/
- **Recommendation:** on for RTX 40/50 users who use Frame Generation. Otherwise neutral; let users A/B.
- **Undo:** restore the captured HwSchMode and reboot.

### 8. Game Mode: **situational** (default on; do not offer "off" as an optimization)
- **Change:** Settings > Gaming > Game Mode. Registry: `HKCU\Software\Microsoft\GameBar` `AutoGameModeEnabled` (1/0).
- **Evidence:**
  - Microsoft's gaming-performance page recommends Game Mode on (quoted at https://www.elevenforum.com/t/options-to-optimize-gaming-performance-in-windows-11.9492/).
  - A 2026 MakeUseOf test (R5 5600, RX 5600 XT) found Game Mode on was *worse* in two of three games: about 4–5% lower average and about 10% lower 1% lows. It was slightly better in Warzone. This is a small sample with a weak method. https://www.makeuseof.com/disabled-game-mode-gained-back-cpu-performance-didnt-know-lost , summarized at https://windowsforum.com/windows-news.4/windows-11-game-mode-can-hurt-cpu-bound-fps-in-some-games.441277/
  - Game Mode is part of the AMD X3D core-parking chain. https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/
- **Risk:** low.
- **Undo:** restore the captured value.

### 9. Fullscreen optimizations (per game): **situational**
- **Change:** per exe, `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers` with value name = full exe path and data `~ DISABLEDXMAXIMIZEDWINDOWEDMODE`. This is the same as the Compatibility tab checkbox.
- **Evidence:** Microsoft says its telemetry shows FSO performance is "on average, as good as or better than" exclusive fullscreen for "almost all users". It advises disabling FSO only for a specific game that shows regression or input lag. https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations
- **Recommendation:** offer it only as a per-game troubleshooting toggle, never as a global default.
- **Undo:** delete the Layers value, or restore the captured previous data.

### 10. Core parking disable: **possible placebo** (harmful on some CPUs)
- **Change:** `powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100` then `powercfg /setactive SCHEME_CURRENT`. This is a hidden setting; unhide it with `powercfg -attributes SUB_PROCESSOR CPMINCORES -ATTRIB_HIDE`.
- **Evidence:** Microsoft documents that CPMinCores = 100% disables the core parking algorithm. https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-core-parking-cpmincores . No reputable Windows 11 gaming benchmark showing a gain was found. On X3D dual-CCD CPUs, parking is the mechanism that steers games to the cache CCD (see #5), so disabling it is counter-productive.
- **Risk:** medium on X3D; more idle power elsewhere.
- **Undo:** restore the captured CPMINCORES AC/DC values.

### 11. MSI (message-signaled interrupt) mode: **possible placebo**
- **Change:** under the device's hardware key, `...\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties` `MSISupported`=1 (optionally `MessageNumberLimit`), then reboot.
- **Evidence:** Microsoft documents the mechanism and value for driver INF authors, not as an end-user tweak. https://learn.microsoft.com/sr-latn-rs/windows-hardware/drivers/kernel/enabling-message-signaled-interrupts-in-the-registry . Drivers that support MSI already enable it in their INF. No reputable gaming benchmark was found; the evidence is forum and tool threads only. https://forums.guru3d.com/threads/better-msi-utility.458390/post-6375142
- **Risk:** **high.** Forcing MSI on a device or driver that does not support it can cause no-boot or BSOD, so recovery may need Safe Mode.
- **Recommendation:** read-only display ("this device already uses MSI"), or an expert-only toggle with a mandatory restore point.
- **Undo:** delete or restore `MSISupported`; use Safe Mode if needed.

### 12. Nagle's algorithm / TCPNoDelay / TcpAckFrequency: **possible placebo**
- **Change:** per interface `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` `TcpAckFrequency`=1 and `TCPNoDelay`=1 (DWORDs).
- **Evidence:**
  - Nagle batches small TCP segments, and applications disable it per socket via `TCP_NODELAY`. https://en.wikipedia.org/wiki/Nagle%27s_algorithm
  - Microsoft's networking blog shows Nagle combined with delayed ACK hurting request/response apps, and fixes it with `TcpAckFrequency` (KB 823764) and the socket option, not a `TCPNoDelay` registry value. https://learn.microsoft.com/id-id/archive/blogs/nettracer/tcp-delayed-ack-combined-with-nagle-algorithm-can-badly-impact-communication-performance
  - Most real-time game traffic is UDP, which Nagle does not touch. A per-interface `TCPNoDelay` value honoured by modern Windows is not documented in any current Microsoft source found.
- **Risk:** low.
- **Undo:** delete the values.

### 13. NetworkThrottlingIndex: **possible placebo**
- **Change:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` `NetworkThrottlingIndex`=0xFFFFFFFF (default 10).
- **Evidence:** Mark Russinovich explains that when MMCSS detects multimedia playback, NDIS receive indications are limited to about 10 packets/ms (10,000 pps). This was meant to protect audio from DPC storms, and it hurt gigabit *file-transfer* throughput. https://learn.microsoft.com/nl-nl/archive/blogs/markrussinovich/vista-multimedia-playback-and-network-throughput . Game traffic is far below 10k pps, so no gaming latency effect is expected. Disabling via 0xFFFFFFFF is documented only in KB 948066 and user reports.
- **Risk:** low; it may reintroduce audio glitches during heavy transfers.
- **Undo:** restore 10 (0x0000000A).

### 14. NIC interrupt moderation off: **possible placebo**
- **Change:** `Set-NetAdapterAdvancedProperty -Name "<nic>" -DisplayName "Interrupt Moderation" -DisplayValue "Disabled"`. The display name and values vary by vendor; Intel also has "Interrupt Moderation Rate".
- **Evidence:** Intel says a higher ITR lowers CPU load but adds latency, and lowering it helps small-packet workloads. It also says "the default setting is optimal for common network and system configurations" and "Do not change this setting without verifying that the desired change will have a positive effect". https://edc.intel.com/content/www/us/en/design/products/ethernet/adapters-and-devices-user-guide/interrupt-moderation-rate . The effect is at the microsecond level against internet ping in milliseconds. No gaming benchmark was found.
- **Risk:** low–medium (more interrupts and DPC time).
- **Undo:** restore the captured DisplayValue.

### 15. SystemResponsiveness / MMCSS "Games" task: **possible placebo**
- **Change:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile` `SystemResponsiveness` (tweak tools set 0 or 10), plus the `Tasks\Games` subkey (`GPU Priority`=8, `Priority`=6, `Scheduling Category`=High, `SFIO Priority`=High).
- **Evidence (important):** Microsoft says "values that are not evenly divisible by 10 are rounded down… **Values below 10 and above 100 are clamped to 20**. A value of 100 disables MMCSS." So the common "SystemResponsiveness = 0" is treated as **20**, the same as the default. https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service . Task settings only affect threads that register with MMCSS (`AvSetMmThreadCharacteristics`, e.g. "Games"), which many games do not. No benchmark was found.
- **Risk:** low.
- **Undo:** restore the captured values (SystemResponsiveness is typically 20).

### 16. Win32PrioritySeparation: **possible placebo**
- **Change:** `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl` `Win32PrioritySeparation` (default 2; tools use 0x26, 0x28 and similar).
- **Evidence:** Microsoft documents the foreground boost and quantum bits (legacy TechNet, https://technet.microsoft.com/en-us/library/cc940378.aspx). Gaming claims are forum-only. https://forums.blurbusters.com/viewtopic.php?p=69543
- **Risk:** low.
- **Undo:** restore the captured value.

### 17. Disable dynamic tick: **possible placebo**
- **Change:** `bcdedit /set disabledynamictick yes`.
- **Evidence:** Microsoft: "Enables and disables dynamic timer tick feature… This option should only be used for debugging." https://learn.microsoft.com/sv-se/windows-hardware/drivers/devtest/bcdedit--set . Only forum anecdotes claim gaming gains. https://forums.blurbusters.com/viewtopic.php?p=107460
- **Risk:** higher idle power.
- **Undo:** `bcdedit /deletevalue disabledynamictick`.

### 18. HPET disabled in Device Manager / `useplatformtick`: **possible placebo**
- **Change:** disable "High precision event timer" under System devices; `bcdedit /set useplatformtick yes`.
- **Evidence:** Windows already prefers the TSC for QPC (see #4), and `useplatformtick` is "debugging only" per the BCDEdit docs above. The evidence is anecdotal Blur Busters threads only. https://forums.blurbusters.com/viewtopic.php?p=93173
- **Risk:** medium; some platforms rely on HPET for other timers.
- **Undo:** re-enable the device; `bcdedit /deletevalue useplatformtick`.

### 19. Xbox Game Bar / Game DVR capture: background recording off **situational**; removal **possible placebo**
- **Change:**
  - Settings > Gaming > Captures > "Record what happened" off. Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR` `AppCaptureEnabled`=0 and `HKCU\System\GameConfigStore` `GameDVR_Enabled`=0. The policy is `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` `AllowGameDVR`=0.
  - Removal: `Get-AppxPackage Microsoft.XboxGamingOverlay | Remove-AppxPackage`.
- **Evidence:** Microsoft's own troubleshooting article blames a performance problem on Game Bar "remembering" an app as a game. Its fix is turning off "Record in the background while I'm playing a game" or Game Bar. https://learn.microsoft.com/en-us/microsoftteams/troubleshoot/teams-on-windows/performance-affected-when-teams-remembered-as-game . Background recording does continuous encoding. No benchmark was found for simply having Game Bar installed. Removing it breaks X3D parking (#5/#8) and Game Bar features.
- **Recommendation:** ship "background recording off" in the main section and "remove Game Bar" in the secondary section with an X3D warning.
- **Undo:** restore the values; reinstall Xbox Game Bar from the Microsoft Store.

### 20. Standby list clearing: **possible placebo**
- **Change:** a periodic purge of the standby list (ISLC, Process Lasso, or RAMMap "Empty Standby List").
- **Evidence:** the Bitsum (Process Lasso) developer says "most people should not use it". He notes Windows already prunes the list, purging has a cost, and he added the feature on gamer request. The only concrete report is a stutter on Win10 1809 that stopped on 1909+. https://community.bitsum.com/forum/index.php?msg=30475
- **Risk:** low; re-reads from disk after a purge.
- **Undo:** stop or remove the task.

### 21. Visual effects "best performance": **possible placebo** for games
- **Change:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects` `VisualFXSetting`=2 (WinUtil advanced tweak). https://winutil.christitus.com/code-reference/tweaks/z--advanced-tweaks---caution/display/
- **Evidence:** none for in-game FPS. Desktop effects are composited by DWM, which steps aside for fullscreen and independent-flip games (see #9).
- **Undo:** restore 0 or the captured value.

### 22. Other popular WinUtil / Hone items: **possible placebo** for gaming
- **WinUtil:** its Essential Tweaks include setting services to Manual, disabling telemetry, Activity History, Widgets, hibernation and Delivery Optimization. Its Advanced Tweaks include IPv6 off, Teredo off, background apps off and DNS change. The full list with per-tweak code is at https://winutil.christitus.com/guides/ . None publishes gaming measurements; their justifications are privacy, storage or bloat.
- **Hone:** advertises "Boost FPS / Reduce Latency" with only general categories and a testimonial ("up to 45% FPS"). Its own FAQ says results vary. https://hone.gg/
- **Risks:**
  - Teredo/IPv6 off can break Xbox Live party/multiplayer NAT traversal.
  - Services set to Manual can break features.
  - The app should treat these as a separate "privacy/declutter" category, not as gaming optimizations.
- **Undo:** capture each service start type, registry value and netsh state before changing.

## Implementation notes for the app

- **Capture before apply:** the app must store every original value (registry, BCD via `bcdedit /enum`, powercfg indices, NIC advanced properties, service start types). "Undo" must restore the captured value, not an assumed default.
- **Detect hardware before offering:**
  - Dual-CCD X3D: skip #5, #10 and Game Bar removal.
  - Laptop on battery: skip #5 and #6.
  - RTX 40/50 with Frame Generation: recommend HAGS on.
- **Measurement hooks:** a PresentMon frametime capture and a Sleep-precision check would let "situational" and "placebo" tweaks prove themselves per machine. This is consistent with the owner's evidence rule.
- **Unverified items to confirm on a Windows 11 test VM:**
  - The registry storage for "Optimizations for windowed games".
  - Whether modern Windows reads per-interface `TCPNoDelay`.
  - The exact current Microsoft Support URL for the gaming-performance page; the original now redirects.
