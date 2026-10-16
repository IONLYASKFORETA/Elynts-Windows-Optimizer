# Driver detection, lookup, install and cleaning flow

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/22

Researched 2026-10-16. This builds on [overclocking-and-driver-tools.md](overclocking-and-driver-tools.md), which already covers the vendors' silent-install switches, the WU driver-exclusion policy, DDU's CLI flags, the `pnputil` syntax and NVCleanstall's feature list. That material is not repeated here. Each claim has a source URL. **Unverified** marks claims we could not check against a primary source or a live request.

## Answer: recommended flow

1. **Detect.**
   - Enumerate display adapters with CfgMgr32 (`CM_Get_DevNode_Property` with `DEVPKEY_Device_DriverVersion`), or with WMI `Win32_VideoController` (`Name`, `DriverVersion`, `PNPDeviceID`). Key everything on the PCI vendor ID: `10DE` is NVIDIA, `1002` is AMD, `8086` is Intel.
   - Convert NVIDIA's WDDM version to the marketing version from its last five digits. For example, `32.0.15.9174` becomes `591.74`. Alternatively, call NVAPI `NvAPI_SYS_GetDriverAndBranchVersion`.
   - For the chipset, read the installed "AMD Chipset Software" or "Intel Chipset Device Software" version from the Uninstall registry keys, and the platform devices' driver versions from `Win32_PnPSignedDriver` (unverified as a reliable signal; see details).
2. **Look up the latest version.** The answer differs per vendor. Every route is undocumented, so build each one as a swappable `IDriverSource` with a timeout, and fall back to "open the vendor page".
   - **NVIDIA:** `gfwsl.geforce.com/.../AjaxDriverService.php?func=DriverManualLookup`. It returns JSON with `Version`, `DownloadURL`, `IsCRD` and `ReleaseDateTime`. It needs a product-family ID (`pfid`), which NVIDIA only maps from the GPU *name*.
   - **AMD GPU:** scrape the minimal-setup filename on `amd.com/en/support/download/drivers.html`, then build `drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-<ver>-win11-c.exe`. Downloads need a `Referer: https://www.amd.com/...` header.
   - **AMD chipset:** read `drivers.amd.com/drivers/installer/chipset/version.txt`. It also needs the Referer header.
   - **Intel GPU:** parse the `meta-DownloadVersion` and `meta-RecommendedDownloadUrl` fields on Intel download page 785597.
   - **Intel chipset:** offer it only when the user asks. On Windows 11 it is an INF-only package, which matters little (unverified).
   - **winget has no GPU or chipset driver packages** for any of the three vendors (it only has DSA, DDU and NVCleanstall), so it is not a lookup route.
3. **Download and verify.** Check the Authenticode signature (WinVerifyTrust, with NVIDIA, AMD or Intel as the signer) on every download. For Intel, also check the SHA-512 published on its page. AMD publishes no hash.
4. **Install** in a child process from the elevated service:
   - NVIDIA: `setup.exe -s -n Display.Driver` (`-clean` for a clean install). Exit code 0 means OK; 1 means a reboot is needed.
   - AMD GPU: `Setup.exe -INSTALL` or `-install`.
   - AMD chipset: `/S`. Use `/CUSTOMINSTALL=PSP,SATA,…` to pick features.
   - Intel GPU: `Installer.exe -s --overwrite`.
   - Intel chipset: `SetupChipset.exe /exenoui /qn /norestart`.
   - Treat 3010, 1641 and NVIDIA's 1 as success with a reboot needed.
5. **Optional NVIDIA debloat,** done the way TinyNvidiaUpdateChecker (TNUC) and NVCleanstall do it:
   - Extract the `.exe` (it is a 7-Zip SFX).
   - Keep only `Display.Driver` plus `NVI2`, `setup.exe`, `setup.cfg`, `EULA.txt`, `license.txt` and `ListDevices.txt`. Optionally also keep `HDAudio`, `PhysX` or `NvCpl`. On laptops, always keep `NVPCF` and `PPC`.
   - Remove the `${{EulaHtmlFile}}`, `${{FunctionalConsentFile}}` and `${{PrivacyPolicyFile}}` entries from `setup.cfg`, or setup fails.
   - Blank the two `ProgressPresentation*Url` values in `NVI2/presentations.cfg`. These are installer telemetry and ads.
   - Run `setup.exe -s`.
   - **Write this ourselves:** TNUC is GPL-3.0 and NVCleanstall is proprietary with no CLI.
