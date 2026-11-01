# Find which GPU driver settings the app can set and which help

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/89

Researched 2026-10-30 by reading the vendors' own SDK headers, docs and licences (NVAPI R615 from September 2026, ADLX 2.0.0.125 from September 2026, IGCL v306 from 28 October 2026, ADL 18.1, Anti-Lag 2 SDK 2.0.0a), plus vendor web pages and the NVIDIA Profile Inspector source. Every claim links to its source. **(community)** marks a claim from a secondary or community source. **(inference)** marks our own reasoning. The shared web-fetch and web-search budgets ran out part-way, so some vendor pages are cited by their title only; each of those says **(title only)**. See Gaps.

## Answer

- **NVIDIA: yes, for all games and per game.** NVIDIA's public NVAPI has a full "driver settings" (DRS) API for the same profiles NVIDIA Control Panel uses. It is MIT-licensed, free, and the code runs inside the NVIDIA driver.
  - It can read every setting, say where the value comes from (the game's profile, the global profile, or NVIDIA's own default), and put back NVIDIA's default. So exact undo is possible.
  - Writing needs administrator rights. Our service already has them.
  - Most settings are read when the game starts. So they are **not instant changes**. They fit a **per-game tweak** (it stays on the game's profile) better than a per-game profile.
  - "Low Latency Mode: On" and Max Frame Rate are in the public header. **"Ultra" is not**: it uses two setting IDs that only NVIDIA Profile Inspector's community list names.
- **AMD: global only.** ADLX can read and set Anti-Lag, Chill, Boost, Image Sharpening, Enhanced Sync, Frame Rate Target Control, Wait for Vertical Refresh and a few more, with ranges and defaults from the driver.
  - **ADLX has no per-game (application profile) API.** Adrenalin's per-game settings can't be set through a documented interface.
  - Several AMD features switch each other off (Anti-Lag and Chill, Chill and Boost, Enhanced Sync and VSync). Undo must save and put back the whole group.
- **Intel: global and per game.** IGCL's `ctlGetSet3DFeature` takes a game's .exe name, or an empty name for global. It covers low latency (with a "boost" option), frame limit, Endurance Gaming, frame pacing, VSync-style flip modes and more.
  - For each feature the driver reports whether it works per game, which features clash with it, and whether it **can change while the game runs**. That last flag tells us which Intel settings are instant changes.
- **What helps.** Only the driver latency modes have vendor evidence, and only in narrow cases:
  - NVIDIA says Low Latency Mode helps most "when your game is GPU bound, and framerates are between 60 and 100 FPS". It says Ultra cuts latency "by up to 33%", in DX9 and DX11 games only.
  - AMD says Anti-Lag helps "in GPU-limited cases".
  - Game-built-in versions (NVIDIA Reflex, AMD Anti-Lag 2) do more, and Reflex overrides the driver mode.
  - Frame caps and VSync choices are trade-offs, so they are **situational**.
  - Power management mode, texture filtering quality, threaded optimization and shader cache size have no vendor numbers for games. They are **possible placebo**.
- **Anti-cheat.** In October 2023, AMD's driver Anti-Lag+ led to Counter-Strike 2 VAC bans. Valve said it "is implemented by detouring engine dll functions" and "any tampering with CS code will result in a VAC ban".
  - AMD's next driver, 23.10.2, came out right after **(title only)**. Press reports say AMD pulled 23.10.1 **(community)** and that Valve would reverse the bans **(community)**.
  - Settings the driver applies on its own side (DRS, ADLX and IGCL values) don't touch the game's code. We found no ban reports for them **(inference)**.
  - AMD's "Anti-Lag Next" level in ADLX should carry an anti-cheat warning until we know what it does **(inference)**.
- **Recommendation (inference).**
  - Build NVIDIA DRS first: it has the most users and the best API.
  - Then Intel IGCL, which has the cleanest per-game and instant-change support.
  - Then AMD, global only, with honest labels.
  - Offer only documented settings. Keep NVIDIA "Ultra" out, or behind Advanced with a "not documented by NVIDIA" note.

## How each vendor's interface works

### NVIDIA NVAPI driver settings (DRS)

**Is it public, free and allowed?**

- NVAPI's public repo holds the headers and the import libraries. "The actual NVAPI implementation is provided by the NVIDIA GPU driver installed on the system." The repo is "licensed under the MIT License". [NVAPI README](https://github.com/NVIDIA/nvapi/blob/main/README.md), [License.txt](https://github.com/NVIDIA/nvapi/blob/main/License.txt)
- `NvApiDriverSettings.h`, which lists the setting IDs and values, carries the same MIT notice, dated 2019–2026. [NvApiDriverSettings.h](https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h)
- The R615 release notes are dated September 2026. Their file list still names a separate "NVAPI_SDKs_Samples_and_Tools_License_Agreement(Public).pdf", but the repo's `docs` folder has no such file, only the CHM reference and the notes. [R615 release notes](https://github.com/NVIDIA/nvapi/blob/main/docs/R615-NVAPI-OpenSource-Release-Notes.pdf). We read the MIT files as the licence that applies **(inference)**.
- NVIDIA's online reference says DRS was "introduced in R256" to manage 3D settings and application profiles. The page shows Release 590, February 2026. [NVAPI DRS reference](https://docs.nvidia.com/nvapi/group__drsapi.html)
- Every DRS function is documented for "Windows 10 and higher". [nvapi.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi.h)

**What the API can do** ([nvapi.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi.h)):

- **Sessions:** `CreateSession`, `LoadSettings` and `SaveSettings` ("saves the settings data to the system").
- **Profiles:** `GetBaseProfile` and `GetCurrentGlobalProfile` for all games. For one game, `FindApplicationByName` "will always return the profile the driver will apply upon running the application" when given a full path. `CreateProfile` and `CreateApplication` add a profile when a game has none. `CreateApplication` recommends the .exe's absolute path. An .exe can sit in only one profile (`NVAPI_EXECUTABLE_ALREADY_IN_USE`, "Application already exists in the other profile"). [nvapi_lite_common.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi_lite_common.h)
- **Read:** `GetSetting` returns an `NVDRS_SETTING`, which holds:
  - the current value;
  - NVIDIA's predefined value, with `isPredefinedValid`;
  - `isCurrentPredefined` ("0 if the currentValue is a user value");
  - `settingLocation`: the current profile, the global profile, the base profile or the default.
  - `EnumAvailableSettingValues` lists the allowed values and the default for a setting.
- **Undo-friendly writes:**
  - `SetSetting` adds or changes a value.
  - `DeleteProfileSetting` "deletes a setting or sets it back to predefined value".
  - `RestoreProfileDefaultSetting` puts back NVIDIA's predefined value for one setting.
  - `RestoreProfileDefault` does the same for a whole profile, and removes it "if the whole profile was set by the user".
  - `DeleteProfile` removes a profile the app made.
- **Rights:** NVAPI returns `NVAPI_INVALID_USER_PRIVILEGE` when an API needs administrator rights. The R615 notes list only `NvAPI_GPU_SetEDID` as needing them. [R615 release notes](https://github.com/NVIDIA/nvapi/blob/main/docs/R615-NVAPI-OpenSource-Release-Notes.pdf) Even so, Profile Inspector "must be started elevated to write changes to the NVIDIA driver profile database". [Profile Inspector README](https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/README.md) **(community)** Our service is elevated, so either way works **(inference)**.

**How NVIDIA Profile Inspector uses it** (MIT licence, last commit 2026-10-13; [repo](https://github.com/Orbmu2k/nvidiaProfileInspector)) **(community)**:

- It loads `nvapi64.dll` and gets each DRS function through `nvapi_QueryInterface` with a numeric ID. It does not link NVIDIA's `.lib`. [NvapiDrsWrapper.cs](https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/Native/NVAPI/NvapiDrsWrapper.cs)
- Most of its IDs match NVIDIA's public `nvapi_interface.h` (for example `NvAPI_DRS_SetSetting` = `0x577DD202`). [nvapi_interface.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h)
- It tries some IDs that the public table doesn't list first (`0x8A2CF5F5` for SetSetting), plus `LoadSettingsFromFileEx` and `SaveSettingsToFileEx`. It falls back to the public IDs when those are missing.
- It decrypts "internal" settings with a fixed key. [DrsDecrypterService.cs](https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/Common/DrsDecrypterService.cs)
- Its names for hidden settings come from its own `CustomSettingNames.xml`, not from NVIDIA. [CustomSettingNames.xml](https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/CustomSettingNames.xml)
- For our app: a C# service can call the same public functions through `nvapi_QueryInterface` using only the IDs in the MIT `nvapi_interface.h`, or link `nvapi64.lib` from a small native bridge like our ADLX and IGCL bridges. It should skip the private IDs and the decrypting **(inference)**.

**Instant or game restart.**

- NVIDIA's docs don't say when a changed profile reaches a running game. The `FindApplicationByName` wording ("the profile the driver will apply upon running the application") points to the profile being picked when the game starts **(inference)**.
- So treat DRS settings as needing a game restart, and test each one on real hardware. We could not confirm which, if any, apply live (see Gaps).

**Where it fits in the app (inference).**

- A DRS value on a game's profile stays until it is removed, and the driver applies it every time that .exe starts. That matches a **per-game tweak**.
- A per-game profile turns on after the game has started, which is too late for settings read at launch. So DRS changes shouldn't go in per-game profiles.
- The global profile affects every account on the PC. So a global DRS change is a **whole-PC change**. The driver keeps one profile database for the PC, not one per account **(inference)**.

**Exact undo recipe (inference).**

1. Before a change, save the following:
   - whether the game's profile and the .exe entry existed;
   - the setting's `settingLocation` and `isCurrentPredefined`;
   - its current and predefined values.
2. To undo, first re-read the setting. If it no longer holds the value we set, refuse, as the undo rule says. Otherwise:
   - If it was a user value before, `SetSetting` the old value back.
   - If it was NVIDIA's predefined value, `RestoreProfileDefaultSetting`.
   - If it was inherited (no value on this profile), `DeleteProfileSetting`.
   - If we created the profile, `DeleteProfile` it.
3. Always `LoadSettings` just before and `SaveSettings` just after, so changes made in NVIDIA Control Panel in between aren't lost.

### AMD ADLX 3D settings

**Is it public, free and allowed?**

- ADLX is AMD's current SDK. The repo is at version 2.0.0.125, with its last commit on 2026-09-21. [ADLX repo](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX), [ADLXVersion.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ADLXVersion.h)
- "The ADLX library is installed with the driver as a .dll file." [ADLX Programming Guide (in repo)](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/tree/main/SDKDoc)
- The licence allows shipping derivative works in object code inside our app. An earlier research note covers the details: overclocking-and-driver-tools, citing the [ADLX SDK License Agreement](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/ADLX%20SDK%20License%20Agreement.pdf). v1 already ships an ADLX bridge for tuning (`src/Elynt.Windows/AdlxGpuTuning.cs`).

**What it can do.**

- `IADLX3DSettingsServices` has one getter per feature, and each takes a GPU:
  - `GetAntiLag`, `GetChill`, `GetBoost` and `GetImageSharpening`;
  - `GetEnhancedSync`, `GetWaitForVerticalRefresh` and `GetFrameRateTargetControl`;
  - `GetAntiAliasing`, `GetMorphologicalAntiAliasing`, `GetAnisotropicFiltering` and `GetTessellation`;
  - `GetRadeonSuperResolution` and `GetResetShaderCache`.
  - Source: [I3DSettings.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/I3DSettings.h)
- Newer interfaces add the following:
  - AMD Fluid Motion Frames: [I3DSettings1.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/I3DSettings1.h).
  - Sharpen Desktop: [I3DSettings2.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/I3DSettings2.h).
  - A FidelityFX Super Resolution override and a frame-generation "upgrade": [I3DSettings3.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/I3DSettings3.h).
- Each feature has `IsSupported`, `IsEnabled` and `SetEnabled`. Valued features also have a getter, a setter and a `…Range` (for example Chill's `GetFPSRange`, `SetMinFPS` and `SetMaxFPS`). So every value can be read back **(inference)**.
- `IADLX3DSettingsChangedHandling` reports when anyone changes a 3D setting, for example in Adrenalin. That is useful for **drift** **(inference)**.

**No per-game profiles.**

- None of the 3D methods takes an application. They work on a GPU. [I3DSettings.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/I3DSettings.h)
- The only "applications" interface in ADLX lists processes that are using the GPU (`ProcessID`, `Name`, `FullPath`, `GPUDependencyType`). It can't set profiles. [IApplications.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IApplications.h)
- The legacy ADL SDK (18.1, April 2025) still has generic `ADL2_ApplicationProfiles_*` functions, which create a profile in a "driver area" and assign an .exe. [ADL Application Profiles APIs](https://github.com/GPUOpen-LibrariesAndSDKs/display-library/tree/master/Public-Documents/html) But AMD documents no driver-area or property names for Anti-Lag, Chill and the rest. So this is not a documented route to Adrenalin's per-game settings **(inference)**.
- AMD has a support page on customizing graphics settings in Adrenalin, which is where per-game settings are made by hand. [AMD DH3-012](https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html) **(title only)**

**Features that switch each other off** ([I3DSettings.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/I3DSettings.h)):

- Anti-Lag and Anti-Lag Next can't be on with Chill.
- Chill can't be on with Boost or Anti-Lag.
- Enhanced Sync can't be on with VSync (Wait for Vertical Refresh).
- On some GPUs, Radeon Super Resolution can't be on with Boost, Image Sharpening, Integer Scaling or Center Scaling. RSR also turns GPU scaling on.
- "When AMD Radeon Anti-Lag Next is enabled, the mutually exclusive features are automatically disabled. If a mutually exclusive feature is re-enabled, its previous configuration settings are restored." [IADLX3DAntiLag1 doc page (in repo)](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/tree/main/SDKDoc)

**Exact undo recipe (inference).**

- Save the state, value and level of every feature in a clash group before any change in that group.
- To undo, re-read the group first. If something else changed it, refuse. Otherwise turn off what we turned on, then write back the saved values for the group.
- These are global, so each is a **whole-PC change**. AMD's per-GPU settings aren't per account.

**Instant or game restart.**

- ADLX doesn't say. FRTC is described as a cap "in full-screen applications". MLAA requires "exclusively full-screen mode" and is "not applicable to DirectX 12 and Vulkan applications". Anisotropic filtering "only affects DirectX 9 applications".
- We could not find AMD text on whether a change reaches a running game. Treat all of them as needing a game restart until tested.

### Intel IGCL 3D features

**Is it public, free and allowed?**

- "IGCL binaries are distributed as part of Intel Graphics driver package." [IGCL README](https://github.com/intel/drivers.gpu.control-library/blob/master/README.md)
- The licence lets us "use and redistribute" the Control API software and headers "solely for use on Intel platforms", keeping the notice, with no reverse engineering. [IGCL License.txt](https://github.com/intel/drivers.gpu.control-library/blob/master/License.txt)
- The header's last update is v306, from 2026-10-28. [repo](https://github.com/intel/drivers.gpu.control-library)
- v1 already ships an IGCL bridge (`src/Elynt.Windows/IgclGpuTuning.cs`).

**What it can do** ([igcl_api.h](https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h)):

- `ctlGetSupported3DCapabilities` returns one `ctl_3d_feature_details_t` per feature, with:
  - the value type, range and **default**;
  - `PerAppSupport` ("whether the feature is supported per application");
  - `ConflictingFeatures` ("features … which can't be enabled along with" it);
  - `FeatureMiscSupport` flags for DX9, DX11, DX12 and Vulkan, and **`CTL_3D_FEATURE_MISC_FLAG_LIVE_CHANGE`**: "User can change feature live without restarting the game".
- `ctlGetSet3DFeature` gets or sets one feature. `ApplicationName` is the game: "If this is an empty string then this will get/set global settings". It "should contain only the name of the application and not the system specific path".
- Intel's own sample sets Endurance Gaming "Per Application" and then "Global". [3D_Feature_Sample_App.cpp](https://github.com/intel/drivers.gpu.control-library/blob/master/Samples/3D_Feature_Samples/3D_Feature_Sample_App.cpp)
- The features: frame pacing, Endurance Gaming, frame limit, anisotropic filtering, CMAA, texture filtering quality, adaptive tessellation, sharpening filter, MSAA, gaming flip modes, Adaptive Sync Plus, app profiles (compatibility and performance tiers), VRR for windowed games, global-or-per-app choice, low latency, frame generation, prebuilt shader download, a live-state readout and frame generation control.

**Where it fits (inference).**

- Per-game Intel settings whose LIVE_CHANGE flag is set can be **temporary changes** in a per-game profile. Ask the driver at run time; don't hard-code the list.
- Settings without the flag become per-game tweaks.
- Global settings are whole-PC changes.

**Undo (inference).**

- Save the value read with `bSet = false`, write it back on undo, and refuse if the current value no longer matches ours.
- Gap: the header has no call to delete one game's override and fall back to global. `CTL_3D_FEATURE_GLOBAL_OR_PER_APP` ("Set global settings or per application settings") may be how to send a game back to global. That needs a test on Arc hardware.

## NVIDIA settings

The setting IDs and values are from [NvApiDriverSettings.h](https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h) unless marked. All are readable and exactly restorable through DRS (see the undo recipe). "Restart" means the setting probably applies when the game next starts **(inference)**.

| Control Panel setting | DRS setting (public?) | What it does | Evidence tier | Instant? | Anti-cheat |
|---|---|---|---|---|---|
| Low Latency Mode: On | `PRERENDERLIMIT_ID` 0x007BA09E "Maximum pre-rendered frames", 0 = app-controlled, set to 1 (public) | Limits the CPU's queue of frames to one | Situational: GPU-bound, DX9/DX11 only | Restart | None known |
| Low Latency Mode: Ultra | 0x10835000 "Ultra Low Latency - Enabled" plus 0x0005F543 "CPL State" (**not in the public header**; Profile Inspector names only **(community)**) | Sends each frame "just in time" for the GPU | Situational: "up to 33%" by NVIDIA, GPU-bound only | Restart | None known |
| Max Frame Rate | `FRL_FPS_ID` 0x10835002 "Frame Rate Limiter", 0 = off, up to 1023 (public) | Driver frame cap | Situational | Restart (untested) | None known |
| Background Application Max Frame Rate | 0x10835005 (not public **(community)**); the public `APPIDLE_DYNAMIC_FRL_FPS_ID` 0x10835016 "Idle Application Max FPS Limit" is a different setting | Caps a game that is in the background | Situational (power, heat) | Unknown | None known |
| Power management mode | `PREFERRED_PSTATE_ID` 0x1057EB71: Adaptive 0, Prefer max 1, Driver controlled 2, Prefer consistent 3, Prefer min 4, Optimal power 5 = default (public) | How eagerly the GPU stays at high clocks | Possible placebo | Restart | None known |
| Shader Cache Size | `PS_SHADERDISKCACHE_MAX_SIZE_ID` 0x00AC8497, default 0x4000 (public); `PS_SHADERDISKCACHE_ID` on/off, default on (public) | Disk space for compiled shaders. Profile Inspector reads the value as MB, so 0x4000 = 16 GB **(community)** | Possible placebo (raising it); turning the cache off is likely harmful **(inference)** | Restart | None known |
| Vertical sync | `VSYNCMODE_ID` 0x00A879CF: app-controlled (default), force off, force on, half and third rates, virtual; `VSYNCTEARCONTROL_ID` adaptive (public) | Overrides the game's VSync | Situational: off gives the lowest latency; on with G-SYNC avoids tearing | Restart | None known |
| Monitor technology / G-SYNC | `VRR_MODE_ID` 0x1194F158: off, fullscreen only (default), fullscreen and windowed; `VRR_APP_OVERRIDE_ID` per game (public) | Variable refresh on or off | Situational | Restart | None known |
| Threaded optimization | `OGL_THREAD_CONTROL_ID` 0x20C1221E: auto (0), on, off (public; the `OGL_` prefix marks it as an OpenGL setting **(inference)**) | Lets the driver use extra CPU threads | Possible placebo | Restart | None known |
| Texture filtering - Quality | `QUALITY_ENHANCEMENTS_ID` 0x00CE2691: high quality, quality (default), performance, high performance (public) | Trades filtering quality for speed | Possible placebo | Restart | None known |
| Texture filtering - optimizations, negative LOD bias | `PS_TEXFILTER_ANISO_OPTS2_ID`, `PS_TEXFILTER_BILINEAR_IN_ANISO_ID`, `PS_TEXFILTER_DISABLE_TRILIN_SLOPE_ID`, `PS_TEXFILTER_NO_NEG_LODBIAS_ID`, all off by default (public) | Small filtering shortcuts | Possible placebo | Restart | None known |
| Anisotropic filtering, antialiasing, FXAA, ambient occlusion | `ANISO_MODE_*`, `AA_MODE_*`, `FXAA_ENABLE_ID`, `AO_MODE_ID` (public) | Image-quality overrides, not speed | Not a speed tweak | Restart | None known |
| Triple buffering | `OGL_TRIPLE_BUFFER_ID`, off by default (public; OpenGL) | Extra buffer with VSync | Situational (OpenGL only) | Restart | None known |
| Preferred refresh rate | `REFRESH_RATE_OVERRIDE_ID`: app-controlled (default) or highest available (public) | Stops old games from picking 60 Hz | Situational **(inference)** | Restart | None known |
| Vulkan/OpenGL present method | `OGL_CPL_PREFER_DXPRESENT_ID`: auto (default), prefer layered on DXGI, prefer native (public) | How Vulkan and OpenGL frames reach the screen | Situational | Restart | None known |
| DLSS overrides | `NGX_DLSS_SR_OVERRIDE_ID`, `NGX_DLSSG_MULTI_FRAME_COUNT_ID` and more (public in R615) | Forces DLSS presets and frame generation | Out of scope (image quality) | Restart | Not checked |

Notes on evidence:

- **Low Latency Mode.** NVIDIA's August 2019 driver article explains the three levels:
  - Off: "the game's engine will automatically queue 1-3 frames for maximum render throughput".
  - On: one queued frame, "the same setting as" the old Max_Prerendered_Frames = 1.
  - Ultra: "submits the frame just in time for the GPU to pick it up". NVIDIA says it "further reduces latency by up to 33%".
  - It "has the most impact when your game is GPU bound, and framerates are between 60 and 100 FPS". It works in DX9 and DX11. In DX12 and Vulkan, "the game decides when to queue the frame".
  - Source: [NVIDIA, Gamescom 2019 Game Ready Driver](https://www.nvidia.com/en-us/geforce/news/gamescom-2019-game-ready-driver/)
  - No independent measurement was read (see Gaps). So the tier is **situational**: only GPU-bound DX9 and DX11 games without Reflex.
- **Reflex beats it.** NVIDIA's latency guide (2020, page updated 2025) says to turn on Reflex in the game when it is offered, because it "will ensure your CPU is submitting work to the GPU just in time". It says to use the driver's Ultra mode only when Reflex isn't there. When both are on, "NVIDIA Reflex will override Ultra Low Latency functionality". [NVIDIA System Latency Optimization Guide](https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/)
- **VSync.** The same guide says to turn VSync off for the lowest latency, because "VSYNC causes back pressure from the display that reverberates through the entire system". With G-SYNC, VSync on plus Reflex or Ultra caps the frame rate below the refresh rate. That avoids tearing at a slightly higher latency than uncapped. NVIDIA Control Panel VSync works only for fullscreen apps. [NVIDIA System Latency Optimization Guide](https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/) So VSync is **situational**, and depends on whether the monitor has G-SYNC.
- **Max Frame Rate.** NVIDIA added it in the CES 2020 driver. [NVIDIA, New CES 2020 Game Ready Driver](https://www.nvidia.com/en-us/geforce/news/nvidia-geforce-ces-2020-game-ready-driver/) **(title only)** The latency guide gives no numbers for a driver cap. We tier it **situational**: it saves power and heat, and keeps the frame rate inside a VRR range **(inference)**.
- **Power management mode, texture filtering quality, threaded optimization.** NVIDIA's latency guide doesn't mention any of them. [NVIDIA System Latency Optimization Guide](https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/) We found no NVIDIA measurement of a game gain, so they are **possible placebo**. "Prefer maximum performance" keeps clocks up, which costs power when idle **(inference)**. NVIDIA's own setting help page was found but not read (see Gaps). [NVIDIA Control Panel help, Manage 3D Settings](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nv3d/Manage_3D_Settings_(reference).htm) **(title only)**
- **Shader cache size.** NVIDIA added the size option to the Control Panel. [NVIDIA forum, "New option (shader cache size)"](https://www.nvidia.com/en-us/geforce/forums/game-ready-drivers/13/473105/new-option-shader-cache-size/) **(title only, community)** The public default is already 0x4000. Raising it can only matter when a big library fills the cache, so it is **possible placebo** for most players **(inference)**.

## AMD settings

All values come from [I3DSettings.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/I3DSettings.h) and [ADLXDefines.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ADLXDefines.h) unless marked. All are **global only** (per GPU). All can be read back with ranges, so exact undo is possible if the clash group is saved together **(inference)**.

| Adrenalin setting | ADLX interface | What it does (AMD's words where quoted) | Evidence tier | Instant? | Anti-cheat |
|---|---|---|---|---|---|
| Radeon Anti-Lag | `IADLX3DAntiLag`: on or off | "Helps reduce input lag in GPU-limited cases by controlling the pace of the CPU work" | Situational: GPU-limited only | Unknown | Driver-side; none known for Anti-Lag 1 |
| Anti-Lag level: Anti-Lag or "Anti-Lag Next" | `IADLX3DAntiLag1`: `ANTILAG` or `ANTILAGNEXT` (ADLX 1.1 and later) | Next "enables an advanced algorithm in supported DX11 and DX12 games" | Situational | Unknown | **Warn**: unclear whether it works inside the game like Anti-Lag+ **(inference)** |
| Radeon Chill | `IADLX3DChill`: on, min FPS, max FPS | "Conserves GPU power and reduces heat by adjusting the FPS based on the intensity of in-game movement" | Situational (power, heat); not a speed tweak | Unknown | None known |
| Radeon Boost | `IADLX3DBoost`: on, resolution % | "Dynamically reduces resolution during motion … Only works in supported games" | Situational (trades sharpness for FPS) | Unknown | None known |
| Radeon Image Sharpening | `IADLX3DImageSharpening`: on, sharpness; `IADLX3DImageSharpenDesktop` | "Restores clarity softened by other effects" | Not a speed tweak | Unknown | None known |
| Enhanced Sync | `IADLX3DEnhancedSync`: on or off | Sync mode that can't be used with VSync | Situational | Unknown | None known |
| Wait for Vertical Refresh | `IADLX3DWaitForVerticalRefresh`: always off, off unless the app says, on unless the app says, always on | VSync override | Situational | Unknown | None known |
| Frame Rate Target Control | `IADLX3DFrameRateTargetControl`: on, FPS with range | "Sets a user-defined target maximum frame rate in full-screen applications to reduce GPU power consumption" | Situational (power, heat, VRR range) | Unknown | None known |
| Radeon Super Resolution | `IADLX3DRadeonSuperResolution`: on, sharpness | "An upscaling feature for faster game frame rates"; turns on GPU scaling | Situational (image-quality trade) | Unknown | None known |
| Anti-aliasing, MLAA, anisotropic filtering, tessellation | `IADLX3DAntiAliasing`, `…MorphologicalAntiAliasing`, `…AnisotropicFiltering` (DX9 only), `…Tessellation` | Image-quality overrides | Not a speed tweak | Unknown | None known |
| Fluid Motion Frames, FSR override, frame-gen upgrade | `IADLX3DAMDFluidMotionFrames(1)`, `IADLX3DFidelityFXSuperResolution`, `IADLX3DFidelityFXFrameGenUpgrade` | Driver frame generation and upscaling | Out of scope (image quality and smoothness) | Unknown | Not checked |
| Reset Shader Cache | `IADLX3DResetShaderCache` | Deletes the cache | One-off action, can't be undone; not a tweak | n/a | n/a |

Notes on evidence:

- **Anti-Lag.** AMD's Anti-Lag 2 SDK describes both versions:
  - "Anti-Lag 1 is a pure driver-based technology that introduces a carefully calculated delay into the driver-side processing of game's graphics commands."
  - Anti-Lag 2 puts the delay "inside the game's logic, just before the user controls … are sampled", "achieving a significantly greater latency reduction". The game developer must build it in.
  - Source: [AMD Anti-Lag 2 SDK README](https://github.com/GPUOpen-LibrariesAndSDKs/AntiLag2-SDK/blob/main/README.md) (v2.0.0a, October 2024)
  - So, like NVIDIA's mode, driver Anti-Lag is **situational**. A game's own Anti-Lag 2 is better where it exists.
- **Counter-Strike 2 and Anti-Lag+ (October 2023).**
  - Valve's official account posted: "AMD's latest driver has made their 'Anti-Lag/+' feature available for CS2, which is implemented by detouring engine dll functions. If you are an AMD customer and play CS2, DO NOT ENABLE ANTI-LAG/+; any tampering with CS code will result in a VAC ban." [CS2 on X, 13 October 2023](https://x.com/CounterStrike/status/1712875606776729832) (text from the post's title, read through search)
  - AMD released driver 23.10.2 next. [AMD 23.10.2 release notes](https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-23-10-2.html), [23.10.1 release notes](https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-23-10-1.html) **(title only)**
  - Press reported that AMD pulled the 23.10.1 driver. [Tom's Hardware](https://www.tomshardware.com/news/amd-pulls-drivers-that-cause-counter-strike-2-bans-after-valve-roasted-them) **(community, title only)**
  - Press also reported that Valve said the bans would be reversed. [Steam forum, quoting Valve's X account](https://steamcommunity.com/app/730/discussions/0/6993585599476390349/), [GGRecon](https://www.ggrecon.com/articles/counter-strike-2-bans-for-amd-anti-lag-users-set-to-be-reversed) **(community, title only)**
  - AMD's support page title still names "Anti-Lag/Anti-Lag+". [AMD DH3-033](https://www.amd.com/en/resources/support-articles/faqs/DH3-033.html) **(title only)**
  - Lesson (inference): the ban came from code placed inside the game's process, not from a driver-side setting. Anything that injects or detours game code must carry an anti-cheat warning.
- **Chill, FRTC and Boost** are AMD's own claims about power and frame rate, quoted from the header. We found no independent game measurements. Boost and RSR lower the resolution, so they are image-quality trades, not free speed.

## Intel settings

All values are from [igcl_api.h](https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h). Every feature reports per-game support, clashes and "live change" at run time, so the "Per game?" and "Instant?" columns are what the driver says on each PC.

| Intel setting | IGCL feature | Values | What it does | Evidence tier | Per game? / Instant? | Anti-cheat |
|---|---|---|---|---|---|---|
| Low latency | `CTL_3D_FEATURE_LOW_LATENCY` | off, on, on with boost | Driver latency mode | Situational (likely GPU-bound only, like the others **(inference)**); no Intel numbers read | Driver reports both | None known |
| Frame limiter | `CTL_3D_FEATURE_FRAME_LIMIT` | max FPS, "independent of system power state" | Driver frame cap | Situational | Driver reports both | None known |
| Endurance Gaming | `CTL_3D_FEATURE_ENDURANCE_GAMING` | off, on, auto; better performance, balanced, maximum battery; max FPS "when in DC mode globally or per application" | Battery saver for laptops | Situational (laptops on battery); not a speed tweak | Driver reports both | None known |
| Frame pacing | `CTL_3D_FEATURE_FRAME_PACING` | off, no smoothing, max smoothing, competitive | Paces frames for smoothness | Situational | Driver reports both | None known |
| VSync-style flip modes | `CTL_3D_FEATURE_GAMING_FLIP_MODES` | app default, VSync off, VSync on, smooth sync, speed frame, capped FPS (at panel refresh) | Overrides how frames are flipped | Situational | Driver reports both | None known |
| Adaptive Sync Plus | `CTL_3D_FEATURE_ADAPTIVE_SYNC_PLUS` | adaptive sync, adaptive balance and strength, async above max refresh | VRR extras; "OS controls state of adaptive sync" | Situational | Driver reports both | None known |
| Texture filtering quality, anisotropic, CMAA, MSAA, sharpening, adaptive tessellation | `…TEXTURE_FILTERING_QUALITY`, `…ANISOTROPIC`, `…CMAA`, `…MSAA`, `…SHARPENING_FILTER`, `…ADAPTIVE_TESSELLATION` | per enum | Image-quality overrides | Not a speed tweak (texture filtering "performance" is possible placebo) | Driver reports both | None known |
| Game profiles | `CTL_3D_FEATURE_APP_PROFILES` | compatibility and performance tiers | Intel's own per-game fixes | Leave to the driver | Driver reports both | None known |
| Prebuilt shader download | `CTL_3D_FEATURE_PREBUILT_SHADER_DOWNLOAD` | bool | Downloads compiled shaders | Situational (first-run stutter) **(inference)** | Driver reports both | None known |
| Frame generation override | `CTL_3D_FEATURE_FRAME_GENERATION(_CONTROL)` | app choice, 2x, 3x, 4x | Forces XeSS frame generation | Out of scope | Driver reports both | Not checked |

Note: we could not read Intel's support pages on these features (see Gaps), so there is no Intel evidence on the size of the effect.

## Anti-cheat summary

- **The proven case** is AMD Anti-Lag+ in CS2. Valve's reason was "detouring engine dll functions", meaning code changes inside the game's process. [CS2 on X](https://x.com/CounterStrike/status/1712875606776729832)
- **Driver-side values.** DRS, ADLX and IGCL change values the driver applies on its own side. None of them puts code into the game through our app. We found no ban reports for any of them **(inference; no search for each game was possible)**.
- **Our rule (ADR 0005).** Anti-cheat conflicts warn and never block. Put a warning on:
  - AMD "Anti-Lag Next", until its method is known;
  - any frame-generation or upscaling override, until checked;
  - NVIDIA settings outside the public header **(inference)**.

## What the app should build (inference)

1. **NVIDIA, global and per game.**
   - Expose Low Latency Mode (On only, through public `PRERENDERLIMIT`), Max Frame Rate, VSync and G-SYNC mode, and Preferred refresh rate.
   - Label each one with its evidence tier.
   - Power management, texture filtering, threaded optimization and shader cache size get "possible placebo".
   - Per-game DRS values are per-game tweaks. The global profile is a whole-PC change.
2. **Intel, global and per game.**
   - Expose low latency, frame limiter, flip modes, Endurance Gaming (laptops) and frame pacing.
   - Ask the driver which features are per-game and live, and put only live ones in per-game profiles.
3. **AMD, global only.**
   - Expose Anti-Lag (level "Anti-Lag" only), FRTC, Chill, Enhanced Sync and Wait for Vertical Refresh.
   - Say plainly that per-game settings must be made in Adrenalin.
   - Save each clash group as one change.
4. **Leave out** NVIDIA Ultra (undocumented IDs) and AMD Reset Shader Cache (can't be undone), or put them behind Advanced. Also leave out image-quality overrides (AA, AF, sharpening, upscaling, frame generation). Those aren't speed tweaks and belong to the game.

## Gaps

- **The shared web-fetch and web-search budgets ran out.** These pages were found but not read, so claims from them are title-level only:
  - NVIDIA: the Control Panel "Manage 3D Settings" help, the CES 2020 driver article on Max Frame Rate, and the shader-cache forum post.
  - AMD: the 23.10.1 and 23.10.2 release notes, DH3-012 and DH3-033.
  - Press articles on the Anti-Lag+ bans.
  - We have no Intel support pages for low latency, frame limiter and Endurance Gaming.
  - No independent latency or FPS measurements (for example Blur Busters or Hardware Unboxed) were read.
- **Instant or restart** is unconfirmed for NVIDIA DRS and AMD ADLX. It needs a test on each vendor's GPU: change the value while a game runs and watch PresentMon frame times or the cap.
- **AMD "Anti-Lag Next"** isn't mapped to a public product name (Anti-Lag+ or the Anti-Lag 2 driver toggle).
- **AMD per-game:** we didn't test whether the legacy ADL application-profile functions reach Adrenalin's per-game settings. AMD documents no property names for them.
- **Intel undo to "inherit global"** for one game needs a test of `CTL_3D_FEATURE_GLOBAL_OR_PER_APP`.
- **Driver updates.** We didn't confirm whether NVIDIA or AMD driver installs keep or reset these settings. The drift check should re-read them after any driver change.

## Sources

NVIDIA

- NVAPI repo, README and MIT licence (commit 2026-09-18): https://github.com/NVIDIA/nvapi, https://github.com/NVIDIA/nvapi/blob/main/README.md, https://github.com/NVIDIA/nvapi/blob/main/License.txt
- nvapi.h (DRS functions and structures): https://github.com/NVIDIA/nvapi/blob/main/nvapi.h
- NvApiDriverSettings.h (setting IDs and values): https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h
- nvapi_interface.h (function IDs): https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h
- nvapi_lite_common.h (status codes): https://github.com/NVIDIA/nvapi/blob/main/nvapi_lite_common.h
- NVAPI R615 release notes (September 2026): https://github.com/NVIDIA/nvapi/blob/main/docs/R615-NVAPI-OpenSource-Release-Notes.pdf
- NVAPI DRS reference (R590, February 2026): https://docs.nvidia.com/nvapi/group__drsapi.html
- NVIDIA, Gamescom 2019 Game Ready Driver (20 August 2019): https://www.nvidia.com/en-us/geforce/news/gamescom-2019-game-ready-driver/
- NVIDIA, System Latency Optimization Guide (2020, updated 2025): https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/
- NVIDIA, New CES 2020 Game Ready Driver **(title only)**: https://www.nvidia.com/en-us/geforce/news/nvidia-geforce-ces-2020-game-ready-driver/
- NVIDIA Control Panel help, Manage 3D Settings **(title only)**: https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nv3d/Manage_3D_Settings_(reference).htm
- NVIDIA forum, "New option (shader cache size)" **(community, title only)**: https://www.nvidia.com/en-us/geforce/forums/game-ready-drivers/13/473105/new-option-shader-cache-size/
- NVIDIA Profile Inspector (MIT, 2026-10-13) **(community)**: https://github.com/Orbmu2k/nvidiaProfileInspector, including README.md, NvapiDrsWrapper.cs, DrsDecrypterService.cs and CustomSettingNames.xml

AMD

- ADLX repo (2.0.0.125, 2026-09-21): https://github.com/GPUOpen-LibrariesAndSDKs/ADLX
- I3DSettings.h, I3DSettings1.h, I3DSettings2.h, I3DSettings3.h, IApplications.h, ADLXDefines.h, ADLXVersion.h: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/tree/main/SDK/Include
- ADLX docs in the repo (Programming Guide, IADLX3DAntiLag1, IADLX3DSettingsServices): https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/tree/main/SDKDoc
- ADLX SDK License Agreement: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/ADLX%20SDK%20License%20Agreement.pdf
- Anti-Lag 2 SDK README (v2.0.0a, 2024-10-01): https://github.com/GPUOpen-LibrariesAndSDKs/AntiLag2-SDK/blob/main/README.md
- ADL SDK 18.1, Application Profiles APIs (2025-04-11): https://github.com/GPUOpen-LibrariesAndSDKs/display-library
- AMD 23.10.2 release notes **(title only)**: https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-23-10-2.html
- AMD 23.10.1 release notes **(title only)**: https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-23-10-1.html
- AMD DH3-033, Configure Radeon Anti-Lag/Anti-Lag+, Radeon Boost and Radeon Chill **(title only)**: https://www.amd.com/en/resources/support-articles/faqs/DH3-033.html
- AMD DH3-012, Customize Graphics Settings **(title only)**: https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html

Intel

- IGCL repo, README and licence (v306, 2026-10-28): https://github.com/intel/drivers.gpu.control-library, https://github.com/intel/drivers.gpu.control-library/blob/master/License.txt
- igcl_api.h: https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h
- 3D feature sample: https://github.com/intel/drivers.gpu.control-library/blob/master/Samples/3D_Feature_Samples/3D_Feature_Sample_App.cpp

Anti-cheat

- CS2 on X, 13 October 2023 (text from the post's title): https://x.com/CounterStrike/status/1712875606776729832
- Tom's Hardware, AMD pulls driver **(community, title only)**: https://www.tomshardware.com/news/amd-pulls-drivers-that-cause-counter-strike-2-bans-after-valve-roasted-them
- Steam forum, bans to be reversed **(community, title only)**: https://steamcommunity.com/app/730/discussions/0/6993585599476390349/
- GGRecon, bans to be reversed **(community, title only)**: https://www.ggrecon.com/articles/counter-strike-2-bans-for-amd-anti-lag-users-set-to-be-reversed

Earlier notes in this repo

- overclocking-and-driver-tools (ADLX and IGCL licences): `git show origin/claude/project-thread-5ggkan:research/overclocking-and-driver-tools.md`
