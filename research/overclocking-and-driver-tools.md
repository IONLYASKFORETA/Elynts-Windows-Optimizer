# Survey overclocking and driver tools

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/8

Researched 2026-10-15. Each claim has a source URL. "Unverified" marks claims that come only from community or third-party sources, or where the primary page could not be read.

## Answer

- **GPU tuning from a third-party app works for AMD and Intel. It does not work for NVIDIA through any public API.**
  - AMD **ADLX** (it replaces ADL) has supported, range-checked calls for GPU clocks, voltage, VRAM, fan and power limit. It also has calls for auto-overclock and auto-undervolt, and for reset to factory.
  - Intel **IGCL** has calls for frequency, voltage offset, power and temperature limits, VRAM and fan on Arc. These calls work only after the app calls a warranty waiver function, and the app must first show the user a warning popup.
  - **NVIDIA's public NVAPI is read-only for clocks, voltage and power.** Setting them needs an NDA build of NVAPI, which we will not get. NVIDIA's own one-click auto-tuning lives in the NVIDIA App, and it has no documented API.
  - Recommendation: for NVIDIA, link out to the NVIDIA App's Automatic Tuning, or skip NVIDIA GPU overclocking in v1.
- **CPU tuning from Windows is mostly not open to third-party apps.**
  - The **Ryzen Master Monitoring SDK is read-only.** PBO and Curve Optimizer can be changed only in Ryzen Master itself or in the BIOS.
  - **Intel XTU has no public SDK or CLI** that we could find. Undervolting is locked by Undervolt Protection (on 12th gen and newer) and by the Plundervolt BIOS fixes (on 6th to 10th gen). XTU will not start at all when VBS is on, unless UVP is enabled.
  - Recommendation: the app should *guide* users to the BIOS or the vendor tool, not write CPU voltages itself.
- **RAM: XMP/EXPO is effectively a BIOS setting.** Intel says profiles load "using BIOS or a specific tuning application", meaning a vendor tool. We should not try to set them from our app.
- **Low-level drivers: do not ship WinRing0 or inpoutx64.**
  - **WinRing0** is flagged by Defender as `HackTool:Win32/Winring0`. The major tools (LibreHardwareMonitor, FanControl, OpenRGB) have moved to **PawnIO**.
  - PawnIO is signed and GPL-2.0 with an exception for programs that talk to it over IOCTL. However, **FACEIT has blocked PawnIO** because of its signing certificate.
- **Drivers: use the official installers' silent switches, run in a child process.**
  - NVIDIA: `setup.exe -s -n Display.Driver`. Community reports also use `-clean -noreboot -noeula`.
  - AMD: `Setup.exe -INSTALL [-boot] [-LOG]`.
  - Intel: `Installer.exe -s --overwrite`.
  - None of the three vendors has a documented "latest driver" lookup API (unverified for NVIDIA, where only community scrapers exist).
  - NVCleanstall-style stripping is possible by trimming the extracted package, but it is not supported by NVIDIA.
  - To stop Windows Update from replacing the driver, set the `ExcludeWUDriversInQualityUpdate` policy.
- **Driver cleaning: DDU's source is MIT-licensed and has a CLI.**
  - Examples: `-Silent -CleanNvidia -Restart -NoRestorePoint` and others.
  - Recommendation: download DDU from Wagnardsoft at runtime, the way the Chocolatey package does, instead of bundling it.
  - `pnputil /enum-drivers /class Display` plus `pnputil /delete-driver oemNN.inf /uninstall /force` is the built-in fallback that keeps everything inside our app.

## GPU tuning

### NVIDIA NVAPI (public)

- **Licence:** the NVAPI libraries are MIT-licensed. Source: https://github.com/NVIDIA/nvapi/blob/main/License.txt. The repo and docs are at https://github.com/NVIDIA/nvapi and https://developer.nvidia.com/nvapi.
- **What it controls:**
  - The P-state group has only getters: `NvAPI_GPU_GetPstates20`, `GetCurrentPstate`, `GetDynamicPstatesInfoEx`, and the deprecated `GetPstatesInfoEx`. There is no `SetPstates20`. The structures report whether a P-state or clock domain is "overclockable", but nothing documents how to change it. Source: https://docs.nvidia.com/nvapi/group__gpupstate.html
  - We grepped the public `nvapi.h` on 2026-10-15. Its only clock, P-state and thermal entry points are `GetAllClockFrequencies`, `GetCurrentPstate`, `GetDynamicPstatesInfoEx`, `GetPstates20`, `GetPstatesInfoEx` and `GetThermalSettings`. There are no setters for clock, voltage, power or cooler. Source: https://github.com/NVIDIA/nvapi/blob/main/nvapi.h
  - The public SDK does cover driver settings (DRS profiles), display and monitoring. Source: https://developer.nvidia.com/nvapi
