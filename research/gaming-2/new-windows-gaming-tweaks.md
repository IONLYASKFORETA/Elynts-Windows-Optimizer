# Find new evidence-backed Windows gaming tweaks

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/90

Researched 2026-10-30, for Windows 11 24H2 (build 26100) and 25H2 (build 26200). I used Microsoft Learn, the DirectX and Windows blogs, Windows release health, and GPU and CPU vendor pages first, and community tests only where no first-party data exists. Anything marked **(community)** comes from a secondary or community source. Anything marked **(inference)** is my own reasoning, and no source says it. The shared web budgets ran out partway through, so some sources were judged by their title or search listing only. Those are marked **(title only)** and should be read before a tweak is built on them.

This file covers only tweaks that v1 does not already have. v1's 22 reviewed tweaks are in `research/gaming-tweak-catalogue.md` on branch `claude/project-thread-5ggkan` and in `src/Elynt.Core/Tweaks/`. GPU driver-panel settings, network settings, hidden-loss detection, measuring and booster surveys belong to other tickets.

## Answer

- **Only one new tweak is proven: per-app GPU preference, and only on PCs with two GPUs.** On a laptop with integrated and dedicated graphics, Windows decides which GPU a game uses. Its setting overrides the NVIDIA Control Panel. Forcing a game onto the dedicated GPU is a real fix when Windows picked the weak one. On a PC with one GPU, it does nothing.
- **Situational (worth shipping in the main section, with clear "when it helps" text):**
  - The Windows **"Variable refresh rate"** switch. It only helps DirectX 11 fullscreen games that lack their own VRR support, on a VRR monitor.
  - **Auto HDR.** It is a picture feature, not a speed one. It had a crash bug in 24H2 that was fixed in early 2025.
  - The **power mode slider set to "Best performance"**. It only exists while the Balanced plan is on. It switches off power throttling for all apps and favours speed, which matters most on laptops. It is an instant change, so it fits per-game profiles. It also keeps the Balanced plan that AMD X3D core parking needs.
  - **Stopping Windows from lowering an idle foreground game on battery** (`DisableUserPresenceQos`). This is new in 24H2 (mid-2025), and Microsoft itself says to turn it off when benchmarking on battery.
  - **Turning multiplane overlay (MPO) off**, only as a fix for flicker, black screens or desktop stutter. The old NVIDIA registry value no longer works on current Windows 11, according to community reports.
  - **Pinning a game to the V-Cache CCD** on dual-CCD Ryzen X3D CPUs. Use it only when AMD's automatic steering misses a game. It is an instant change and fits per-game profiles.
  - **Communications ducking off.** This stops Windows lowering game sound by 80% when voice chat starts. It is a comfort fix.
- **Possible placebo (secondary section):** USB selective suspend off, PCIe link-state power management off, power throttling off for the whole PC, pinning games to P-cores on Intel hybrid CPUs, audio enhancements and other audio "latency" settings, memory compression off, Spectre/Meltdown mitigations off, and Control Flow Guard (exploit protection) off.
- **Two of those carry real risks.** Spectre mitigations off is a security risk. Exploit protection off can stop Riot Vanguard games from launching (`VAN 9002`). I recommend not shipping either.
- **There is no Windows setting for the DirectX shader cache size.** The size controls live in the GPU driver panels, which another ticket covers. Microsoft's fix for shader-compile stutter is Advanced Shader Delivery, which games and stores adopt; it is not a tweak. v1 already keeps shader caches out of automatic cleaning.
- **New in 24H2, 25H2 and 2026, but not tweaks:** Xbox mode (the full-screen Xbox shell, out for all Windows 11 PCs since April 2026), Auto SR (Snapdragon and ROG Xbox Ally X only), Advanced Shader Delivery, the 24H2 Auto HDR and Easy Anti-Cheat fixes, and Microsoft's 2026 work on background load and scheduling. The app can point to Xbox mode but has no documented way to switch it.
- **Gaps:** No reputable FPS measurement exists for any of the new power or USB settings. The P-core pinning, memory compression and X3D affinity tests I found were not read in full. Several registry names are community-sourced and need a check on a Windows 11 25H2 test PC before shipping (see "Checks before building").

## Candidates at a glance

"Instant" follows the glossary: it takes effect at once with no restart or sign-out. "Next launch" means no restart, but the game reads it when it starts. That suits a per-game tweak (set once per .exe), not a per-game profile, which starts after the game does **(inference)**.