6. **Clean, only when needed** (switching vendor, or a broken install). The default is `pnputil /export-driver` followed by `/delete-driver oemNN.inf /uninstall /force`, in normal mode, run by us. Offer **DDU** as an "advanced, deep clean" option:
   - It is MIT-licensed, so redistributing it is legally fine, but downloading it at runtime is still better because it updates often.
   - `-Silent -CleanNvidia|-CleanAmd|-CleanIntel -Restart -PreventWinUpdate`.
   - **Safe Mode is recommended, not required.** DDU checks `BootMode` and only warns. It makes a restore point only in normal boot.
   - If we drive Safe Mode ourselves, use `bcdedit /set {current} safeboot minimal`. Add our service under `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal`, and always remove `safeboot` on the next boot. DDU uses a self-removing service for that step.

## 1. Detection

| What | API | Notes | Source |
|---|---|---|---|
| GPU driver version | `DEVPKEY_Device_DriverVersion` through `CM_Get_DevNode_Property` or `SetupDiGetDeviceProperty` | The value comes from the INF `DriverVer`. Read-only. | https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-driverversion |
| GPU name, version, PnP ID | WMI `Win32_VideoController` (`Name`, `DriverVersion`, `PNPDeviceID`) | Not deprecated. Non-WDDM hardware "returns inaccurate property values". `AdapterRAM` is `uint32`, so it is useless above 4 GB (our inference). | https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-videocontroller |
| NVIDIA marketing version | Last 5 digits of the WDDM version, with a dot after the third (TNUC: `rawVersion[^5..].Insert(3, ".")`), or NVAPI `NvAPI_SYS_GetDriverAndBranchVersion` | TNUC does the WMI query with an 8 s timeout and parses `VEN_`/`DEV_` from `PNPDeviceID`. | https://github.com/ElPumpo/TinyNvidiaUpdateChecker/blob/master/TinyNvidiaUpdateChecker/Handlers/GPUHandler.cs ; https://github.com/NVIDIA/nvapi/blob/main/nvapi.h |
| Laptop vs desktop | `Win32_SystemEnclosure` chassis type (as TNUC does) | Needed for the NVIDIA lookup (notebook vs desktop `pfid`) and to keep NVPCF/PPC. | GPUHandler.cs above |
| Chipset package version | Uninstall registry entries for "AMD Chipset Software" and "Intel Chipset Device Software", plus `Win32_PnPSignedDriver` for the AMD/Intel SYSTEM-class devices | **Unverified:** we found no vendor doc that names a canonical "installed chipset package version". The Chocolatey AMD package uses a marker file instead. | https://community.chocolatey.org/packages/amd-ryzen-chipset |

## 2. Latest-version lookup

### NVIDIA: AjaxDriverService (undocumented, works today)