- **NDA parts:** developers ask NVIDIA for an "NVAPI NDA edition" to get P-state, clock, undervolt and fan control, and the thread has no public answer. Source: https://forums.developer.nvidia.com/t/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/79626
  - Unverified: tools such as Afterburner use undocumented or NDA entry points. Plan as though we have no access to them.
- **Safety limits:** not applicable, because the public API exposes no setters.

### NVIDIA built-in auto-tuning (NVIDIA App)

- **How it works:** NVIDIA App → System → Performance → Automatic Tuning. It runs a scan of about 10–20 minutes, then applies a "safe overclock". It also runs periodic re-check scans. Users can set targets for voltage, power, temperature and fan. It is for GeForce RTX desktops and laptops. Sources:
  - https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-update-av1-performance-tuning/
  - https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/
- **Third-party API:** none documented. Our app can only point the user to it.

### AMD ADLX (and legacy ADL)

- **Status:** ADL is "Now superceded by ADLX", and AMD says to use ADLX for new apps. Source: https://gpuopen.com/adl/
- **Repo and docs:** https://github.com/GPUOpen-LibrariesAndSDKs/ADLX and https://www.gpuopen.com/adlx. ADLX is forward- and backward-compatible with AMD drivers and runs on Windows 10 and newer.
- **What it controls.** Function names come from the SDK headers in `SDK/Include` of the ADLX repo:
  - **`IADLXGPUTuningServices`:** `IsAtFactory`, `ResetToFactory`, and `IsSupported…` checks for AutoTuning, PresetTuning, ManualGFX, ManualVRAM, ManualFan and ManualPower. Source: `IGPUTuning.h`
  - **Auto tuning:** `StartUndervoltGPU`, `StartOverclockGPU`, `StartOverclockVRAM`, plus completion events. Source: `IGPUAutoTuning.h`
  - **Manual GFX:** `Set/GetGPUMin/MaxFrequency` and `SetGPUVoltage`, each with `…Range` and `…Default` getters, plus `IsValidGPUTuningStates`. Source: `IGPUManualGFXTuning.h`
  - **Manual power:** `SetPowerLimit` and `SetTDCLimit`, each with `…Range` and `…Default`. Source: `IGPUManualPowerTuning.h`
  - **Manual VRAM:** max VRAM frequency and memory-timing presets. Source: `IGPUManualVRAMTuning.h`
  - The GPUOpen reference pages exist, for example https://gpuopen.com/manuals/adlx/adlx-sdk-references/adlx-interfaces/gpu-tuning/iadlxmanualpowertuning, but they are JavaScript-rendered and our fetch got no body text.
- **Safety limits:** every setter has a driver-reported `Range` and `Default`, a validity check, and a factory reset. This gives us a clean way to undo changes.
- **Licence:** AMD's "SDK License Agreement (ADLX SDK)" in the repo root.
  - It allows distributing sample code and derivative works **in object-code form only**, inside our software, "for use with Your products that work with or include AMD processors". Distribution must be under an end-user licence that meets the agreement's Section 3.
  - It is revocable and includes a confidentiality clause.
  - It is fine for a closed binary. It is awkward for an open-source repo that copies ADLX sample code verbatim. Unverified: the runtime DLL ships with the AMD driver, which the "compatible with AMD drivers" wording suggests.
  - Source: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/ADLX%20SDK%20License%20Agreement.pdf

### Intel IGCL (Intel Graphics Control Library, Arc)

- **Status:** IGCL is Intel's "latest recommended SDK" and replaces the OEM-only CUI SDK. Sources:
  - https://www.intel.com/content/www/us/en/support/articles/000097877/graphics.html
  - https://github.com/intel/drivers.gpu.control-library
- **Runtime:** the IGCL binaries ship inside the Intel graphics driver. The repo provides the headers and a wrapper. The performance and telemetry APIs are 64-bit only. Source: https://github.com/intel/drivers.gpu.control-library
- **What it controls.** Function names come from `include/igcl_api.h`:
  - `ctlOverclockGpuFrequencyOffsetSet(V2)` and `ctlOverclockGpuVoltageOffsetSet`
  - `ctlOverclockGpuMaxVoltageOffsetSetV2` and `ctlOverclockPowerLimitSet(V2)`
  - `ctlOverclockTemperatureLimitSet(V2)`
  - `ctlOverclockVramFrequencyOffsetSet`, `ctlOverclockVramVoltageOffsetSet` and `ctlOverclockVramMemSpeedLimitSetV2`
  - `ctlOverclockWriteCustomVFCurve` and `ctlOverclockGpuLockSet`
  - `ctlOverclockResetToDefault`
  - `ctlFanSet{Default,FixedSpeed,SpeedTable}Mode` and `ctlPowerSetLimits`