| # | Tweak | What it changes | Tier | Undo | Instant? | Fits | Anti-cheat |
|---|---|---|---|---|---|---|---|
| 1 | GPU preference per game | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, value = game .exe path, data `GpuPreference=2;` | **Proven** (two GPUs only) | Delete or restore the value | Next launch (per-game tweak) | Laptops with iGPU + dGPU; desktops with two GPUs | None known |
| 2 | Windows "Variable refresh rate" | Same key, `DirectXUserGlobalSettings` item `VRROptimizeEnable=1;` | **Situational** | Restore the item | Next launch | VRR monitor, any GPU vendor | None known |
| 3 | Auto HDR | Same value, item `AutoHDREnable` (global or per game) | **Situational** (looks, not speed) | Restore the item | Next launch | HDR display; DX10–DX12 games | None known |
| 4 | Power mode "Best performance" | Power slider overlay `ded574b5-45a0-4f42-8737-46345c09c238` on the Balanced plan | **Situational** | Restore the previous overlay | **Yes** (profile-eligible) | Laptops most; desktops on Balanced; X3D | None |
| 5 | Keep an idle game at full QoS on battery | `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling` `DisableUserPresenceQos`=1 | **Situational** | Delete the value | Unknown, so treat as needs restart until tested | Laptops and handhelds on battery | None |
| 6 | Multiplane overlay (MPO) off | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` `DisableOverlays`=1 (current); `HKLM\SOFTWARE\Microsoft\Windows\Dwm` `OverlayTestMode`=5 (old) | **Situational** (troubleshooting only) | Delete the value; restart | No (restart) | Anyone with flicker or black screens | None known |
| 7 | Pin a game to the V-Cache CCD | `SetProcessDefaultCpuSets` (or affinity) on the game process | **Situational** | Clear the CPU sets; the game exiting also ends it | **Yes** (profile-eligible) | Ryzen 7900X3D/7950X3D/9900X3D/9950X3D | None documented; protected games may refuse the handle |
| 8 | Communications ducking off | `HKCU\Software\Microsoft\Multimedia\Audio` `UserDuckingPreference`=3 | **Situational** (comfort) | Restore the value | Yes **(inference)** | Anyone using voice chat | None |
| 9 | USB selective suspend off | Power setting `2a737441-…` / `48e6b7a6-50f5-4782-a5d4-53bb8f07e226` = 0 | **Possible placebo** | Restore AC/DC indexes | Yes | Only as a fix for USB dropouts | None |
| 10 | PCIe link-state power management off | Power setting `501a4d13-42af-4429-9fd1-a8218c268e20` / `ee12f906-d277-404b-b6da-e5fa1a576df5` = 0 | **Possible placebo** | Restore AC/DC indexes | Yes **(inference)** | Desktops on Balanced; not laptops on battery | None |
| 11 | Power throttling off for the whole PC | `…\Control\Power\PowerThrottling` `PowerThrottlingOff`=1 | **Possible placebo** | Delete the value | No (restart) **(community)** | Nobody by default | None |
| 12 | Pin games to P-cores (Intel hybrid) | Affinity or CPU sets to P-cores | **Possible placebo** | Clear | Yes | 12th–14th gen, Core Ultra | Same as #7 |
| 13 | Audio enhancements off and other audio "latency" settings | Per-device effects; exclusive mode; sample rate | **Possible placebo** (for FPS and latency) | Restore | Yes | Only as a fix for broken audio effects | None |
| 14 | Memory compression off | `Disable-MMAgent -MemoryCompression` | **Possible placebo** | `Enable-MMAgent -MemoryCompression` | No (restart) **(community)** | Nobody by default | None |
| 15 | Spectre/Meltdown mitigations off | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management` `FeatureSettingsOverride` / `…Mask` | **Possible placebo**, security risk | Delete both values; restart | No (restart) | Mostly older CPUs | None documented |
| 16 | Control Flow Guard / exploit protection off | Exploit protection, system-wide or per .exe | **Possible placebo**, security risk | Restore the setting | No | Nobody | **Vanguard won't launch (`VAN 9002`)** |

## Main section: proven and situational

### 1. GPU preference per game: **proven** (on PCs with two GPUs)

