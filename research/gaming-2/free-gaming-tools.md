# Survey free gaming tools for features we lack

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/87

Researched 2026-10-30 from vendor product pages, release notes, SDK docs and project READMEs, compared against the code on `main` and the earlier notes in `research/` on branch `claude/project-thread-5ggkan`. **(community)** marks a secondary or community source. **(inference)** marks my own reasoning. **(unverified)** marks a claim whose linked page I could not open this session, because the shared web-fetch and search budgets ran out; it rests on the page title, an earlier note, or prior knowledge, and should be checked before we build on it.

## Answer

- **The biggest gap is GPU driver settings (area B).** Every vendor app lets the user cap the frame rate, turn on a low-latency mode, set V-Sync and VRR, and override upscaling or frame generation, per game or for all games. v1 has none of these. The SDKs we already ship can reach most of them: AMD's ADLX has calls for Anti-Lag, Chill, Boost, Frame Rate Target Control, Radeon Super Resolution, Enhanced Sync, V-Sync, shader cache reset, Fluid Motion Frames and FSR upgrade. NVIDIA's public NVAPI covers driver profiles, and Intel's IGCL has 3D controls. They fit per-game profiles as temporary changes.
- **The second gap is measuring (area C).** NVIDIA App, Adrenalin, Intel Graphics Software and CapFrameX show FPS and frame times in game. CapFrameX also records a run with Intel's PresentMon, logs sensors alongside it, and compares runs (average, P1, P0.2, stutter count). v1 shows live sensors but cannot record or compare a run. Our evidence rule needs exactly this before/after proof.
- **Hidden-loss checks (area D):** LatencyMon finds drivers that hold up the CPU (DPC and ISR latency) and hard page faults. v1 has nothing like it. Adrenalin shows and switches Smart Access Memory (Resizable BAR); v1's BIOS readouts cover only the memory profile and PBO.
- **Tweaks (area A):** Process Lasso adds per-game CPU affinity or CPU sets, Efficiency mode control, I/O priority and ProBalance. Hone, AtlasOS and ReviOS add device power saving off, interrupt (device) affinity, CPU security mitigations off and memory-manager switches. Only CPU sets/affinity and Efficiency mode have a clear mechanism, and both are situational. The rest have no published measurements and belong in possible placebo. Turning off the Sticky Keys shortcut is a small, safe fix for a real in-game interruption.
- **Online play (area E):** the free tools offer a DNS changer with a ping test, a hosts file editor, IPv6 and Teredo switches, and Hone's network bundle (DSCP tags, congestion provider, TCP and adapter settings). None publishes evidence of lower in-game ping. Hone itself says the ISP and the game server "still affect your ping".
- **Anti-cheat:** driver-level settings and vendor overlays have no documented conflicts. Features that hook into the game do: AMD's Anti-Lag+ got Counter-Strike 2 players VAC-banned in October 2023, and BattlEye refuses to start next to injected graphics mods such as Special K. So we should build only driver-level or event-tracing features, never injection.
- **Do not copy:** one-click bundles (HYPR-RX, Hone's merged groups and game presets, NVIDIA's "Optimize") clash with ADR 0006 (no presets) unless we split them into separate switches. Global fullscreen-optimizations off, removing Xbox/Game Bar, and turning Teredo off clash with the earlier gaming tweak catalogue.
- **Other, lower priority:** instant replay and recording, RTX HDR and image filters, Special K's single-player mods, AI assistants, and Intel's shared GPU memory override for laptops with integrated graphics.
- **v1 already matches or beats these tools** on timer resolution, power plans, Game Mode and HAGS, per-game priority and closing apps, GPU tuning with confirm-or-revert, vendor auto-tuning on AMD and Intel, driver installs and cleaning, shader cache cleanup, BIOS guides, and undo.

## What v1 already has

These were left out of the "missing" list because v1 on `main` already does them.