- **Safety limits:**
  - **`ctlOverclockWaiverSet` is required** before most overclock calls. The header says apps must "notify each user at least once with a popup of the dangers and requiring acceptance". The user's choice may be cached.
  - Error codes reject out-of-range voltage, frequency, power, temperature and VRAM speed, and invalid VF curves. Some changes return "reset required".
  - Sources: https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h and https://intel.github.io/drivers.gpu.control-library/Control/api.html
- **Licence:** Intel Software License Agreement. We may "use and redistribute" the Control API software and headers "solely for use on Intel platforms", provided we keep the notice. Reverse engineering of the binaries is not allowed. Source: https://github.com/intel/drivers.gpu.control-library/blob/master/License.txt

## CPU tuning

### AMD Ryzen Master and its SDK

- **The SDK is read-only.** It provides "read-only access to valuable processor metrics", runs on Windows 10/11 x64, and should be polled at most once per second. Source: https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html
  - Licence: the page states no redistribution terms. The SDK has had permission-related CVEs. Source: https://www.amd.com/en/resources/product-security/bulletin/amd-sb-9004.html
- **Ryzen Master itself** can tune PBO, Curve Optimizer, cores, iGPU and memory timings/EXPO parameters, "on the fly or set through the BIOS". Settings made in the BIOS "remain effective across system restarts". Source: https://www.amd.com/en/products/software/ryzen-master.html
  - AMD says using PBO "invalidates the AMD product warranty". Same source.