- **What it changes:** Settings > System > Display > Graphics, then a GPU choice per app. Windows stores it in `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, in a value named with the full path to the .exe ([ElevenForum](https://www.elevenforum.com/t/change-graphics-performance-preference-for-apps-in-windows-11.5675/latest) **(community)**). The data is `GpuPreference=N;`. The numbers match DirectX's documented `DXGI_GPU_PREFERENCE` enum: 0 = no preference, 1 = minimum power (integrated), 2 = high performance (dedicated or external) ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_6/ne-dxgi1_6-dxgi_gpu_preference)). That mapping is also stated by a user on [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/a/6800271) **(community)**. On PCs with two or more dedicated GPUs, build 25336 and later can pick a specific one; this is stored as a `SpecificAdapter=` item **(community)**, same ElevenForum page.
- **Evidence:**
  - Microsoft's DirectX blog (2023-01-19, build 25281) describes the per-app GPU preference and the "Default high performance GPU" choice for hybrid and gaming laptops. It also says a per-app setting overrides the system-wide one ([DirectX blog](https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page)).
  - NVIDIA says that from Windows 10 20H1 "the Windows OS assigns the graphics processor" and that the Windows setting "overrides any settings made in the NVIDIA Control Panel" ([NVIDIA Control Panel help](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nv3d/Setting_the_preferred_graphics_processor.htm)). So on Windows 11 this is the switch that counts.
  - "Let Windows decide" uses the integrated GPU for 2D apps and the dedicated one for 3D apps (ElevenForum **(community)**). Users on Microsoft Q&A report games and apps left on the integrated GPU ([Q&A](https://learn.microsoft.com/en-us/answers/a/6800271) **(community)**).
  - A game on the integrated GPU instead of a dedicated one is many times slower **(inference)**. That is why the tier is proven: the benefit is the whole dedicated GPU, not a small tuning gain.
- **Undo:** delete the value, or put back the saved data. Removing an app in Settings resets it to "Let Windows decide" (ElevenForum).
- **Instant?** No restart. Microsoft's text, as quoted by ElevenForum, is "Choices may not take effect until the next time the app launches". Build it as a per-game tweak (like v1's fullscreen-optimizations one), not a profile item **(inference)**.
- **Fits:** laptops with integrated + dedicated graphics (NVIDIA Optimus, AMD hybrid, Intel), and desktops with two GPUs. It is not applicable with one GPU. It does nothing on a laptop whose MUX switch is set to dedicated-only, because the integrated GPU is then hidden **(inference)**.
- **Anti-cheat:** none known. It is a per-user DirectX setting.
- **App note:** the Engine should detect two adapters and grey the tweak out otherwise. It should also show which GPU the game used last time, from Task Manager's GPU engine data **(inference)**.

### 2. Windows "Variable refresh rate" switch: **situational**

- **What it changes:** Settings > System > Display > Graphics > "Variable refresh rate". Stored as the `VRROptimizeEnable=1;` item inside the `DirectXUserGlobalSettings` string under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`. 0, or no item, means off (the default) ([ElevenForum](https://www.elevenforum.com/t/enable-or-disable-variable-refresh-rate-for-games-in-windows-11.12052) **(community)**).
- **Evidence:**
  - Microsoft says it "enables VRR support for DirectX 11 full screen games that do not support VRR natively". It works with FreeSync, G-SYNC and VESA Adaptive-Sync. It only appears with the right drivers and a VRR-capable monitor ([DirectX blog, 2023-01-19](https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page)).
  - The switch arrived in Windows 10 1903 ([Tom's Hardware, 2019](https://www.tomshardware.com/news/windows-10-variable-refresh-rate-vrr-dx11,39582.html) **(title only)**).
  - The benefit is VRR itself (no tearing without v-sync's waits), but only for that narrow set of games **(inference)**. Games with native VRR, DX12/Vulkan games and windowed games are not affected by this switch.
- **Undo:** put the item back as it was. The app must edit just this item in the shared string. v1's windowed-games tweak writes `SwapEffectUpgradeEnable` into the same value, so a whole-value write would clobber it **(inference)**.
- **Instant?** No restart; it applies when the game next starts **(inference)**. It is global only. The per-app drawer offers GPU preference, Auto HDR and windowed optimizations, not VRR (DirectX blog).
- **Fits:** VRR monitors on NVIDIA, AMD or Intel; desktop and laptop. It is not applicable without a VRR display (Settings hides it).
- **Anti-cheat:** none known.

### 3. Auto HDR: **situational** (a picture feature, not a speed one)

- **What it changes:** Settings > System > Display > HDR > Auto HDR, or per game in the Graphics page. It is stored as an `AutoHDREnable` item in the same `DirectXUserGlobalSettings` string **(community, unverified; check on a test PC)**.
- **Evidence:**
  - Microsoft: it "automatically upgrades the rendering of color in existing DirectX 10 thru DirectX 12 games" on an HDR display ([DirectX blog](https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page)). Turning it on forces "Optimizations for windowed games" on ([Microsoft Support](https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952), already cited by v1).
  - In 24H2, Auto HDR made some games crash or show wrong colours. Microsoft held the 24H2 upgrade back on affected PCs in December 2024 ([Neowin](https://www.neowin.net/news/microsoft-blocks-windows-11-24h2-update-on-more-pcs-due-to-new-gaming-issues/), [The Register](https://www.theregister.com/2024/12/19/windows_11_24h2_issues/) **(community, title only)**). It was fixed in early 2025 ([PCWorld](https://www.pcworld.com/article/2593590/windows-11s-auto-hdr-works-again-but-you-have-to-manually-update-first.html), [OC3D](https://overclock3d.net/news/software/microsoft-finally-fixes-windows-11-24h2s-auto-hdr-bugs/) **(community, title only)**).
  - I found no measured FPS cost. It is extra post-processing, so a small GPU cost is likely **(inference)**.
- **Tier reasoning:** it changes how a game looks, not how fast it runs. It belongs in the main section as an "if you have an HDR screen" option, never as a speed tweak.
- **Undo:** restore the item. It can be set per game.
- **Instant?** Next launch **(inference)**.
- **Fits:** HDR displays; SDR-only DX10–DX12 games.
- **Anti-cheat:** none known. It is part of Windows and injects nothing into the game **(inference)**.

### 4. Power mode "Best performance" (power slider): **situational**

- **What it changes:** Settings > System > Power & battery > Power mode. These are three "overlays" on top of the Balanced plan: Best power efficiency `961cc777-2547-4f9d-8174-7d86181b8a7a`, Balanced (no overlay), and Best performance `ded574b5-45a0-4f42-8737-46345c09c238` ([Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/customize-power-slider)).
- **Evidence:**
  - Microsoft: the slider only appears while **Balanced** (or a plan derived from it) is active. In Best performance, "all apps are opted out" of power throttling. OEMs tune the CPU's energy-performance preference per mode, and can link their own GPU and thermal settings to it. Game Mode works independently of it (same Learn page).
  - Microsoft's 2017 power-throttling post says Windows "turns throttling off" under Best performance ([Windows blog](https://blogs.windows.com/windowsexperience/2017/04/18/introducing-power-throttling/)).
  - I found no reputable FPS test. On laptops the OEM's per-mode power limits make the effect real but different on every model **(inference)**.
- **Why this one matters for the app:** v1's High and Ultimate Performance tweaks switch away from Balanced. That hides this slider and breaks dual-CCD X3D core parking (v1 catalogue #5). Best performance on Balanced is the speed option that keeps both **(inference)**. Whether the overlay changes X3D parking at all is untested; see "Checks before building".
- **How to set it:** Microsoft documents no registry value or runtime API for setting it, only provisioning and INF (Learn page). People use `powercfg /overlaysetactive <GUID>` or the undocumented `PowerSetActiveOverlayScheme` export ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/3919778/changing-overlay-power-mode-with-script) **(community, title only)**). Changes can be watched with the documented `PowerRegisterForEffectivePowerModeNotifications` (Learn page).
- **Undo:** save the previous AC and DC overlay and put it back.
- **Instant?** Yes. It fits per-game profiles.
- **Fits:** laptops most (more battery drain on DC). Desktops running Balanced. It is not applicable while v1's High or Ultimate plan is on.
- **Anti-cheat:** none.

### 5. Keep an idle foreground game at full speed on battery (`DisableUserPresenceQos`): **situational**

- **What it changes:** `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling` `DisableUserPresenceQos` (DWORD) = 1 ([Microsoft Learn: Quality of Service](https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service), updated 2025-12-09).
- **Evidence:**
  - Microsoft: "By default, Windows may lower the QoS policy of a foreground application to Medium QoS after a period of user inactivity where no input is detected" (on battery only). Microsoft adds that the feature "should be disabled when testing on battery (for example, running performance benchmarks)" (same page).
  - Its release column reads "Ge 2025.05", which suggests a May 2025 update to the 24H2 platform **(inference)**.
  - This bites during cutscenes, in-game benchmarks, and possibly controller play. Whether controller input counts as "user input" is not documented (gap).
- **Undo:** delete the value, or restore the saved one.
- **Instant?** Unknown. Treat it as needs-restart until tested.
- **Fits:** laptops and handhelds on battery. It is not applicable to desktops (never on battery).
- **Anti-cheat:** none.
- **Related, how Windows ranks apps:** a focused window gets High QoS. A visible but unfocused window gets Medium. A minimized or covered one gets Low, which on battery means efficient cores and frequency. Apps that tag themselves Eco always get efficient cores (same Learn page). So a focused game is not throttled. Only background helpers are, on battery **(inference)**. The per-process opt-out (`SetProcessInformation` with `ProcessPowerThrottling`, state cleared) is documented and instant. It could be offered for a named background app, such as a streaming tool, inside a per-game profile, but I found no evidence that games gain from it.

### 6. Multiplane overlay (MPO) off: **situational** (troubleshooting only, never a default)

- **What it changes:**
  - Current Windows 11: `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` `DisableOverlays` (DWORD) = 1. One user reports this works on 25H2. Another says it broke VRR ([Guru3D, 2025](https://forums.guru3d.com/goto/post?id=6305178) **(community)**).
  - The older value: `HKLM\SOFTWARE\Microsoft\Windows\Dwm` `OverlayTestMode` (DWORD) = 5 ([Neowin](https://www.neowin.net/amp/microsoft-amd-nvidia-are-all-sleeping-on-windows-11-mpo-display-issues/) **(community)**). This is what NVIDIA's 2021 fix (`mpo_disable.reg`, KB 5157, for drivers 461.09+) set ([Unreal Engine forum quoting NVIDIA](https://forums.unrealengine.com/t/dropdown-menus-disappear-instantly/478497/9) **(community)**). The NVIDIA page itself blocks automated reading. Guru3D users report it "doesn't do anything anymore" on current Windows 11.
  - Check the result with `dxdiag`: MaxPlanes 1 means off, 4 means on (Guru3D **(community)**).
- **Evidence:**
  - MPO arrived in Windows 8.1 (WDDM 1.3). It composes windows, video and the desktop in display hardware "without having to involve the CPU or use up other system resources" ([Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/multiplane-overlay-support)). So turning it off moves that work back to the GPU, and costs power and possibly latency in windowed games **(inference)**.
  - The case for turning it off is fixing bugs: flicker, black or white screens, stutter in browsers and desktop apps ([Neowin](https://www.neowin.net/amp/microsoft-amd-nvidia-are-all-sleeping-on-windows-11-mpo-display-issues/) **(community)**). No source claims an FPS gain.
- **Undo:** delete the value and restart.
- **Instant?** No, it needs a restart **(inference)**. Not for profiles.
- **Fits:** PCs with flicker or black-screen problems, often with multiple monitors **(community)**; any GPU vendor.
- **Anti-cheat:** none known.
- **App note:** label it "fix for flicker, not a speed tweak". Because the value name has changed once already, the drift check should confirm MPO is really off (MaxPlanes) and not just that the value exists **(inference)**.

### 7. Pin a game to the V-Cache CCD on dual-CCD Ryzen X3D: **situational**

- **What it changes:** while the game runs, it limits the game's threads to the cores of the CCD that has 3D V-Cache, using `SetProcessDefaultCpuSets` (CPU Sets, made for games: [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/xbox-apps/cpusets-games)) or `SetProcessAffinityMask`. The V-Cache CCD is the one whose L3 cache is larger, which `GetLogicalProcessorInformationEx` reports per core ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex)) **(inference: how to find it)**.
- **Evidence:**
  - AMD's own route is automatic. The chipset driver's "3D V-Cache Performance Optimizer", Xbox Game Bar's game detection, Game Mode and the Balanced plan park the other CCD while a game runs ([Hardware Busters](https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/), already cited by v1; AMD's install guide as reported by [Tom's Hardware](https://www.tomshardware.com/news/amd-ryzen-7000x3d-install-guide) **(title only)**).
  - That route can fail. In 2025 a Game Bar fault on **Windows 10** stopped X3D steering ([Neowin](https://www.neowin.net/news/report-microsoft-quietly-disables-vital-windows-feature-crippling-many-amd-ryzen-cpus/), [TechRadar](https://www.techradar.com/computing/windows/report-suggests-windows-10s-game-bar-is-crashing-with-some-high-end-amd-ryzen-cpus-and-i-hope-microsoft-will-investigate-soon) **(community, title only)**). Players also pin Battlefield 6 to CCD0 by hand ([EA forum](https://forums.ea.com/discussions/battlefield-6-technical-issues-en/9950x3d-core-parking/12741808/replies/12763769) **(community)**).
  - A community comparison of Process Lasso pinning against Game Bar parking on a 9950X3D exists ([r/hardware, March 2025](https://reddit.birdcat.cafe/r/hardware/comments/1jgyu9k/9950x3d_benchmarked_with_process_lasso_vs_game) **(community, not read)**). I found no reputable outlet test of manual pinning against working automatic parking.
  - When parking works, pinning should match it. The gain is only for games the driver misses, or where the user can't run Game Bar or Balanced **(inference)**.
- **Undo:** clear the CPU sets (or restore the full affinity mask). The change also ends when the game exits.
- **Instant?** Yes. It fits per-game profiles next to v1's game priority.
- **Fits:** Ryzen 9 7900X3D, 7950X3D, 9900X3D and 9950X3D: dual-CCD parts where only one CCD has V-Cache. It is not applicable to single-CCD X3D (7800X3D, 9800X3D) or other CPUs. It should not be used alongside v1's "core parking off" placebo tweak, which fights AMD's steering.
- **Anti-cheat:** no anti-cheat page names affinity or CPU sets. Setting either needs a process handle with set-information rights, the same as v1's priority setting. A protected game may refuse that handle **(inference)**. Riot's October 2026 block covers reading game memory, not this (anti-cheat research, row 13).

### 8. Communications ducking off: **situational** (comfort)

- **What it changes:** Sound Control Panel > Communications tab > "Do nothing". This is `HKCU\Software\Microsoft\Multimedia\Audio` `UserDuckingPreference` (DWORD): 0 mute other sounds, 1 reduce by 80% (the default), 2 reduce by 50%, 3 do nothing ([Wisechecker](https://wisechecker.com/disable-communications-ducking-globally-windows-11/), [Winhance docs](https://winhance.net/docs/features/optimizations/sound.html) **(community)**).
- **Evidence:** when Windows detects a "communications" stream (voice chat), it turns other sounds down, game audio included. Users ask Microsoft to fix the tab ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/3859036/when-will-the-communications-setting-in-control-pa) **(community, title only)**). This is about hearing the game, not FPS.
- **Undo:** restore the saved value. **Instant?** Yes, for the next voice stream **(inference)**. **Fits:** anyone using Discord or in-game voice. **Anti-cheat:** none.

## Possible placebo (secondary section)

### 9. USB selective suspend off: **possible placebo**

- **What it changes:** Power Options > USB settings > "USB selective suspend setting" = Disabled. Power setting subgroup `2a737441-1930-4402-8d77-b2bebba308a3`, setting `48e6b7a6-50f5-4782-a5d4-53bb8f07e226`, 0 = disabled, 1 = enabled ([ElevenForum](https://elevenforum.com/t/enable-or-disable-usb-selective-suspend-in-windows-11.10251/) **(community)**).
- **Evidence:** selective suspend lets Windows suspend one idle USB device while others stay on ([Microsoft Learn](https://learn.microsoft.com/windows-hardware/drivers/usbcon/usb-selective-suspend)). A mouse or controller in active use is not idle, so suspend should not touch it mid-game **(inference)**. I found no measured input-lag or FPS gain. The real use is fixing devices that drop out or reconnect, such as some USB headsets and DACs (helpdeskgeek, [Help Desk Geek](https://helpdeskgeek.com/what-is-usb-selective-suspend-on-windows-10-and-how-to-disable-it/) **(community, title only)**).
- **Undo:** restore the saved AC and DC indexes. **Instant?** Yes (`powercfg /setacvalueindex` then `/setactive`). **Fits:** AC only on laptops, to save battery **(inference)**. **Anti-cheat:** none.

### 10. PCIe link-state power management (ASPM) off: **possible placebo**

- **What it changes:** Power Options > PCI Express > Link State Power Management: Off, Moderate power savings or Maximum power savings. Subgroup `501a4d13-42af-4429-9fd1-a8218c268e20`, setting `ee12f906-d277-404b-b6da-e5fa1a576df5` ([Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/pci-express-settings)).
- **Evidence:** it lets PCIe links (GPU, NVMe, Wi-Fi) drop into low-power states when idle. Waking costs microseconds. A GPU busy rendering a game keeps its link active **(inference)**. I found no reputable gaming test. A 2026 Japanese explainer covers the stutter claims ([gaming-st.com](https://gaming-st.com/trouble-settings/pcie-link-state-power-management-aspm-gaming-2026/) **(community, title only)**). Windows' High performance plan already sets it Off **(inference; check with `powercfg /q`)**, so v1 users on that plan have this already.
- **Undo:** restore the saved AC and DC indexes. **Instant?** Yes **(inference)**. **Fits:** desktops on Balanced; not laptops on battery. **Anti-cheat:** none.

### 11. Power throttling off for the whole PC: **possible placebo**

- **What it changes:** `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling` `PowerThrottlingOff` (DWORD) = 1, then restart ([Winaero](https://winaero.com/disable-power-throttling-windows-10-1709) **(community, title only)**). Microsoft documents the slider and per-app opt-outs, not this value.
- **Evidence:** a focused game already runs at High QoS. Low QoS only changes clocks and cores on battery. Best performance mode (#4) already opts every app out (Microsoft Learn pages in #4 and #5). So on a desktop, this mainly stops apps that tag themselves Eco (such as Task Manager's Efficiency mode) from saving power **(inference)**. There is no measured gaming gain.
- **Undo:** delete the value; restart. **Fits:** nobody by default; prefer #4. **Anti-cheat:** none.

### 12. Pin games to P-cores on Intel hybrid CPUs: **possible placebo**

- **What it changes:** affinity or CPU sets limited to P-cores, as in #7.
- **Evidence:**
  - Windows 11 already gives the focused app High QoS and sends only Low and Eco work to efficient cores (Microsoft Learn QoS page, #5). So a focused game already favours P-cores **(inference)**. I could not read an E-cores-off test within this session's budget (gap).
  - Intel's own fix is its free **Application Optimization (APO)** app. It uses per-game profiles and claims up to 14% in listed titles, now 26+ games on 14th gen and Core Ultra 200S ([Tom's Hardware, Sept 2025](https://www.tomshardware.com/pc-components/cpus/intel-apo-update-boosts-gaming-performance-by-up-to-14-percent-in-select-titles-15-new-games-added-to-the-support-list), [Wccftech](https://wccftech.com/intel-apo-supports-26-games-faster-performance-core-ultra-200s-14th-gen-cpus/) **(community, title only)**). The app could point Intel owners to APO rather than pin cores itself **(inference)**.
  - Arrow Lake's weak launch results were traced to firmware and Windows updates, not settings ([Intel field update, Dec 2024](https://community.intel.com/t5/Blogs/Tech-Innovation/Client/Field-Update-1-of-2-Intel-Core-Ultra-200S-Series-Performance/post/1651305) **(title only)**; [TechPowerUp](https://techpowerup.com/329990/intel-reports-findings-on-missing-arrow-lake-performance-targets-jan-2025-for-0x114-microcode?cp=2) **(community, title only)**).
- **Undo:** clear. **Instant?** Yes. **Fits:** 12th–14th gen Core, Core Ultra 200S/200HX. **Anti-cheat:** as #7.

### 13. Audio enhancements off and other audio "latency" settings: **possible placebo** (for FPS and latency)

- **What it changes:** Settings > System > Sound > device > Audio enhancements: Off. Also "exclusive mode", and output format or sample-rate changes.
- **Evidence:** no source shows an FPS or input-latency gain. "Audio enhancements" are the device's effect plug-ins. Microsoft Q&A explains how they differ from "Enhance audio" ([Q&A](https://learn.microsoft.com/en-us/answers/questions/2028149/what-is-enhance-audio-vs-enable-audio-enhancements) **(community, title only)**). Effect plug-ins can break sound outright: in 24H2, Dirac Audio's `cridspapo.dll` cut audio output until a driver fix (Microsoft release health, resolved 2025-09-22, safeguard 54283088: [Microsoft Learn](https://learn.microsoft.com/en-gb/windows/release-health/resolved-issues-windows-11-24h2)). So "off" is a fix for broken sound, not a speed tweak.
- **Undo:** restore per device. The per-device registry storage is messy, so the app should open the Settings page rather than write it **(inference)**. **Anti-cheat:** none.

### 14. Memory compression off: **possible placebo**

- **What it changes:** `Disable-MMAgent -MemoryCompression` (PowerShell, elevated), then restart **(community)**: [ElevenForum](https://www.elevenforum.com/t/enable-or-disable-memory-compression-in-windows-10-and-windows-11.3555/latest).
- **Evidence:** compression keeps more pages in RAM instead of the page file. Turning it off trades CPU time for more disk paging when RAM runs short **(inference)**. A 2026 hands-on test found "the numbers told a different story" ([TweakTown](https://www.tweaktown.com/guides/11644/i-turned-off-memory-compression-on-my-pc-and-the-numbers-told-a-different-story/index.html) **(community, title only)**). There is no reputable gain.
- **Undo:** `Enable-MMAgent -MemoryCompression`; restart. **Fits:** nobody by default; riskiest on 8–16 GB PCs **(inference)**. **Anti-cheat:** none.

### 15. Spectre/Meltdown mitigations off: **possible placebo**, security risk (recommend not shipping)

- **What it changes:** `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management` `FeatureSettingsOverride` = 3 and `FeatureSettingsOverrideMask` = 3, then restart. These are Microsoft's KB4073119 values ([KB mirror](https://mskb.pkisolutions.com/kb/4073119) **(community mirror)**). InSpectre is the usual tool.
- **Evidence:** Microsoft cut the Spectre v2 cost with retpoline in 2018 ([Microsoft Tech Community](https://techcommunity.microsoft.com/blog/windowsosplatform/mitigating-spectre-variant-2-with-retpoline-on-windows/295618) **(title only)**). Gaming gain claims are forum-only and mostly on older CPUs ([TechPowerUp forum](https://www.techpowerup.com/forums/threads/spectre-meltdown-mitigation-management-performance-impact-on-windows-11.287847/post-4627751), [AnandTech forum](https://forums.anandtech.com/threads/spectre-fix-nerfing-older-systems-how-to-fix-the-fix-is-a-fix-even-necessary.2570649/post-39933088) **(community)**). Newer CPUs fix most of these in hardware **(inference)**.
- **Risk:** it re-opens known side-channel attacks to any code running on the PC.
- **Anti-cheat:** none documented. BattlEye's "Windows kernel modification detected" covers software that "disables certain important security features" (anti-cheat research, row 17). Whether that includes this is unknown.
- **Undo:** delete both values; restart.

### 16. Control Flow Guard / exploit protection off: **possible placebo**, anti-cheat conflict (recommend not shipping)

- **What it changes:** Windows Security > App & browser control > Exploit protection, system-wide or per .exe (CFG, DEP and others).
- **Evidence:** I found no first-party or measured gaming gain. Riot says error **VAN 9002** is fixed by turning exploit protection back on ([Riot: VAN 9002](https://support.riotgames.com/en-us/riot/client/error-van-9002), [Riot: enable exploit protection](https://support.riotgames.com/valorant/support-tools/how-to-enable-exploit-protection-and-prevent-error-code-van9002) **(title only)**).
- **Anti-cheat:** **Vanguard won't launch** with it off. Per ADR 0005 this would be a warning, not a block. But with no benefit and a security cost, I recommend leaving it out.

## Not tweaks: what's new for gaming in 24H2, 25H2 and 2026

- **Xbox mode** (previously "Xbox full screen experience"): first on the ROG Xbox Ally (2025). Since 2026-04-30 it has been rolling out to Windows 11 "laptops, desktops and tablets". It "brings your library and recent games forward while reducing background distractions" ([Windows blog](https://blogs.windows.com/windowsexperience/2026/04/30/windows-11-pc-gamers-xbox-mode-rolls-out-and-rog-xbox-ally-updates-include-auto-sr-preview/)). Microsoft gives no memory numbers. ASUS sells it as reducing RAM use ([ASUS ROG](https://rog.asus.com/me-en/articles/guides/how-to-enable-xbox-mode-to-reduce-ram-usage-when-gaming-on-windows/) **(title only)**). One report claims about 400 MB of RAM and 15% CPU load saved ([Borncity](https://borncity.com/news/windows-11-xbox-modus-spart-400-mb-ram-und-15-cpu-last/) **(community, title only)**). HotHardware tested 15 games on the Xbox Ally X ([HotHardware](https://hothardware.com/news/xboxs-new-full-screen-gaming-mode) **(community, not read)**). It shipped in KB5083631 ([Windows Latest](https://www.windowslatest.com/2026/05/01/windows-11-kb5083631-out-with-xbox-mode-faster-performance-direct-download-links-msu/) **(title only)**). I found no documented switch the app could set. Show it as advice ("on a handheld or low-RAM PC, try Xbox mode") rather than a tweak **(inference)**.
- **Auto SR** (Automatic Super Resolution): this is an NPU upscaler. It is in preview on the ROG Xbox Ally X, set per game in Game Bar (same Windows blog). Before that it was on Snapdragon Copilot+ PCs only. It does not fit NVIDIA, AMD or Intel desktops today.
- **Advanced Shader Delivery:** shaders precompiled and downloaded with the game, to remove first-run compile stutter. It shipped in Agility SDK 1.618 (2025-09-25), went to a Windows PC preview at GDC 2026, and expanded with AMD on 2026-05-15 ([DirectX blog: Agility SDK 1.618](https://devblogs.microsoft.com/directx/agility-sdk-1-618/), [Microsoft Game Dev: GDC 2026](https://developer.microsoft.com/en-us/games/articles/2026/03/gdc-2026-advanced-shader-delivery-on-windows/), [DirectX blog](https://devblogs.microsoft.com/directx/advanced-shader-delivery-expands-public-preview-with-amd/) **(title only)**). Games and stores adopt it; users have nothing to switch.
- **Shader cache size:** I found no Windows setting for it **(inference from not finding one)**. The driver panels have size controls (NVIDIA "Shader Cache Size"), which are the driver-panel ticket's subject. v1 already leaves shader caches unticked in Clean up and never cleans them weekly (`src/Elynt.Core/Cleanup/CleanupCatalogue.cs`, `src/Elynt.Service/WeeklyCleanupService.cs`).
- **24H2 fixes that matter to gamers:** older Easy Anti-Cheat drivers (before April 2024) caused freezes and blue screens on Intel Alder Lake+ and vPro PCs. 24H2 was held back (safeguard 52325539) until 2025-07-24 ([Microsoft release health](https://learn.microsoft.com/en-gb/windows/release-health/resolved-issues-windows-11-24h2)). The Auto HDR crash is covered in #3.
- **Older, still relevant:** the July 2023 update reduced stutter with high-polling-rate mice ([Custom PC](https://www.custompc.com/microsoft/windows-11-update-july-2023-game-stutter), [Windows Central](https://www.windowscentral.com/gaming/pc-gaming/the-latest-windows-11-cumulative-update-has-a-hidden-change-beneficial-for-gamers) **(community, title only)**). It is a reason to keep Windows updated, not a setting.
- **2026 direction:** in December 2025 Microsoft promised gaming work on "background workloads, power and scheduling, graphics stack and drivers" ([Tom's Hardware](https://www.tomshardware.com/software/windows/microsoft-promises-to-make-windows-11-the-best-operating-system-for-gaming-says-it-will-focus-on-background-workloads-power-and-scheduling-graphics-stack-and-drivers) **(title only)**). KB5083631 (April 2026) added CPU-burst responsiveness work ([Windows Central](https://www.windowscentral.com/microsoft/windows-11/microsoft-is-working-on-major-performance-boost-for-windows-11-that-will-speed-up-app-launches-and-common-actions-by-automatically-maxing-out-cpu-in-short-bursts) **(title only)**). Expect Windows itself to take over some of what tweaks did. The drift check should re-test these tweaks on each feature update **(inference)**.
- **Left to other tickets:** a monitor left at 60 Hz, single-channel RAM or a narrow PCIe link (hidden-loss detection); the NVIDIA and AMD panels (driver-panel settings); network latency (online play); before and after numbers (measuring).

## Checks before building

1. On a 25H2 test PC, confirm the `DirectXUserGlobalSettings` item names (`VRROptimizeEnable`, `AutoHDREnable`) and the `GpuPreference` numbers. Use the Settings toggles and watch the registry.
2. Confirm that `DisableOverlays` turns MPO off on 24H2 and 25H2 (dxdiag MaxPlanes) and whether it breaks VRR. Confirm that `OverlayTestMode` is ignored.
3. Find a supported way to set the power-mode overlay (`powercfg /overlaysetactive` on 24H2 and 25H2). Check whether Best performance changes core parking on a dual-CCD X3D.
4. Test whether `DisableUserPresenceQos` applies without a restart, and whether controller input counts as user presence.
5. Measure X3D pinning against working Game Bar parking, and P-core pinning on Intel hybrid, with PresentMon (measuring ticket). Read the r/hardware and HotHardware tests above in full.
6. Check that CPU sets can be set on games protected by EAC, BattlEye, Vanguard and FACEIT without errors.

## Sources

Microsoft (first party)
- Microsoft Learn, DXGI_GPU_PREFERENCE: https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_6/ne-dxgi1_6-dxgi_gpu_preference
- DirectX blog, Navigating the redesigned Graphics settings page (2023-01-19): https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page
- Microsoft Support, Optimizations for windowed games: https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952
- Microsoft Learn, Customize the Windows performance power slider: https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/customize-power-slider
- Microsoft Learn, Quality of Service (updated 2025-12-09): https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service
- Windows blog, Introducing Power Throttling (2017-04-18): https://blogs.windows.com/windowsexperience/2017/04/18/introducing-power-throttling/
- Microsoft Learn, Multiplane overlay support: https://learn.microsoft.com/en-us/windows-hardware/drivers/display/multiplane-overlay-support
- Microsoft Learn, CPUSets for game development: https://learn.microsoft.com/en-us/windows/uwp/xbox-apps/cpusets-games
- Microsoft Learn, GetLogicalProcessorInformationEx: https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex
- Microsoft Learn, USB selective suspend: https://learn.microsoft.com/windows-hardware/drivers/usbcon/usb-selective-suspend
- Microsoft Learn, PCI Express settings: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/pci-express-settings
- Microsoft release health, resolved issues in 24H2: https://learn.microsoft.com/en-gb/windows/release-health/resolved-issues-windows-11-24h2
- Windows blog, Xbox mode rolls out (2026-04-30): https://blogs.windows.com/windowsexperience/2026/04/30/windows-11-pc-gamers-xbox-mode-rolls-out-and-rog-xbox-ally-updates-include-auto-sr-preview/
- DirectX blog, Agility SDK 1.618 (2025-09-25): https://devblogs.microsoft.com/directx/agility-sdk-1-618/
- Microsoft Game Dev, GDC 2026: Advanced Shader Delivery on Windows: https://developer.microsoft.com/en-us/games/articles/2026/03/gdc-2026-advanced-shader-delivery-on-windows/
- DirectX blog, Advanced Shader Delivery expands public preview with AMD (2026-05-15): https://devblogs.microsoft.com/directx/advanced-shader-delivery-expands-public-preview-with-amd/
- Microsoft Tech Community, Mitigating Spectre variant 2 with Retpoline: https://techcommunity.microsoft.com/blog/windowsosplatform/mitigating-spectre-variant-2-with-retpoline-on-windows/295618
- Microsoft Q&A (community answers on Microsoft's site): https://learn.microsoft.com/en-us/answers/a/6800271 ; https://learn.microsoft.com/en-us/answers/questions/3919778/changing-overlay-power-mode-with-script ; https://learn.microsoft.com/en-us/answers/questions/3859036/when-will-the-communications-setting-in-control-pa ; https://learn.microsoft.com/en-us/answers/questions/2028149/what-is-enhance-audio-vs-enable-audio-enhancements

Vendors and anti-cheat (first party)
- NVIDIA Control Panel help, Setting the preferred graphics processor: https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nv3d/Setting_the_preferred_graphics_processor.htm
- NVIDIA KB 5157, multiplane overlay (blocked from automated reading): https://nvidia.custhelp.com/app/answers/detail/a_id/5157
- Intel, Field Update 1 of 2: Core Ultra 200S performance status: https://community.intel.com/t5/Blogs/Tech-Innovation/Client/Field-Update-1-of-2-Intel-Core-Ultra-200S-Series-Performance/post/1651305
- ASUS ROG, How to enable Xbox mode to reduce RAM usage: https://rog.asus.com/me-en/articles/guides/how-to-enable-xbox-mode-to-reduce-ram-usage-when-gaming-on-windows/
- Riot, Error VAN 9002: https://support.riotgames.com/en-us/riot/client/error-van-9002 ; https://support.riotgames.com/valorant/support-tools/how-to-enable-exploit-protection-and-prevent-error-code-van9002

Community and press (secondary)
- ElevenForum, GPU preference: https://www.elevenforum.com/t/change-graphics-performance-preference-for-apps-in-windows-11.5675/latest
- ElevenForum, VRR switch: https://www.elevenforum.com/t/enable-or-disable-variable-refresh-rate-for-games-in-windows-11.12052
- ElevenForum, USB selective suspend: https://elevenforum.com/t/enable-or-disable-usb-selective-suspend-in-windows-11.10251/
- ElevenForum, memory compression: https://www.elevenforum.com/t/enable-or-disable-memory-compression-in-windows-10-and-windows-11.3555/latest
- Guru3D, Disabling MPO in 2025: https://forums.guru3d.com/goto/post?id=6305178
- Neowin, MPO display issues: https://www.neowin.net/amp/microsoft-amd-nvidia-are-all-sleeping-on-windows-11-mpo-display-issues/
- Unreal Engine forum, NVIDIA MPO fix (2021): https://forums.unrealengine.com/t/dropdown-menus-disappear-instantly/478497/9
- Tom's Hardware, VRR for DX11 games (2019): https://www.tomshardware.com/news/windows-10-variable-refresh-rate-vrr-dx11,39582.html
- Neowin, 24H2 blocked over gaming issues: https://www.neowin.net/news/microsoft-blocks-windows-11-24h2-update-on-more-pcs-due-to-new-gaming-issues/
- The Register, 24H2 issues (2024-12-19): https://www.theregister.com/2024/12/19/windows_11_24h2_issues/
- PCWorld, Auto HDR works again: https://www.pcworld.com/article/2593590/windows-11s-auto-hdr-works-again-but-you-have-to-manually-update-first.html
- OC3D, Auto HDR fixed: https://overclock3d.net/news/software/microsoft-finally-fixes-windows-11-24h2s-auto-hdr-bugs/
- Hardware Busters, 7950X3D core parking: https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/
- Tom's Hardware, AMD Ryzen 7000X3D install guide: https://www.tomshardware.com/news/amd-ryzen-7000x3d-install-guide
- Neowin, Game Bar and Ryzen X3D (Windows 10): https://www.neowin.net/news/report-microsoft-quietly-disables-vital-windows-feature-crippling-many-amd-ryzen-cpus/
- TechRadar, Game Bar crashing with X3D on Windows 10: https://www.techradar.com/computing/windows/report-suggests-windows-10s-game-bar-is-crashing-with-some-high-end-amd-ryzen-cpus-and-i-hope-microsoft-will-investigate-soon
- EA forum, pinning Battlefield 6 to CCD0: https://forums.ea.com/discussions/battlefield-6-technical-issues-en/9950x3d-core-parking/12741808/replies/12763769
- r/hardware, 9950X3D Process Lasso vs Game Bar: https://reddit.birdcat.cafe/r/hardware/comments/1jgyu9k/9950x3d_benchmarked_with_process_lasso_vs_game
- Tom's Hardware, Intel APO up to 14%: https://www.tomshardware.com/pc-components/cpus/intel-apo-update-boosts-gaming-performance-by-up-to-14-percent-in-select-titles-15-new-games-added-to-the-support-list
- Wccftech, Intel APO in 26 games: https://wccftech.com/intel-apo-supports-26-games-faster-performance-core-ultra-200s-14th-gen-cpus/
- TechPowerUp, Arrow Lake findings: https://techpowerup.com/329990/intel-reports-findings-on-missing-arrow-lake-performance-targets-jan-2025-for-0x114-microcode?cp=2
- Wisechecker, communications ducking: https://wisechecker.com/disable-communications-ducking-globally-windows-11/
- Winhance docs, sound settings: https://winhance.net/docs/features/optimizations/sound.html
- Help Desk Geek, USB selective suspend: https://helpdeskgeek.com/what-is-usb-selective-suspend-on-windows-10-and-how-to-disable-it/
- gaming-st.com, PCIe ASPM and games (2026, Japanese): https://gaming-st.com/trouble-settings/pcie-link-state-power-management-aspm-gaming-2026/
- Winaero, disable power throttling: https://winaero.com/disable-power-throttling-windows-10-1709
- TweakTown, memory compression test: https://www.tweaktown.com/guides/11644/i-turned-off-memory-compression-on-my-pc-and-the-numbers-told-a-different-story/index.html
- KB4073119 mirror: https://mskb.pkisolutions.com/kb/4073119
- TechPowerUp forum, Spectre mitigations on Windows 11: https://www.techpowerup.com/forums/threads/spectre-meltdown-mitigation-management-performance-impact-on-windows-11.287847/post-4627751
- AnandTech forum, Spectre fix on older systems: https://forums.anandtech.com/threads/spectre-fix-nerfing-older-systems-how-to-fix-the-fix-is-a-fix-even-necessary.2570649/post-39933088
- Borncity, Xbox mode savings: https://borncity.com/news/windows-11-xbox-modus-spart-400-mb-ram-und-15-cpu-last/
- HotHardware, 15 games in Xbox full screen mode: https://hothardware.com/news/xboxs-new-full-screen-gaming-mode
- Windows Latest, KB5083631 with Xbox mode: https://www.windowslatest.com/2026/05/01/windows-11-kb5083631-out-with-xbox-mode-faster-performance-direct-download-links-msu/
- Custom PC, July 2023 stutter fix: https://www.custompc.com/microsoft/windows-11-update-july-2023-game-stutter
- Windows Central, cumulative update change for gamers: https://www.windowscentral.com/gaming/pc-gaming/the-latest-windows-11-cumulative-update-has-a-hidden-change-beneficial-for-gamers
- Tom's Hardware, Microsoft's 2026 gaming promises: https://www.tomshardware.com/software/windows/microsoft-promises-to-make-windows-11-the-best-operating-system-for-gaming-says-it-will-focus-on-background-workloads-power-and-scheduling-graphics-stack-and-drivers
- Windows Central, CPU bursts for responsiveness: https://www.windowscentral.com/microsoft/windows-11/microsoft-is-working-on-major-performance-boost-for-windows-11-that-will-speed-up-app-launches-and-common-actions-by-automatically-maxing-out-cpu-in-short-bursts