| Feature in the free tools | Which tools | Where it is in v1 |
|---|---|---|
| Timer resolution | Hone, AtlasOS | `gaming.global-timer-resolution` tweak; timer resolution in per-game profiles (`GameProfile.RaiseTimerResolution`) |
| High or Ultimate Performance power plan, per-game power plan | WinUtil, Hone, Process Lasso (Performance Mode, Bitsum Highest Performance) | `gaming.high-performance-plan`, `gaming.ultimate-performance-plan`; both can go in a per-game profile |
| Game priority while the game runs | Process Lasso (persistent priorities), Hone game mode | `GameProfile.Priority` (Normal, Above normal, High) |
| Close background apps while gaming | Hone game mode | `GameProfile.AppsToClose` |
| Game Mode, HAGS, windowed-game optimizations, mouse acceleration off | WinUtil, Hone, AtlasOS | `gaming.*` tweaks |
| Per-game fullscreen optimizations off | WinUtil (global only) | `gaming.fullscreen-optimizations-off` (per game, on purpose) |
| MSI mode, Nagle, network throttling, MMCSS, Win32PrioritySeparation, HPET, dynamic tick, standby list cleaning, visual effects | Hone, WinUtil, AtlasOS, Process Lasso | `gaming.placebo.*` tweaks |
| Memory Integrity off, Defender off, Defender exclusion for a game folder | AtlasOS, ReviOS, hellzerg Optimizer | `security.*` tweaks |
| Telemetry, Copilot, Recall, Widgets, Game DVR background recording off; Windows Update control | WinUtil, Hone, AtlasOS, ReviOS, hellzerg Optimizer | `debloat.*`, `privacy.*`, `update.*` tweaks |
| Services, startup items | WinUtil, Hone, AtlasOS, hellzerg Optimizer | Startup & Services page |
| Lock the GPU at its top boost clock | Hone ("Lock NVIDIA GPU to max boost clock") | "Locked min GPU clock" control in GPU tuning (`NvidiaGpuTuning.cs`) |
| GPU overclock, undervolt, fan, power limit; one-click auto-tune | NVIDIA App, Adrenalin, Intel Graphics Software | Overclocking page through NVML, ADLX and IGCL; auto-tune for AMD and Intel (NVIDIA's has no API) |
| Driver updates, clean installs, NVIDIA telemetry left out | NVIDIA App, Adrenalin, Hone | Drivers page, DDU, lean install |
| Scheduled cleanup, shader cache cleanup | Hone Boost-Ups, Adrenalin | Cleanup page, `WeeklyCleanupService` |
| BIOS settings | Hone (Premium "set everything to Hone recommended") | BIOS guides and readouts; the app never changes the BIOS |
| Game library | Adrenalin, NVIDIA App | Games page |
| Live temperatures and clocks | All vendor apps, CapFrameX | Sensors panel, Home page |

## Missing features at a glance

Suggested tiers use our evidence tiers. "None documented" means no first-party source names a conflict.

| # | Missing feature | Tools that have it | Suggested tier | Anti-cheat risk | Area |
|---|---|---|---|---|---|
| B1 | Frame rate cap (per game, all games, background apps) | NVIDIA App, Adrenalin (FRTC, Chill), Intel Graphics Software (Endurance Gaming), Special K | Situational | None documented for driver caps; injected caps risky | B |
| B2 | Driver low-latency mode | NVIDIA App (Low Latency Mode), Adrenalin (Anti-Lag) | Situational (GPU-bound only) | None for driver-level; Anti-Lag+ caused VAC bans | B |
| B3 | Upscaling and frame generation overrides | NVIDIA App (DLSS Override, Smooth Motion), Adrenalin (RSR, AFMF, Boost, FSR upgrade) | Situational | None documented | B |
| B4 | V-Sync, VRR and sync modes | NVIDIA App, Adrenalin (FreeSync, Enhanced Sync), Intel (Smooth Sync, frame delivery) | Situational | None | B |
| B5 | Other driver settings (shader cache, filtering, anti-aliasing, sharpening, power mode) | NVIDIA App, Adrenalin | Mostly image quality; power mode possible placebo | None | B |
| C1 | Record a run and compare before/after | CapFrameX, Adrenalin (unverified), NVIDIA FrameView (unverified) | The measurement itself | None for event-tracing capture | C |
| C2 | In-game FPS and frame-time overlay | NVIDIA App, Adrenalin, Intel Graphics Software, CapFrameX, Special K | The measurement itself | Vendor overlays none; hooking overlays unknown | C |
| C3 | Latency readout (PC latency) | NVIDIA App (Reflex stats and Reflex Analyzer), CapFrameX (beta) | The measurement itself | None | C |
| C4 | Sensor log recorded with the run | CapFrameX | The measurement itself | None | C |
| D1 | DPC/ISR latency and hard page fault check | LatencyMon | Situational (finds faults on some PCs) | None (runs outside games) | D |
| D2 | Resizable BAR / Smart Access Memory state | Adrenalin | Situational | None | D |
| A1 | Per-game CPU affinity or CPU sets | Process Lasso | Situational | None documented | A |
| A2 | Efficiency mode (EcoQoS) control | Process Lasso | Situational | None | A |
| A3 | ProBalance (automatic background restraint) | Process Lasso | Possible placebo for games | None | A |
| A4 | I/O and memory priority per game | Process Lasso, Hone | Possible placebo | None documented | A |
| A5 | Device power saving off (USB, PCIe, network adapter) | Hone, AtlasOS | Possible placebo | None | A |
| A6 | Interrupt (device) affinity, MSI on all devices | Hone | Possible placebo | None documented | A |
| A7 | CPU security mitigations off (Spectre, Meltdown) | hellzerg Optimizer, AtlasOS, ReviOS (unverified) | Possible placebo on current CPUs; security risk | None documented | A |
| A8 | Memory compression, SysMain, Fault Tolerant Heap, service grouping | ReviOS, Hone | Possible placebo | None | A |
| A9 | Sticky Keys and Filter Keys shortcuts off | WinUtil, hellzerg Optimizer, AtlasOS (unverified) | Proven fix for an interruption; no FPS effect | None | A |
| E1 | DNS changer with ping test | hellzerg Optimizer, WinUtil | Possible placebo for ping | None | E |
| E2 | Hosts file editor | hellzerg Optimizer (unverified) | Not a performance feature | None documented | E |
| E3 | Network bundle (DSCP, congestion provider, TCP, adapter) | Hone | Possible placebo | None documented | E |
| E4 | IPv6 off, Teredo off, prefer IPv4 | WinUtil | Possible placebo; can break Xbox features | None | E |
| O1 | Instant replay and recording | NVIDIA App, Adrenalin | Not a performance feature | None for vendor capture | Other |
| O2 | HDR and image filters | NVIDIA App (RTX HDR, Dynamic Vibrance, Freestyle), Adrenalin (Image Sharpening 2, Integer Scaling), Special K | Not a performance feature | Special K injection risky | Other |
| O3 | One-click game settings | NVIDIA App ("Optimize"), Hone (game presets) | Not a tweak; clashes with no presets | None | Other |
| O4 | Shared GPU memory override for integrated graphics | Intel Graphics Software | Situational (laptops with Intel integrated graphics) | None | Other |

## B. GPU driver settings

### B1. Frame rate cap

- **Tools:** NVIDIA App has Max Frame Rate, and since August 2025 also "Background Application Max Frame Rate" among the legacy settings it took over from the NVIDIA Control Panel ([NVIDIA, 2025-08-18](https://nvidia.com/en-us/geforce/news/nvidia-app-global-dlss-overrides-rtx-40-series-smooth-motion)). The Max Frame Rate name in the app is (unverified). Adrenalin has Frame Rate Target Control, which lets users "set a target maximum frame rate", and Radeon Chill, which sets the frame rate "based on in-game movements" ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). Intel Graphics Software has Endurance Gaming on some laptops, capping at 60, 40 or 30 FPS to save battery ([Intel](https://www.intel.com/content/www/us/en/support/articles/000097679.html)). Special K has its own limiter and a "Latent Sync" mode ([Blur Busters forum](https://forums.blurbusters.com/viewtopic.php?p=74923) **(community)**).
- **How it works:** the driver holds back each frame so the game never runs above the target.
- **Evidence:** AMD says FRTC "can reduce GPU power consumption" ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). Intel uses caps to save battery ([Intel](https://www.intel.com/content/www/us/en/support/articles/000097679.html)). Players also cap just under the monitor's refresh rate so VRR stays in range and frame pacing is steadier ([Blur Busters forum](https://forums.blurbusters.com/viewtopic.php?p=74923) **(community)**). Suggested tier: situational.
- **Anti-cheat risk:** none documented for driver caps (inference: the cap runs in the driver, not in the game). Caps that inject into the game, like Special K's, carry the BattlEye risk in the anti-cheat section below.
- **How it would land:** a per-game profile setting, applied as a temporary change. AMD: ADLX `GetFrameRateTargetControl` and `GetChill` ([GPUOpen ADLX reference](https://gpuopen.com/manuals/adlx/adlx-sdk-references/adlx-interfaces/3d-graphics/iadlx3dsettingsservices)). NVIDIA: public NVAPI driver-settings (DRS) profiles ([NVIDIA NVAPI](https://developer.nvidia.com/nvapi), via the earlier tech-stack note). Intel: IGCL 3D controls ([IGCL repo](https://github.com/intel/drivers.gpu.control-library)); the exact frame-limit control is (unverified).

### B2. Driver low-latency mode

- **Tools:** Adrenalin's Radeon Anti-Lag "reduces lag by intelligently controlling the queuing of CPU work" in DirectX 9, 11 and 12 games ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). NVIDIA's Low Latency Mode (Off, On, Ultra) sits in the NVIDIA App's driver settings (unverified; the pages I read only say the app shows Control Panel 3D settings "still applicable to modern games", [NVIDIA, 2024-11-12](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)). The game-built-in versions are NVIDIA Reflex, AMD Anti-Lag 2 ([GPUOpen](https://gpuopen.com/anti-lag-2/)) and Intel XeLL ([Intel XeLL guide](https://www.intel.com/content/www/us/en/developer/articles/technical/xell-developer-guide.html)). Only the game can switch those on, so they are not app features.
- **How it works:** the driver stops the CPU from preparing frames far ahead of the GPU, so fewer frames wait in a queue. It only matters when the GPU is the bottleneck. AMD's own page for Anti-Lag 2 talks about "GPU-bound scenarios" ([GPUOpen](https://gpuopen.com/anti-lag-2/)).
- **Evidence:** the vendor pages give no figures. Reviewers measured the game-built-in Reflex with NVIDIA's LDAT tool ([TechPowerUp](https://www.techpowerup.com/review/nvidia-reflex-review-test-ldat-v2/3.html) **(community)**). Suggested tier: situational.
- **Anti-cheat risk:** in October 2023 AMD's Anti-Lag+ hooked ("detoured") functions inside Counter-Strike 2. Valve wrote "DO NOT ENABLE ANTI-LAG/+; any tampering with CS code will result in a VAC ban", and AMD pulled driver 23.10.1 ([Tom's Hardware, 2023-10-13](https://www.tomshardware.com/news/amd-pulls-drivers-that-cause-counter-strike-2-bans-after-valve-roasted-them) **(community)**). Plain driver-level Anti-Lag and Low Latency Mode were not involved (inference). The app should offer only driver-level modes.
- **How it would land:** per-game profile setting. AMD: ADLX `GetAntiLag` ([GPUOpen ADLX reference](https://gpuopen.com/manuals/adlx/adlx-sdk-references/adlx-interfaces/3d-graphics/iadlx3dsettingsservices)). NVIDIA: NVAPI DRS (unverified which setting ID).

### B3. Upscaling and frame generation overrides

- **Tools:**
  - NVIDIA App: DLSS Override puts newer DLSS models, Multi Frame Generation and DLAA into games that shipped without them ([NVIDIA, 2025-01-30](https://www.nvidia.com/en-us/geforce/news/nvidia-app-update-dlss-overrides-and-more/)). Since August 2025 it can be set for all games at once ([NVIDIA, 2025-08-18](https://nvidia.com/en-us/geforce/news/nvidia-app-global-dlss-overrides-rtx-40-series-smooth-motion)).
  - NVIDIA Smooth Motion is a "driver-based AI model" that adds a generated frame between two rendered frames in DirectX 11, DirectX 12 and Vulkan games. It works on RTX 50 and, since August 2025, RTX 40 ([NVIDIA](https://nvidia.com/en-us/geforce/news/nvidia-app-global-dlss-overrides-rtx-40-series-smooth-motion)).
  - Adrenalin: Radeon Super Resolution (driver upscaling), Fluid Motion Frames 2.1 (driver frame generation), Radeon Boost ("dynamically adjusts resolution during camera rotation") and FSR. HYPR-RX turns these on together ([AMD](https://www.amd.com/en/products/software/adrenalin.html), [AMD HYPR-RX](https://www.amd.com/en/products/software/adrenalin/hypr-rx.html)).
  - Intel Graphics Software: no driver frame-generation override found. Users are asking Intel for one ([Intel community](https://community.intel.com/t5/Intel-Arc-Discrete-Graphics/Intel-Graphics-Software-Needs-Urgent-Updates-XeSS-Frame-Gen/m-p/1738354) **(community)**, unverified).
- **Evidence:** vendor figures only. AMD claims "up to 2.3X" FPS from HYPR-RX with AFMF 2.1 against native resolution ([AMD HYPR-RX](https://www.amd.com/en/products/software/adrenalin/hypr-rx.html)). Generated frames make motion look smoother but don't make the game respond faster (inference). Suggested tier: situational. It needs an honest note that more FPS here is not lower latency.
- **Anti-cheat risk:** none documented for driver overrides. Tools that swap DLSS or FSR files inside the game folder could trip file-integrity checks (inference). The app should never do that.
- **How it would land:** per-game profile setting. AMD: ADLX `GetRadeonSuperResolution`, `GetBoost`, `GetAMDFluidMotionFrames`, `GetFidelityFXSuperResolution`, `GetFidelityFXFrameGenUpgrade` ([GPUOpen ADLX reference](https://gpuopen.com/manuals/adlx/adlx-sdk-references/adlx-interfaces/3d-graphics/iadlx3dsettingsservices)). NVIDIA: whether DLSS Override and Smooth Motion are reachable through public NVAPI DRS is (unverified). If they aren't, link to the NVIDIA App instead.

### B4. V-Sync, VRR and sync modes

- **Tools:** NVIDIA App has G-SYNC under System > Displays ([NVIDIA, 2024-11-12](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)); its V-Sync setting is (unverified). Adrenalin has FreeSync and Enhanced Sync ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). Intel has Smooth Sync for fixed-refresh monitors ([TechPowerUp, 2022](https://techpowerup.com/295666/intel-smooth-sync-lets-gamers-with-fixed-refresh-rate-monitors-enjoy-low-latency-gaming) **(community)**) and frame delivery settings ([Intel](https://www.intel.com/content/www/us/en/support/articles/000095281/graphics.html), unverified content).
- **How it works:** these pick how finished frames reach the screen: wait for the screen (V-Sync), let the screen wait for the frame (VRR), or show the newest frame and drop the rest (Enhanced Sync, Smooth Sync).
- **Evidence:** vendor claims only ("No stuttering. No tearing", [AMD](https://www.amd.com/en/products/software/adrenalin.html)). Suggested tier: situational.
- **Anti-cheat risk:** none.
- **How it would land:** per-game settings in B. A check that VRR is on when the monitor supports it belongs in D (hidden-loss checks). AMD: ADLX `GetEnhancedSync`, `GetWaitForVerticalRefresh`.

### B5. Other driver settings

- **Tools:** in August 2025 the NVIDIA App added anisotropic and texture filtering, FXAA, MSAA, supersampling and transparency anti-aliasing, MFAA and PhysX settings ([NVIDIA](https://nvidia.com/en-us/geforce/news/nvidia-app-global-dlss-overrides-rtx-40-series-smooth-motion)). Adrenalin has Image Sharpening 2 and Integer Scaling ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). ADLX also exposes anti-aliasing, tessellation and shader cache reset ([GPUOpen ADLX reference](https://gpuopen.com/manuals/adlx/adlx-sdk-references/adlx-interfaces/3d-graphics/iadlx3dsettingsservices)). NVIDIA's shader cache size and "Prefer maximum performance" power mode are in its driver settings (unverified).
- **Evidence:** mostly image quality, not speed. Tessellation and texture filtering overrides can cost or save a little speed in old games (inference). "Prefer maximum performance" is possible placebo, and v1 already offers a locked minimum GPU clock (inference).
- **How it would land:** low priority in B. Shader cache reset is a one-off action, so it would sit beside the existing shader cache cleanup rather than in a profile (inference).

## C. Measuring the difference

### C1. Record a run and compare before and after

- **Tools:** CapFrameX "combines Intel's PresentMon with hardware monitoring, frametime and FPS charts, and configurable overlays". It reports Average, P1, P0.2, Min, Max and AdaptiveStd, counts a stutter as a frame over 2.5 times the average, and compares runs side by side in reports. Latest release is v1.9.1 ([CapFrameX README](https://github.com/CXWorld/CapFrameX)). NVIDIA's free FrameView does similar logging ([FrameView user guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-8-user-guide-web-version.pdf), unverified content). Adrenalin's performance logging is (unverified).
- **How it works:** PresentMon reads Windows' own event tracing (ETW) for each frame the game presents, so it doesn't load anything into the game ([PresentMon repo](https://github.com/GameTechDev/PresentMon), unverified wording).
- **Evidence:** this is the measuring tool, not a tweak. The earlier catalogue already asked for "a PresentMon frametime capture" so situational and placebo tweaks could prove themselves on each PC (earlier `gaming-tweak-catalogue.md`).
- **Anti-cheat risk:** none documented. Event tracing doesn't touch the game process (inference).
- **How it would land:** C. For example, a "Measure" action on a per-game profile or a tweak: play the same spot for 60 seconds with it off and on, then show the average, 1% low and stutter count side by side. PresentMon's licence must be checked before we bundle it (unverified).

### C2. In-game overlay

- **Tools:** NVIDIA App's overlay shows "real-time FPS and performance statistics", including GPU and CPU use ([NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/)). The Alt+R overlay also shows whether DLSS overrides are active ([NVIDIA, 2025-08-19](https://www.nvidia.com/en-us/geforce/news/nvidia-app-update-5-geforce-game-ready-driver/)). Intel Graphics Software has a performance overlay, with a support article for when it doesn't appear ([Intel](https://www.intel.com/content/www/us/en/support/articles/000100253/graphics.html), unverified content). Adrenalin's metrics overlay is (unverified). CapFrameX offers its own hook-free overlay, an experimental in-game overlay, or RTSS ([CapFrameX README](https://github.com/CXWorld/CapFrameX)).
- **Anti-cheat risk:** vendor overlays have no documented conflicts. No first-party anti-cheat page names RTSS or similar hooking overlays, so treat them as unknown (earlier `anti-cheat-requirements.md`, row 12). Since 6 October 2026 Riot blocks unknown third-party apps from reading game memory ([Riot](https://support.riotgames.com/ar-ae/riot/performance/game-memory-access-removed-for-third-party-apps)).
- **How it would land:** lower priority than C1. The vendor overlays already exist, so the app could just name the hotkey for the user's GPU (inference). If we build one, it should be a separate window and never hook the game (inference).

### C3. Latency readout

- **Tools:** NVIDIA App supports the Reflex Analyzer for PC and system latency ([NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/)). Its overlay can show latency in Reflex games ([HWCooling](https://www.hwcooling.net/en/the-added-value-of-the-nvidia-app-quick-latency-insights-in-game-via-hud/) **(community)**, unverified content). CapFrameX has a "Use PC Latency" option that is "Still beta state" ([CapFrameX README](https://github.com/CXWorld/CapFrameX)).
- **Evidence:** the Reflex Analyzer needs a supported monitor and mouse. A software latency figure is an estimate (inference).
- **How it would land:** C, after C1, as an extra column where the game reports it.

### C4. Sensor log recorded with the run

- **Tools:** CapFrameX lets the user choose CPU, GPU, memory and storage sensors to record next to frame times ([CapFrameX README](https://github.com/CXWorld/CapFrameX)).
- **How it would land:** C. v1 already reads these sensors live (`Sensors.cs`). Logging them during a C1 run would show thermal or power throttling (inference).

## D. Hidden-loss checks

### D1. DPC/ISR latency and hard page fault check

- **Tools:** LatencyMon from Resplendence. It measures how long each driver's interrupt routines (ISRs) and deferred procedure calls (DPCs) take, counts hard page faults per process, and names the worst driver. It is free for home use ([Resplendence](https://www.resplendence.com/latencymon), [technical notes](https://www.resplendence.com/latencymon_technical), all unverified content). It is on winget as `Resplendence.LatencyMon` ([winget.run](https://winget.run/pkg/Resplendence/LatencyMon) **(community)**).
- **How it works:** while a driver runs a long DPC or ISR, that CPU core can't do anything else. Audio crackles and frames stutter (inference).
- **Evidence:** it is a diagnosis tool. On a healthy PC it finds nothing; on a PC with a bad Wi-Fi, audio or ACPI driver it points at the cause (inference). Audio vendors send customers to it ([Allen & Heath](https://support.allen-heath.com/hc/en-gb/articles/4403611454609-General-LatencyMon) **(community)**, unverified content). Suggested tier: situational.
- **Anti-cheat risk:** none. It runs outside the game (inference).
- **How it would land:** D. Quick option: add LatencyMon to the Apps catalogue's utilities and point to it from a stutter check. Fuller option: our own short kernel event trace of DPC and ISR times, read by the service (inference; needs its own research on Windows event tracing).

### D2. Resizable BAR / Smart Access Memory state

- **Tools:** Adrenalin links to AMD's "Smart Technologies", which include Smart Access Memory ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). The SAM on/off switch inside Adrenalin is (unverified). NVIDIA's system information shows Resizable BAR (unverified).
- **How it works:** with Resizable BAR on, the CPU can reach all of the GPU's memory at once instead of 256 MB at a time. It needs Above 4G Decoding and Resizable BAR on in the BIOS (unverified).
- **Evidence:** vendor claims of gains in some games (unverified). Suggested tier: situational.
- **Anti-cheat risk:** none.
- **How it would land:** D, as a new BIOS readout next to `bios.memory-profile` and `bios.pbo` (`BiosReport.cs`), with a BIOS guide for turning it on.

## A. Tweaks

### A1. Per-game CPU affinity or CPU sets

- **Tools:** Process Lasso applies "permanent" CPU affinities every time a process starts. It also offers CPU Sets, a "softer form of CPU affinities that work more like preferred cores" ([Bitsum](https://bitsum.com/)).
- **How it works:** affinity forbids the game from using the other cores. A CPU set only tells Windows which cores to prefer ([Microsoft CPU sets](https://learn.microsoft.com/en-us/windows/win32/procthread/cpu-sets), [Microsoft CPU sets for games](https://msdn.microsoft.com/windows/uwp/xbox-apps/cpusets-games), unverified wording).
- **Evidence:** situational. It helps when Windows puts a game on the wrong cores, for example the non-V-Cache half of a dual-CCD X3D CPU or Intel's efficient cores (inference). AMD's own fix for X3D is its driver plus Game Mode and the Balanced plan (earlier `gaming-tweak-catalogue.md`, citing [Hardware Busters](https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/) **(community)**). Hard affinity can also starve a game of cores. I found no independent measurements.
- **Anti-cheat risk:** none documented. Some anti-cheats protect the game process, so the change may just fail (inference).
- **How it would land:** A new per-game profile field next to Priority, applied as an instant change. CPU sets should be the default and hard affinity an Advanced choice (inference).

### A2. Efficiency mode (EcoQoS) control

- **Tools:** Process Lasso "controls and persists Windows 11's Efficiency Mode", which marks processes as not performance critical ([Bitsum](https://bitsum.com/)).
- **How it works:** Windows lets apps opt in to power throttling through `SetProcessInformation` with `ProcessPowerThrottling` ([Microsoft](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation), unverified wording). Windows may then run them at low clocks or on efficient cores ([Microsoft sustainable software blog](https://devblogs.microsoft.com/sustainable-software/?p=911), unverified content).
- **Evidence:** situational. It matters most on laptops and hybrid CPUs (inference). It is a softer option than closing an app: the launcher or browser keeps running, only slower (inference).
- **Anti-cheat risk:** none. It changes background apps, not the game (inference).
- **How it would land:** a per-game profile option: "slow these apps down instead of closing them". Optionally, make sure the game itself is never throttled.

### A3. ProBalance

- **Tools:** Process Lasso. ProBalance "temporarily lowers the priority of background processes that monopolize the CPU" ([Bitsum](https://bitsum.com/)).
- **Evidence:** Bitsum cites a 2015 Betanews test with its CPUEater stress demo: at 48 hog threads, Chrome took 30–40 seconds to appear without ProBalance and under 1 second with it ([Bitsum](https://bitsum.com/)). That is a stress test, not a game. v1 already raises the game's priority and closes chosen apps. Suggested tier: possible placebo for games (inference).
- **How it would land:** A or other, low priority.

### A4. I/O and memory priority per game

- **Tools:** Process Lasso rules include I/O priority ([Bitsum](https://bitsum.com/)). Hone has "Optimize I/O Operations" and "Optimize Memory Management" in its free tier, with no description of what they change ([Hone support](https://support.hone.gg/en/recommended-optimizations)).
- **Evidence:** none found. Suggested tier: possible placebo.
- **How it would land:** A, as more per-game profile fields; low priority.

### A5. Device power saving off

- **Tools:** Hone's free tier has "Disable Power Saving Features" ([Hone support](https://support.hone.gg/en/recommended-optimizations)). Hone 1.4 (May 2026) groups "power plan and power-saving changes" under "Enable Maximum Performance Power Mode" ([Hone blog](https://hone.gg/blog/?p=4196)). AtlasOS has power-saving options too ([Atlas docs](https://docs.atlasos.net/post-install/atlas-folder/configuration/), unverified content).
- **How it works:** turns off USB selective suspend, PCIe link power saving and network adapter power saving (inference; Hone doesn't list the exact settings).
- **Evidence:** none published for FPS. It may stop wake-up delays or USB drop-outs on some PCs (inference). It raises idle power. Suggested tier: possible placebo.
- **How it would land:** A, possible placebo section. The power-plan parts can reuse v1's existing `PowerSettingStep` (inference).

### A6. Interrupt (device) affinity and MSI on all devices

- **Tools:** Hone's free tier has "Optimize Device Affinities" and "Optimize Message Signal Interrupts" ([Hone support](https://support.hone.gg/en/recommended-optimizations)). v1 only has MSI mode for the graphics card, in possible placebo.
- **Evidence:** none published. Pinning a device's interrupts to a busy core can make things worse (inference). Suggested tier: possible placebo, needs reboot.
- **How it would land:** A, possible placebo section.

### A7. CPU security mitigations off

- **Tools:** hellzerg Optimizer ([repo](https://github.com/hellzerg/optimizer)), AtlasOS ([Atlas docs](https://docs.atlasos.net/post-install/atlas-folder/configuration/)) and ReviOS's Revision Tool ([DeepWiki](https://deepwiki.com/meetrevision/revision-tool/4.1-performance-tweaks) **(community)**) all offer to turn off Spectre and Meltdown protections (all unverified).
- **Evidence:** the cost of these protections was largest on older CPUs; current CPUs fix much of it in hardware, so the gain for games is likely small (inference). The security cost is real.
- **Anti-cheat risk:** none documented.
- **How it would land:** if at all, an A tweak with the security risk flag, offered only under Advanced. I'd recommend leaving it out until the "Windows tweak evidence" ticket finds a measurement (inference).

### A8. Memory and system-service switches

- **Tools:** ReviOS's Revision Tool has switches for SysMain (Superfetch), memory compression, Fault Tolerant Heap, service grouping and Intel TSX ([DeepWiki](https://deepwiki.com/meetrevision/revision-tool/4.1-performance-tweaks) **(community)**, unverified content). Hone has "Optimize Memory Management" ([Hone support](https://support.hone.gg/en/recommended-optimizations)).
- **Evidence:** none found. Turning memory compression off uses more RAM, so it can only help PCs with plenty of it (inference). Suggested tier: possible placebo.
- **How it would land:** A, possible placebo section. SysMain is a service, so the Startup & Services page may already cover it if it is on the reviewed list (inference).

### A9. Sticky Keys and Filter Keys shortcuts off

- **Tools:** WinUtil, hellzerg Optimizer and AtlasOS (unverified which of them; WinUtil's tweak list is at [WinUtil docs](https://winutil.christitus.com/dev/tweaks)).
- **How it works:** pressing Shift five times opens the Sticky Keys prompt, which pulls a fullscreen game to the desktop. Holding right Shift for eight seconds does the same for Filter Keys. Turning the shortcuts off stops the prompts (inference; Windows accessibility behaviour).
- **Evidence:** it fixes a real interruption and has no FPS effect. Suggested tier: proven (as an interruption fix).
- **Anti-cheat risk:** none.
- **How it would land:** A, an account change and an instant change, so it can also go in per-game profiles.

### Hone groups we can't assess

Hone 1.4 merged its tweaks into groups such as "Optimize FPS & Input Lag" (timer resolution, CSRSS settings, raw mouse input, Game Mode) and "Optimize Graphics Presentation Latency" ("Flip Integrity Fix") ([Hone blog, 2026-05-26](https://hone.gg/blog/?p=4196)). Hone doesn't publish what these change, so I couldn't compare them. That belongs to the Windows tweak evidence and paid booster tickets.

## E. Online play

- **E1. DNS changer with ping test.** hellzerg Optimizer has one (unverified, [repo](https://github.com/hellzerg/optimizer)). WinUtil's Advanced tweaks include a DNS change (earlier `gaming-tweak-catalogue.md`, citing [WinUtil guides](https://winutil.christitus.com/guides/tweaks/)). DNS only changes how fast a server's name is looked up before connecting. In-game ping depends on the route to the game server (inference). Hone itself says your ISP and game server "still affect your ping" ([Hone](https://www.hone.gg/)). Suggested tier: possible placebo for ping.
- **E2. Hosts file editor.** hellzerg Optimizer (unverified, [repo](https://github.com/hellzerg/optimizer)). Blocking hosts can break launchers or anti-cheat checks that need them (inference). Not a performance feature.
- **E3. Hone's network bundle.** Free with referrals: netsh and TCP settings. Premium: "Remove Network Bandwidth Limits", congestion provider, adapter settings and DSCP ([Hone support](https://support.hone.gg/en/recommended-optimizations)). Hone's own page says to reset internet settings if latency or speed gets worse. DSCP tags only help if your router acts on them (inference). v1 already has Nagle, network throttling and interrupt moderation in possible placebo. Suggested tier: possible placebo.
- **E4. IPv6 off, Teredo off, prefer IPv4.** WinUtil Advanced tweaks ([WinUtil Teredo](https://winutil.christitus.com/dev/tweaks/z--advanced-tweaks---caution/teredo)). The earlier catalogue found that IPv6 or Teredo off breaks some Xbox Live and Game Pass features. Leave out, or possible placebo with that warning.

## Other

- **O1. Instant replay and recording.** NVIDIA App saves "the last 30 seconds of gameplay", records up to "8K HDR at 30 fps, or 4K HDR at 120 fps", and has AV1 on RTX 40 and automatic Highlights ([NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/)). Adrenalin has "Media and Capture" ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). This is not a performance feature. v1 already lists OBS in the Apps catalogue and turns off Game DVR background recording.
- **O2. HDR and image filters.** RTX HDR adds HDR to DirectX and Vulkan games without it, and RTX Dynamic Vibrance and Freestyle change how a game looks ([NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/)). Adrenalin has Image Sharpening 2 and Integer Scaling ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). Special K adds HDR and other fixes by injecting into the game ([Special K wiki](https://wiki.special-k.info/FAQ), unverified content). These change looks, not speed.
- **O3. One-click game settings.** NVIDIA's "Optimize" button sets a game's own settings from NVIDIA's data for over 1,200 titles, with a Performance-to-Quality slider ([NVIDIA, 2024-11-12](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)). Hone has "game configs and presets" ([Hone](https://www.hone.gg/)). Both are presets, which ADR 0006 rules out. NVIDIA's data isn't public anyway (inference).
- **O4. Shared GPU memory override.** Intel Graphics Software lets laptops with Core Ultra integrated graphics give up to 87% of system memory to the GPU ([Tom's Hardware](https://www.tomshardware.com/pc-components/gpu-drivers/intels-new-graphics-driver-lets-you-dedicate-up-to-87-percent-of-laptop-memory-capacity-to-the-igpu-for-vram-core-ultra-cpus-get-shared-gpu-memory-override-feature) **(community)**, unverified content). It only matters for integrated-graphics gaming.
- **AI assistants.** NVIDIA's Project G-Assist ([NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/)) and AMD Chat ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). Out of scope.

## Anti-cheat: what the free tools teach us

- **Driver-level settings are the safe route.** No first-party source names a conflict with a driver frame cap, driver low-latency mode, VRR setting or vendor overlay (inference from the absence in the earlier anti-cheat matrix).
- **Anything that hooks the game is the risk.** Anti-Lag+ hooked Counter-Strike 2's code and got players VAC-banned ([Tom's Hardware](https://www.tomshardware.com/news/amd-pulls-drivers-that-cause-counter-strike-2-bans-after-valve-roasted-them) **(community)**). BattlEye refuses to start with injected graphics mods such as ReShade or Special K (earlier `anti-cheat-requirements.md`, row 14, citing [Rockstar support](https://support.rockstargames.com/articles/2qX3EkMbz2ahbzhgJXSZ29/failed-to-initialize-battleye-service-driver-load-error-1072) and the [BattlEye FAQ](https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq)).
- **Measuring should use event tracing.** PresentMon-style capture reads Windows events, not the game's memory, so it avoids Riot's October 2026 block on apps that read game memory (inference, [Riot](https://support.riotgames.com/ar-ae/riot/performance/game-memory-access-removed-for-third-party-apps)).
- Following ADR 0005, anything with a known conflict warns and never blocks. Injection is simply something we shouldn't build (inference).

## What not to copy

| Feature | Tools | Why not |
|---|---|---|
| One-click bundles: HYPR-RX, Hone's merged groups and presets, NVIDIA "Optimize" | Adrenalin, Hone, NVIDIA App | ADR 0006: no presets. Each part can still be its own switch. |
| Fullscreen optimizations off for every game | WinUtil Advanced ([WinUtil](https://winutil.christitus.com/dev/tweaks/z--advanced-tweaks---caution/disablefso)) | The earlier catalogue says per game only, never global. v1 does it per game. |
| Remove Xbox / Game Bar | Hone ("Disable Xbox", free tier), WinUtil | Game Bar and Game Mode keep dual-CCD X3D CPUs on the right cores (earlier catalogue). |
| Teredo or IPv6 off | WinUtil | Breaks some Xbox Live and Game Pass features (earlier catalogue). |
| Change BIOS settings | Hone Premium | v1's BIOS guides on purpose never change the BIOS. |
| Inject into the game (Special K, RTSS-style hooks, Anti-Lag+) | Special K, Anti-Lag+ | Anti-cheat blocks and a documented VAC ban wave. |

## Tool notes

- **Hone (free tier).** Free gets 10 optimizations, plus one per referral up to 15, the Balanced game mode, the Standard Boost-Up, and ads. Premium unlocks the rest, more game modes and game presets; the price is shown only at checkout ([Hone premium](https://hone.gg/ja/premium)). The web installer comes through Overwolf, and the page makes no anti-cheat statement ([Hone](https://www.hone.gg/)). Version 1.4 (May 2026) merged its tweaks into groups ([Hone blog](https://hone.gg/blog/?p=4196)). Its support page lists the free subset as timer resolution, MSI, I/O operations, power saving, memory management, device affinities, services, Xbox off and NVIDIA tweaks; that page may predate 1.4 ([Hone support](https://support.hone.gg/en/recommended-optimizations)).
- **Chris Titus WinUtil.** Essential tweaks, Advanced tweaks ("caution"), app installs and update settings, documented per tweak ([WinUtil docs](https://winutil.christitus.com/dev/tweaks)). Its gaming items are mostly ones v1 already has. The new ones are global FSO off, Teredo and IPv6, DNS, and probably Sticky Keys (unverified).
- **NVIDIA App.** Launched 2024-11-12 ([NVIDIA](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)). It added DLSS overrides in January 2025, and global DLSS overrides, Smooth Motion on RTX 40 and more legacy 3D settings in August 2025 ([NVIDIA](https://nvidia.com/en-us/geforce/news/nvidia-app-global-dlss-overrides-rtx-40-series-smooth-motion)). Its Automatic Tuning has no API, which is why v1 can't start it (`NvidiaGpuTuning.AutoTune`).
- **AMD Software: Adrenalin Edition.** HYPR-RX, Anti-Lag, Chill, Boost, RSR, AFMF, FRTC, FSR, FreeSync, Enhanced Sync, Image Sharpening 2, Integer Scaling, a game library, capture, tuning and AMD Chat ([AMD](https://www.amd.com/en/products/software/adrenalin.html)). Most of its 3D settings are in ADLX, which v1 already loads for tuning (`AdlxGpuTuning.cs`).
- **Intel Graphics Software.** It replaced Arc Control ([Neowin](https://www.neowin.net/news/intel-graphics-software-unveiled-an-all-in-one-hub-to-replace-arc-control/) **(community)**, unverified content). Its Performance Tuning was still "in Beta" in the 2026-06-03 driver ([Intel release notes 101.8826](https://downloadmirror.intel.com/927537/ReleaseNotes_101.8826_.pdf)). It also has Endurance Gaming on some laptops, a performance overlay and the shared memory override (see above). v1 already loads IGCL for tuning (`IgclGpuTuning.cs`).
- **Process Lasso (free).** "Most Features Are Free. Pro Unlocks the Rest", and Pro is a one-time payment ([Bitsum](https://bitsum.com/)). Which features are Pro-only is on a page I couldn't open ([Bitsum free vs Pro](https://bitsum.com/howfree), unverified). Its developer says most people should not use standby list purging (earlier catalogue, [Bitsum forum](https://community.bitsum.com/forum/index.php?msg=30475)).
- **CapFrameX.** Open source, built on PresentMon, v1.9.1 ([CapFrameX README](https://github.com/CXWorld/CapFrameX)). The best free model for area C.
- **Special K.** A mod framework that injects into games for frame limiting, HDR and fixes ([Special K wiki](https://wiki.special-k.info/FAQ), unverified content). Single-player only in practice, because of the anti-cheat risk.
- **LatencyMon.** Resplendence's driver latency checker ([Resplendence](https://www.resplendence.com/latencymon), unverified content). The model for D1.
- **hellzerg Optimizer.** An open-source tweak tool with telemetry, services, network/DNS, hosts and app installs ([repo](https://github.com/hellzerg/optimizer), unverified content). Whether it is still maintained in 2026 is (unverified).
- **AtlasOS and ReviOS.** Playbooks that change a fresh Windows install, with a folder or tool to switch options afterwards ([Atlas docs](https://docs.atlasos.net/post-install/atlas-folder/configuration/), [ReviOS docs](https://www.revi.cc/docs/faq/after/features), unverified content). Their gaming parts that v1 lacks are mitigations off, device power saving, and memory and service switches (A5, A7, A8). Both turn off security features v1 already covers with warnings.

## Gaps

- The shared web-fetch budget (100 per hour across all agents) and the search budget ran out part-way. I could not open the pages for LatencyMon, Special K, WinUtil's full tweak list, hellzerg Optimizer, AtlasOS, ReviOS, Intel Graphics Software's feature list, Process Lasso's free-vs-Pro page, PresentMon, Microsoft's power-throttling and CPU-sets docs, or NVAPI's DRS reference. Every claim resting on them is marked (unverified).
- I did not confirm which NVIDIA driver settings (Low Latency Mode, Max Frame Rate, DLSS Override, Smooth Motion) can be written through the public NVAPI DRS calls. Which IGCL 3D controls Intel exposes (frame limit, low latency) is also unconfirmed. Both should be checked before B is planned.
- ADLX's 3D settings index doesn't say whether they apply to all games or one game. If they are global only, a per-game profile would set them when the game starts and put them back when it closes, as with other temporary changes (inference).
- I found no independent measurements for Process Lasso's CPU sets or Efficiency mode in games, for Hone's tweaks, or for the AtlasOS and ReviOS options.
- The licences of PresentMon and LatencyMon, and whether either may be bundled or must be downloaded, are unchecked.

## Sources

**Hone**
- Hone home page: https://www.hone.gg/
- Hone Premium vs Free: https://hone.gg/ja/premium
- Hone 1.4 update (2026-05-26): https://hone.gg/blog/?p=4196
- Hone recommended optimizations: https://support.hone.gg/en/recommended-optimizations

**Chris Titus WinUtil**
- Tweak docs: https://winutil.christitus.com/dev/tweaks (unverified content)
- Disable Fullscreen Optimizations: https://winutil.christitus.com/dev/tweaks/z--advanced-tweaks---caution/disablefso
- Disable Teredo: https://winutil.christitus.com/dev/tweaks/z--advanced-tweaks---caution/teredo
- Tweaks guide (cited by the earlier catalogue): https://winutil.christitus.com/guides/tweaks/

**NVIDIA**
- NVIDIA App product page: https://www.nvidia.com/en-us/software/nvidia-app/
- NVIDIA App launch (2024-11-12): https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/
- DLSS 4 overrides (2025-01-30): https://www.nvidia.com/en-us/geforce/news/nvidia-app-update-dlss-overrides-and-more/
- Global DLSS overrides, Smooth Motion on RTX 40 (2025-08-18): https://nvidia.com/en-us/geforce/news/nvidia-app-global-dlss-overrides-rtx-40-series-smooth-motion
- Game Ready Driver and app update (2025-08-19): https://www.nvidia.com/en-us/geforce/news/nvidia-app-update-5-geforce-game-ready-driver/
- NVAPI: https://developer.nvidia.com/nvapi (via earlier tech-stack note)
- FrameView user guide: https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-8-user-guide-web-version.pdf (unverified content)
- TechPowerUp Reflex LDAT review **(community)**: https://www.techpowerup.com/review/nvidia-reflex-review-test-ldat-v2/3.html
- HWCooling on NVIDIA App latency HUD **(community)**: https://www.hwcooling.net/en/the-added-value-of-the-nvidia-app-quick-latency-insights-in-game-via-hud/

**AMD**
- AMD Software: Adrenalin Edition: https://www.amd.com/en/products/software/adrenalin.html
- HYPR-RX: https://www.amd.com/en/products/software/adrenalin/hypr-rx.html
- Anti-Lag 2 on GPUOpen: https://gpuopen.com/anti-lag-2/
- ADLX IADLX3DSettingsServices reference: https://gpuopen.com/manuals/adlx/adlx-sdk-references/adlx-interfaces/3d-graphics/iadlx3dsettingsservices
- Tom's Hardware on Anti-Lag+ VAC bans (2023-10-13) **(community)**: https://www.tomshardware.com/news/amd-pulls-drivers-that-cause-counter-strike-2-bans-after-valve-roasted-them

**Intel**
- Endurance Gaming: https://www.intel.com/content/www/us/en/support/articles/000097679.html
- Driver 101.8826 release notes (2026-06-03): https://downloadmirror.intel.com/927537/ReleaseNotes_101.8826_.pdf
- Performance overlay not available: https://www.intel.com/content/www/us/en/support/articles/000100253/graphics.html (unverified content)
- Frame delivery settings: https://www.intel.com/content/www/us/en/support/articles/000095281/graphics.html (unverified content)
- XeLL developer guide: https://www.intel.com/content/www/us/en/developer/articles/technical/xell-developer-guide.html
- IGCL repo: https://github.com/intel/drivers.gpu.control-library (via earlier tech-stack note)
- TechPowerUp on Smooth Sync (2022) **(community)**: https://techpowerup.com/295666/intel-smooth-sync-lets-gamers-with-fixed-refresh-rate-monitors-enjoy-low-latency-gaming
- Neowin on Intel Graphics Software replacing Arc Control **(community)**: https://www.neowin.net/news/intel-graphics-software-unveiled-an-all-in-one-hub-to-replace-arc-control/
- Tom's Hardware on shared GPU memory override **(community)**: https://www.tomshardware.com/pc-components/gpu-drivers/intels-new-graphics-driver-lets-you-dedicate-up-to-87-percent-of-laptop-memory-capacity-to-the-igpu-for-vram-core-ultra-cpus-get-shared-gpu-memory-override-feature
- Intel community request for a frame-gen override **(community)**: https://community.intel.com/t5/Intel-Arc-Discrete-Graphics/Intel-Graphics-Software-Needs-Urgent-Updates-XeSS-Frame-Gen/m-p/1738354

**Process Lasso**
- Bitsum home page: https://bitsum.com/
- Free vs Pro: https://bitsum.com/howfree (unverified content)
- Developer on standby list purging (via earlier catalogue): https://community.bitsum.com/forum/index.php?msg=30475

**Measuring**
- CapFrameX README: https://github.com/CXWorld/CapFrameX
- PresentMon: https://github.com/GameTechDev/PresentMon (unverified content)

**Special K**
- Special K FAQ: https://wiki.special-k.info/FAQ (unverified content)
- Blur Busters thread on Latent Sync **(community)**: https://forums.blurbusters.com/viewtopic.php?p=74923

**LatencyMon**
- Resplendence LatencyMon: https://www.resplendence.com/latencymon (unverified content)
- Technical notes: https://www.resplendence.com/latencymon_technical (unverified content)
- winget package **(community)**: https://winget.run/pkg/Resplendence/LatencyMon
- Allen & Heath support article **(community)**: https://support.allen-heath.com/hc/en-gb/articles/4403611454609-General-LatencyMon (unverified content)

**hellzerg Optimizer, AtlasOS, ReviOS**
- hellzerg Optimizer: https://github.com/hellzerg/optimizer (unverified content)
- Atlas folder configuration: https://docs.atlasos.net/post-install/atlas-folder/configuration/ (unverified content)
- ReviOS docs: https://www.revi.cc/docs/faq/after/features (unverified content)
- Revision Tool performance tweaks **(community)**: https://deepwiki.com/meetrevision/revision-tool/4.1-performance-tweaks (unverified content)

**Microsoft**
- CPU sets: https://learn.microsoft.com/en-us/windows/win32/procthread/cpu-sets (unverified content)
- CPU sets for game development: https://msdn.microsoft.com/windows/uwp/xbox-apps/cpusets-games (unverified content)
- SetProcessInformation: https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation (unverified content)
- Sustainable software blog on EcoQoS: https://devblogs.microsoft.com/sustainable-software/?p=911 (unverified content)

**Anti-cheat (via earlier `anti-cheat-requirements.md`)**
- Riot, game memory access removed for third-party apps: https://support.riotgames.com/ar-ae/riot/performance/game-memory-access-removed-for-third-party-apps
- Rockstar, BattlEye driver load error: https://support.rockstargames.com/articles/2qX3EkMbz2ahbzhgJXSZ29/failed-to-initialize-battleye-service-driver-load-error-1072
- BattlEye FAQ (Ubisoft): https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq
- Hardware Busters on X3D core parking **(community)**: https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/