- **Curve Optimizer:** per-core or all-core, with automatic value derivation for the CPU only. Turning it off may need a restart. Source: https://www.amd.com/content/dam/amd/en/documents/products/software-tools/faq-curve-optimizer.pdf
- **No public write API.** We found no supported way for a third party to set PBO or Curve Optimizer from Windows. The community route (the SMU over PawnIO's `RyzenSMU` module, https://github.com/namazso/PawnIO.Modules) is undocumented by AMD. Our recommendation: tell users to do it in the BIOS or Ryzen Master.

### Intel XTU

- **No public SDK or CLI found.**
  - Forum questions about the "Intel Overclocking SDK" and about the removed `XTUCli.exe` got no answer from Intel. Sources:
    - https://community.intel.com/t5/Software-Archive/Intel-Overclocking-SDK/m-p/1167130
    - https://community.intel.com/t5/Processors/Run-XTU-programmatically-or-from-CLI/m-p/1315189
  - The release notes mention no SDK. Source: https://downloadmirror.intel.com/873886/XTU-7.14.2.93-ReleaseNotes.pdf
- **Supported platforms:** desktop, high-end mobile and HEDT. Servers are not supported. Same release notes.
- **Undervolt Protection (UVP, 12th gen and newer):**
  - It is on by default. Voltage cannot go below the boot-time voltage, and XTU ignores negative offsets past that point.
  - It can be disabled only in the BIOS, only on some K/KF/KS/X CPUs with Z/X chipsets.
  - Source: https://intel.com/content/www/us/en/support/articles/000094219.html
- **VBS:**
  - XTU "is unable to start" with VBS unless UVP is enabled in the BIOS.
  - On 11th gen and older there is no fix, so Intel says to overclock in the BIOS.
  - Source: https://www.intel.com/content/www/us/en/support/articles/000093813.html
  - Windows 11 turns memory integrity (HVCI) on by default on most new devices. Source: https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules
- **Plundervolt (CVE-2019-11157, INTEL-SA-00289):**
  - It affects 6th–10th gen Core and some Xeons.
  - The fix is a BIOS/microcode update. Per press reports, OEM BIOSes then lock voltage control (unverified against Intel's own page, which only says "update BIOS").
  - Sources: https://www.intel.com/content/www/us/en/security-center/advisory/intel-sa-00289.html and https://www.pcworld.com/article/398498/plundervolt-attack-against-intel-core-cpus-prompts-fix-that-disables-cpu-voltage-settings.html

### What needs the BIOS

These need the BIOS on Intel or AMD:
- Persistent PBO and Curve Optimizer (Ryzen Master changes persist only when set in the BIOS)
- Disabling UVP
- Undervolting on Plundervolt-patched systems
- Any overclocking on pre-12th-gen Intel with VBS on
- XMP/EXPO enablement

The sources are the AMD and Intel pages cited above.

## RAM (XMP / EXPO)

- **Intel:** XMP profiles load "using BIOS or a specific tuning application". For the right utility, Intel tells you to ask the board vendor. Source: https://www.intel.com/content/www/us/en/support/articles/000094616/processors.html
- **AMD:** Ryzen Master lists "EXPO memory parameters" and memory timings. Source: https://www.amd.com/en/products/software/ryzen-master.html
- **Verdict:** not strictly BIOS-only, because vendor tools can apply profiles. However, no public API exists, and persistent enablement is a BIOS or firmware setting. Our app should detect the setting and explain it, not toggle it.

## Low-level access drivers

| Driver | Status | Licence | Defender / anti-cheat |
|---|---|---|---|
| **WinRing0** (`WinRing0x64.sys`) | Abandoned. Gives broad MSR/IO/physical-memory access. Linked to CVE-2020-14979 (EVGA PX1). https://github.com/Rem0o/FanControl.Releases/discussions/3017 | — | Defender flags it as `HackTool:Win32/Winring0` (from about March 2025, same source). The Microsoft vulnerable-driver blocklist is on by default since the Windows 11 2022 update and is enforced under HVCI and Smart App Control. https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules. **Do not ship it.** |
| **PawnIO** | A "scriptable universal kernel driver". The official build is signed, and the official modules are signed builds. https://pawnio.eu/ and https://github.com/namazso/PawnIO.Modules. LibreHardwareMonitor switched to it (https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/commit/eb5e1a20be996d4865170b13bab97af43d97f341, about Sep 2025), as did FanControl v239 and OpenRGB (https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/2885). Modules include IntelMSR, RyzenSMU, SMBus, LpcIO and others. | Driver: GPL-2.0 with an exception for independent programs that use only the IOCTL interface (https://github.com/namazso/PawnIO). Modules: LGPL-2.1. Distribution is through the PawnIO.Setup installer: https://github.com/namazso/PawnIO.Setup/releases | **FACEIT blocks it** because its signing certificate is shared with cheat software. No confirmed fix yet. https://github.com/namazso/PawnIO.Setup/issues/1 and https://github.com/Rem0o/FanControl.Releases/discussions/3537. No reports either way for Vanguard, EAC or BattlEye. **Use PawnIO if we need ring-0, and warn FACEIT users.** |
| **inpoutx64** (Highresolution Enterprises) | Generic port-I/O driver, possibly with physical-memory access. HWiNFO's author calls it "Not very safe". https://www.hwinfo.com/forum/threads/inpoutx64-sys-driver-and-hwinfo.7234/ | Freeware (unverified; the vendor site could not be fetched) | ESET flags it as a "potentially dangerous application". https://forum.eset.com/topic/40479-inpoutx64sys-potentially-dangerous-application/. **Do not ship it.** |

Recommendation: prefer the vendor APIs (ADLX, IGCL), which run in user mode through the vendor's own signed driver. Load PawnIO only when the user opts in.

## Driver installs

| Vendor | Official source | Silent switches | Notes |
|---|---|---|---|
| NVIDIA | https://www.nvidia.com/en-us/drivers/details/ (web lookup). NVIDIA App auto-update: https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/ | Documented: `setup.exe -s -n Display.Driver`, `-log:` and `-loglevel:`. Exit codes: 0 means OK, 1 means OK but reboot needed. Silent uninstall: `RunDll32 NVI2.DLL,UninstallPackage Display.Driver -silent -n`. Source: https://docs.nvidia.com/datacenter/tesla/driver-installation-guide/windows.html. Community-reported: `-s -clean -noreboot -noeula`, and deleting the `NvApp` files except the EULA folders to skip the NVIDIA App. https://forums.developer.nvidia.com/t/nvidia-geforce-gameready-studio-driver-silent-deployment-parameters-that-exclude-nvidia-app/372634 | No documented "latest version" API. Community scrapers use undocumented endpoints (unverified). Example: https://git.blob42.xyz/Archives/nvidia-patch/src/commit/8a481562428549eda7eab03905b0c0e8541b7ec7/tools/nv-driver-locator |
| AMD | Auto-Detect tool and product selector: https://www.amd.com/en/resources/support-articles/faqs/RSX2-INSTALL.html. Install types: Default, Minimal or Driver Only. Factory Reset optionally pauses Windows Update. | `Setup.exe -INSTALL [-boot] [-LOG file] [-USE path] [-OUTPUT screen\|detail]`, plus `-UNINSTALL ALL` and `-CFG`. Source: https://drivers.amd.com/relnotes/command-line-installation.pdf | No public version API found. |
| Intel | Arc driver page: https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html. The Intel Driver & Support Assistant handles updates. | `Installer.exe --overwrite -s`. Use `--help` for the full list. Source: https://www.intel.com/content/www/us/en/support/articles/000006773/graphics.html | The installer has a clean-install option. Intel recommends DDU when a different vendor's driver was installed before. https://www.intel.com/content/www/us/en/support/articles/000098031/graphics.html |

- **NVCleanstall-style stripping (TechPowerUp):**
  - It is free. It removes components such as Telemetry, USB-C and 3D Vision, including ones the official custom install won't let you deselect. It saves the package for offline install.
  - No licence or CLI is stated, so we cannot redistribute it. Source: https://www.techpowerup.com/nvcleanstall/
  - We could do the same by extracting the NVIDIA package and pruning component folders before `setup.exe -s`. NVIDIA does not support this, and the NVIDIA driver licence terms on redistributing modified packages were not checked, so modify only on the user's machine.
- **Windows Update driver delivery:**
  - Vendors publish drivers as "Automatic" or "Manual". Automatic drivers install through the daily scan. Manual drivers appear only under Optional updates (Windows 10 2004 and later). Source: https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/understanding-windows-update-automatic-and-optional-rules-for-driver-distribution
  - To stop WU from replacing our driver, set the policy `HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate\ExcludeWUDriversInQualityUpdate=1`. The GPO is "Do not include drivers with Windows Updates". It is documented for Pro and Enterprise; Home is not listed. Source: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update
  - The setting is easy to undo (delete the value).

## Driver cleaning

### DDU (Display Driver Uninstaller, Wagnardsoft)

- **Licence:** the source repo is labelled **MIT**: https://github.com/Wagnard/display-drivers-uninstaller. The Chocolatey package links its LICENSE.md, and its install script **downloads DDU from wagnardsoft.com at install time instead of bundling it**: https://community.chocolatey.org/packages/ddu/18.0.7
  - We could not fetch the LICENSE.md file directly, and the official site states no redistribution terms. Source: https://www.wagnardsoft.com/DDU/download
  - The latest version is 18.1.6.1, per the same download page.
  - Recommendation: download it at runtime from Wagnardsoft, and give credit.
- **CLI flags, per the developer:**
  - General: `-Silent`, `-Restart`, `-Shutdown`, `-NoSafeModeMsg`, `-cleancomplete`, `-PreventWinUpdate`, `-NoRestorePoint`
  - Vendor cleaning: `-CleanNvidia`, `-CleanAmd`, `-CleanIntel`, `-CleanRealtek`, `-CleanSoundBlaster`
  - Extras: `-RemovePhysx`, `-RemoveGFE`, `-RemoveNVBROADCAST`, `-RemoveNVCP`, `-RemoveINTELCP`, `-RemoveAMDCP`
  - Source: https://www.wagnardsoft.com/forums/viewtopic.php?p=8253
- **Safety guidance:**
  - Safe Mode is "HIGHLY recommend[ed]" but optional.
  - Disconnect from the internet, or use DDU's option to block WU driver downloads.
  - Back up data and keep the BitLocker recovery key ready.
  - Source: https://www.wagnardsoft.com/content/How-use-Display-Driver-Uninstaller-DDU-Guide-Tutorial
  - For reversibility, our app should create its own restore point before running DDU (DDU's `-NoRestorePoint` flag suggests DDU makes one by default; unverified).

### pnputil (built into Windows)

- **Commands:**
  - `pnputil /enum-drivers [/class Display] [/files]`, where `/class` needs Windows 11 21H2 and `/files` needs 22H2
  - `pnputil /delete-driver oemNN.inf [/uninstall] [/force] [/reboot]`
  - `pnputil /remove-device … [/class] [/force]` (Windows 11 22H2)
  - `pnputil /export-driver oemNN.inf <dir>`, which gives a backup for rollback
  - `pnputil /add-driver *.inf /install`
  - Source: https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax
- **Tradeoff:** it removes driver-store packages and devices, but it does not clean the vendor's leftover registry keys, services or folders the way DDU does. It is fully supported and needs no third-party binary. Pair `/export-driver` before deleting to keep the change reversible.