- **Endpoint:** `https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&pfid=<id>&osID=<id>&dch=1&numberOfResults=10&languageCode=1078`. TNUC uses it with a browser User-Agent. Source: https://github.com/ElPumpo/TinyNvidiaUpdateChecker/blob/master/TinyNvidiaUpdateChecker/MainConsole.cs and `Handlers/OldMetadataHandler.cs` in the same repo.
- **Verified live on 2026-10-16.** `pfid=1066&osID=57` returned `Success`, an `IDS[].downloadInfo` array with `Version` (617.42), `ReleaseDateTime`, `DownloadURL` (`…/Windows/617.42/617.42-desktop-win10-win11-64bit-international-dch-whql.exe`), `IsCRD`, `IsBeta`, `IsWHQL`, `IsFeaturePreview`, `DownloadURLFileSize` and `ReleaseNotes`.
  - `IsCRD` is 0 for Game Ready and 1 for Studio (per TNUC's comment).
  - Download URLs follow the pattern `international.download.nvidia.com/Windows/<ver>/<key>.exe`. Source: TNUC `NewMetadataHandler.cs`.
- **Fragility:**
  - The `pfid` is a *product family* ID that the endpoint does not derive from the PCI device ID. TNUC maps GPU name to `pfid` using a community JSON (https://github.com/ZenitH-AT/nvidia-data). It also has an "experimental" source that does not need `pfid` (`https://gpu.me/v1/index2.json`, a TechPowerUp-backed feed).
  - TNUC's own code says its type mapping "will break if NVIDIA changes their naming scheme".
  - **Licence:** NVIDIA publishes no terms for the endpoint. TNUC is **GPL-3.0**, so read it for the technique but do not copy its code into our closed app. Source: https://github.com/ElPumpo/TinyNvidiaUpdateChecker/blob/master/LICENSE
- **Mitigation:** ship our own small name-to-`pfid` table and refresh it from our update channel. Simpler still: because every GeForce desktop GPU on the current branch gets the same package, query one known `pfid` per branch (desktop or notebook) and compare versions. **Unverified:** that the per-family results never differ within a branch.

### AMD GPU (Adrenalin): page scrape plus a Referer-gated CDN

- `drivers.amd.com` answers a request without an AMD Referer with **302 → "Download-Incomplete.html"**. We verified this on 2026-10-16 against `drivers.amd.com/drivers/installer/chipset/version.txt`.
- The drivers page currently links `drivers.amd.com/drivers/installer/26.10/whql/amd-software-adrenalin-edition-26.8.1-minimalsetup-260818_web.exe`. The version (26.8.1) comes from the filename. Source: https://www.amd.com/en/support/download/drivers.html
- **Full package:** `https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-<ver>-win11-c.exe`, downloaded with `Referer: https://www.amd.com/en/support/download/drivers.html`. Install with `-install`. Source: https://push.chocolatey.org/packages/amd-software-adrenalin-edition
- **Pick the `-c` combined package.** The `-a` and `-b` split packages cover different hardware: RDNA2 and Ryzen desktop iGPUs versus RDNA3/4. A source that chose the `-b` package left Ryzen iGPUs without a driver at 26.9.1. AMD publishes no checksum, so verify the Authenticode signature. Source: https://github.com/sbj-proshop/unattended-driver-install/pull/1
- **Fragility:** high. The filename scheme, the page markup and the Referer rule are all undocumented. Fall back to the minimal-setup web installer (the auto-detect tool), which covers both "Radeon Graphics and Ryzen chipsets" (same AMD page).

### AMD chipset

- **Version feed:** `https://drivers.amd.com/drivers/installer/chipset/version.txt`, with the package at `.../AMD_Chipset_Software.exe` in the same folder (Referer-gated). Source: https://github.com/sbj-proshop/unattended-driver-install/pull/1. The file contents are **unverified**, because our fetch was redirected without a Referer.
- **Versioned URL example:** `https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe`, released 2026-08-14. Source: https://community.chocolatey.org/packages/amd-ryzen-chipset
- **Official silent switches:**
  - `/S` installs.
  - `/S /EXPRESSUNINSTALL=1` uninstalls.
  - `/CUSTOMINSTALL=<features>` and `/CUSTOMUNINSTALL=` select features such as `PSP`, `SATA` and `SMBUS`.
  - Source: https://www.amd.com/en/resources/support-articles/release-notes/amd-ryzen--chipset-driver-release-notes--2-04-04-111-.html
- Install only on Ryzen platforms. The installer fails elsewhere, per the Chocolatey package above.

### Intel GPU (Arc / Iris Xe)

- Page https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html carries front-matter fields:
  - `meta-DownloadVersion` (e.g. `32.0.101.9150 WHQL Certified`)
  - `meta-RecommendedDownloadUrl` (`https://downloadmirror.intel.com/930514/gfx_win_101.9150.exe`)
  - `meta-LastUpdate`
  - The **SHA-512** appears only in the visible body text.
- **Fragility:** medium. It is a page scrape, but the fields are machine-oriented metadata.
- **Intel DSA is not automatable.** It has a winget package (`Intel.IntelDriverAndSupportAssistant`, proprietary, installer `https://dsadata.intel.com/installer`). Source: https://github.com/microsoft/winget-pkgs/tree/master/manifests/i/Intel/IntelDriverAndSupportAssistant
  - Intel declined an enterprise or automation request: the feature "will not be addressed by our developer team this year". Source: https://community.intel.com/t5/Graphics/Intel-DSA-for-Enterprise/td-p/1535741
  - We found no CLI or API. Use it only as a "let Intel handle it" link-out.

### Intel chipset (INF utility)

- `SetupChipset.exe /exenoui /qn /norestart` installs silently. `ONLY_EXTRACT=<dir>` extracts the INFs. Source: https://downloadmirror.intel.com/916808/Readme.txt (the readme for the server build, installer 3.1.7.285).
- **Unverified:** we did not check that the client build has the same switches, or that the INF-only package changes anything on Windows 11, where the inbox INFs already name the devices.

### winget

- Checked microsoft/winget-pkgs at the 2026-10-16 head.
- Publisher **Nvidia** has CUDA, PhysX, FrameView and similar, but **no display driver and no NVIDIA App**.
- **AMD** has only `AMDSoftwareCloudEdition` (an Azure V620 VM driver, 23.Q3).
- **Intel** has DSA and XTU, but no graphics or chipset driver.
- `TechPowerUp.NVCleanstall` (proprietary, Inno) and `Wagnardsoft.DisplayDriverUninstaller` (MIT, NSIS) exist.
- **Conclusion:** winget is not a driver lookup route. Source: https://github.com/microsoft/winget-pkgs/tree/master/manifests

## 3. Silent install: extra points beyond the earlier note

- **Exit codes:**
  - NVIDIA returns 0 for OK and 1 for OK with a reboot needed. Source: https://docs.nvidia.com/datacenter/tesla/driver-installation-guide/windows.html
  - The Chocolatey AMD chipset example treats `0, 1605, 1614, 1641, 3010` as success. Those codes are its own assumption, so they are unverified for AMD. Source: https://community.chocolatey.org/packages/amd-ryzen-chipset
  - The Chocolatey Adrenalin package accepts only 0. Source: https://push.chocolatey.org/packages/amd-software-adrenalin-edition
- **TNUC invokes the full NVIDIA package with `/s /noreboot`.** Source: TNUC `Forms/ReadyInstallForm.cs`. NVIDIA's documented form is `-s`.
- **AMD uninstall:** the `-uninstall` switch is reported as not working silently. Source: https://push.chocolatey.org/packages/amd-software-adrenalin-edition. Use pnputil, DDU or the AMD Cleanup Utility instead.

## 4. NVIDIA component stripping

TNUC's `MakeInstallerCore` implements the technique, and it matches what NVCleanstall offers:

1. **Extract** with 7-Zip, NanaZip or WinRAR. Require `setup.exe` and a `setup.cfg` whose root element is `<setup>`.
2. **Find components** by scanning each subfolder for its `.nvi` manifest. Dependencies are listed as `<dependencies><package type="requires" package="…"/>`. Some names need aliasing, e.g. `Display.NvApp.NvCPL` → `NvCpl` and `NvContainer.*` → `NvContainer`.
3. **Choose what to keep.**
   - **Kept:** the chosen components plus `NVI2`, `EULA.txt`, `license.txt`, `ListDevices.txt`, `setup.cfg` and `setup.exe`.
   - **Typically dropped:** `NvApp`, `NvApp.MessageBus`, `NvBackend`, `NvContainer`, `GFExperience*`, `NvTelemetry` (on older drivers), `Display.Update`, `FrameViewSDK`, `NvModuleTracker`, `NvVAD`, `ShadowPlay` and `PPC` (on desktop only).
   - **Laptops:** always keep `NVPCF` (Dynamic Boost, RTX 30+ laptops) and `PPC`.
   - `NvCpl` is the standalone legacy Control Panel.
4. **Edit `setup.cfg`.** Delete the `/setup/manifest/file[@name="${{EulaHtmlFile}}"]`, `${{FunctionalConsentFile}}` and `${{PrivacyPolicyFile}}` nodes. Otherwise "the installer throws error codes".
5. **Edit `NVI2/presentations.cfg`.** Set `ProgressPresentationUrl` and `ProgressPresentationSelectedPackageUrl` to `""`. The code comments call these telemetry and installer ads.

- **Sources:**
  - https://github.com/ElPumpo/TinyNvidiaUpdateChecker/blob/master/TinyNvidiaUpdateChecker/MainConsole.cs
  - https://github.com/ElPumpo/TinyNvidiaUpdateChecker/blob/master/TinyNvidiaUpdateChecker/Handlers/ComponentHandler.cs
  - Both were read at commit `81582239`, dated 2026-10-10.
- **Fragility:** medium. Component folder names and the `setup.cfg` EULA entries change between driver branches. TNUC added the EULA fix after installs broke. Do not hard-code the component names: drive the list from the `.nvi` dependency graph, and keep an allowlist of what to drop.
- **Licence:** NVCleanstall is proprietary (winget `License: Proprietary`, Inno `/VERYSILENT`) and has no documented CLI, so we cannot drive it. Sources: https://www.techpowerup.com/nvcleanstall/ and https://github.com/microsoft/winget-pkgs/tree/master/manifests/t/TechPowerUp/NVCleanstall
  - TNUC is GPL-3.0. Reimplement the technique; do not copy its code.
  - Modify the NVIDIA package only on the user's machine. Never redistribute a modified package; NVIDIA's licence terms for that were not reviewed.

## 5. Cleaning: DDU vs pnputil

| | pnputil (default) | DDU (advanced) | AMD Cleanup Utility |
|---|---|---|---|
| **Licence** | Built into Windows | **MIT**. LICENSE read from the `WPF` branch at commit `bbb8ddf`, dated 2026-10-05. Redistribution and automation are allowed if we keep the notice. https://github.com/Wagnard/display-drivers-uninstaller/blob/WPF/LICENSE | AMD freeware. No CLI is documented. https://www.amd.com/en/resources/support-articles/faqs/GPU-601.html |
| **Depth** | Driver-store packages and devices only | Also vendor services, folders, registry and caches. Extra flags in the current source go beyond the earlier note: `-cleanallgpus`, `-RemoveNvidiaDirs`, `-RemoveAMDDirs`, `-RemoveVulkan`, `-RemoveIntelNpu`, `-CleanQualcomm`, `-CleanLisuan`, `-RemoveAudioBus`, `-removemonitors`, `-NoSetupAPI`, `-KeepNVCPopt`. These were grepped from the source and may be undocumented. | AMD graphics and audio only; not the chipset |
| **Safe Mode** | Not needed | **Recommended, not enforced.** The code checks `SystemInformation.BootMode`. In normal mode it logs "We are not in Safe Mode" and shows a notice unless run with `-Silent`. Source: `FrmMain.xaml.vb` | Offers a reboot into Safe Mode and "recommend[s]" it |
| **Restore point** | We create it (and `/export-driver` first) | Created only when `CreateRestorePoint` is set, the boot is normal, and `-NoRestorePoint` is absent. Source: `FrmMain.xaml.vb` `SystemRestore()` | Always creates one (per AMD) |
| **Safe-boot handling** | — | Adds its services under `SafeBoot\Minimal` or `SafeBoot\Network`. Installs a `DDUSafeBootService` that runs `bcdedit` up to 3 times to delete `safeboot`, so the PC cannot get stuck booting into Safe Mode. Deletes `SafeBoot\Option`. Sources: `CleanupEngine.vb`, `DDUSafeBootService.vb` | — |

- DDU source: https://github.com/Wagnard/display-drivers-uninstaller/tree/WPF/display-driver-uninstaller
- **Latest DDU on winget:** 18.1.5.3 (2026-05-10). The earlier note found 18.1.6.1 on Wagnardsoft's site, so winget lags. Download from https://www.wagnardsoft.com/DDU/download and pin the publisher signature (unverified: that the binaries are Authenticode-signed).
- **Recommendation:**
  - Use pnputil in normal mode for same-vendor reinstalls.
  - Use DDU with `-Silent -Clean<Vendor> -PreventWinUpdate -Restart` for vendor switches or broken installs, and let the user opt in to Safe Mode.
  - If we automate Safe Mode, copy DDU's approach: register a self-removing "undo safeboot" step that runs first on the next boot. A failure here leaves the PC in a Safe-Mode boot loop, which is the main risk of this feature.
