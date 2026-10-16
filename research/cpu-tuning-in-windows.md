# Find safe in-Windows CPU tuning routes

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/14

Researched 2026-10-16. This file follows on from `research/overclocking-and-driver-tools.md`, which covers the PawnIO and WinRing0 background. Sources are listed per claim. Labels:

- **(community)**: only community tools, forums or press back the claim.
- **(inference)**: my own reading, with no source saying it.
- **(unverified)**: plausible, but it needs a test on real hardware.

Code facts about PawnIO modules, ZenStates-Core, RyzenAdj and UXTU come from reading the source of the official repositories as of 2026-10-16. Each one has a file path.

## Answer

- **Every in-Windows CPU tuning route is runtime only.** Neither AMD SMU mailbox writes nor Intel MSR writes survive a reboot. The PBO2 Tuner guide also says they are lost on sleep/wake. This works in our favour: **a crash or power-cycle always returns the CPU to the BIOS settings**, so the only crash-loop risk is our own re-apply at logon. The crash-detection rule must skip that re-apply.
- **PawnIO ships both modules we would need, and both are signed:**
  - `RyzenSMU`: send-SMU-command since Nov 2025, Zen 1 to Zen 5, desktop and mobile.
  - `IntelMSR`: MSR write since Mar 2026. Writes are allowlisted to 0x150 (OC mailbox), 0x610 (package power limit), 0x601, 0x607/0x608 and 0x1A4.
  - `IntelMCHBAR` is **read-only**, so we cannot write the MMIO power limit.
  - **`RyzenSMU` lets through any message ID from 0 to 0xFF, with no allowlist.** All safety therefore has to live in our service.
- **Ship (opt-in, behind a "PawnIO required, FACEIT blocks it" warning):**
  1. **AMD Curve Optimizer, negative only**, on Ryzen 5000/7000/9000 desktop, including X3D. The cap is **-15 all-core by default and -30 in Advanced** (never -50, even where the platform allows it), and it must pass a stress test before it auto-applies.
  2. **AMD power limits, reduce only.** PPT/TDC/EDC go **down** from the fused stock values the SMU reports, never up. There is no boost-clock override and no PBO scalar.
  3. **Intel PL1/PL2 reduce only**, through MSR 0x610. If the BIOS lock bit (bit 63) is set, hide the control. On 13th/14th-gen desktop, offer "Intel Default Settings" values as the preset, and show the microcode version, warning if it is below 0x12B.
- **Do not ship:**
  - **Intel undervolting.** It does nothing on 12th gen and newer while Undervolt Protection is on (the default). On 6th to 10th gen it is usually locked by the Plundervolt BIOS fix. The register is undocumented. **Show a BIOS guide instead.**
  - Any **raise** of limits (PBO above stock, boost override, PL above BIOS).
  - Positive CO.
  - Zen 2 CO. Zen 2 has no supported CO, and ZenStates-Core marks its Zen 2 IDs "Not sure".
  - Mobile RyzenAdj-style STAPM tuning in v1.
- **Licences:** the PawnIO driver is GPL-2.0 with an IOCTL exception, and the modules are LGPL-2.1, so both are fine to call. ZenStates-Core and UXTU are **GPL-3.0**, so we should not link them into a closed app. We should write our own small SMU and MSR layer from the public message IDs (inference, not legal advice).

## PawnIO: what the official modules allow

Source: https://github.com/namazso/PawnIO.Modules (LGPL-2.1-or-later, file headers and `COPYING`). The official signed driver only loads the official signed modules: "Access is limited to hardware supported by the official modules" (https://pawnio.eu/).

| Module | Read | Write | Notes |
|---|---|---|---|
| `RyzenSMU.p` | SMU registers in the mailbox ranges, PM table, codename, SMU version | `ioctl_send_smu_command` takes **any message ID from 0 to 0xFF**, plus 6 args. `ioctl_write_smu_register` is limited to the mailbox/SMN ranges in `check_smu_register_range` | Families 0x15 (Carrizo/Bristol only), 0x17, 0x19 and 0x1A. Codenames run from SummitRidge to GraniteRidge, StrixPoint, StrixHalo, KrackanPoint and Turin. Send-command was added in commit `05b441f` (2025-11-30) and ships in release 0.2.9 (2026-06-22) and later. Callers should hold the `\BaseNamedObjects\Access_PCI` mutex. |
| `IntelMSR.p` | Thermal, energy/RAPL, 0x150, 0x601, 0x610, turbo ratios, 0x8B (microcode `BIOS_SIGN_ID`), 0x1A4 | **Only** 0x601 `VR_CURRENT_CONFIG`, 0x610 `PKG_POWER_LIMIT`, 0x150 `OC_MAILBOX`, 0x607/0x608 VR mailbox, 0x1A4 prefetcher control | MSR write was added in commit `0f56608`/`02b58ae` (2026-03-17) and ships in 0.2.9 and later |
| `IntelMCHBAR.p` | MCHBAR dword/qword | **None** | So the MMIO copy of the power limit (MCHBAR+0x59A0) cannot be written through official PawnIO **(inference from source)** |

PawnIO's anti-cheat status is unchanged from `overclocking-and-driver-tools.md`:
- **FACEIT blocks it** (https://github.com/Rem0o/FanControl.Releases/discussions/3537, which quotes FACEIT support, **(community)**).
- Nothing public says either way for Vanguard, EAC or BattlEye (same thread, which has no answers).
- Riot says only that Vanguard blocks "a driver that doesn't meet its security requirements", and it lists no tuning drivers (https://support.riotgames.com/en-us/riot/performance/van-incompatible-oem-driver).
- The HWiNFO author described Vanguard as blocking low-level hardware-access drivers such as CPU-Z's in 2020, then relaxing that (https://hwinfo.com/forum/threads/incompatibility-with-riot-vanguard-anti-cheat.6356/latest, **(community)**).
- The tuning values themselves are not visible to anti-cheat as "cheating". The risk is only the loaded driver **(inference)**.

## AMD

### Curve Optimizer (per-core and all-core voltage-curve offset)

- **What it is:** CO "shift[s] the Voltage/Frequency curve to include lower voltages (negative values)". Ryzen Master User Guide: https://docs.amd.com/r/en-US/68886-ryzen-master-user-guide/CPU
- **Officially supported generations:**
  - CO arrived with Ryzen Master 2.9.0.2093 for "desktops with Ryzen 5000 processors and Threadripper PRO 5000WX". https://www.amd.com/content/dam/amd/en/documents/products/software-tools/faq-curve-optimizer.pdf
  - AM5 boards accept -50 to +30 or more, and Ryzen Master displays at most +30/-50. https://fr.msi.com/faq/10122
- **X3D:**
  - **5800X3D (Zen 3):** AMD locked overclocking and PBO. CO is not in Ryzen Master for it. Some boards' BIOSes and PBO2 Tuner unlock negative CO at runtime **(community)**: https://www.computerbase.de/forum/threads/amd-ryzen-master-2-9-0-2093-support-fuer-die-3d-v-cache-cpu-und-auto-curve-oc.2081392/latest. The PBO2 Tuner guide suggests starting at -10, working down in -5 steps, to a floor of -30 **(community)**: https://github.com/PrimeO7/How-to-undervolt-AMD-RYZEN-5800X3D-Guide-with-PBO2-Tuner
  - **7000X3D (Zen 4):** AMD "will not allow direct frequency overclocking due to a 1.4V limit" but allows "both ... PBO and use of the Curve Optimizer" (AMD statement as reported by https://www.thefpsreview.com/2023/01/05/no-manual-overclocking-for-amd-ryzen-7000x3d-series-cpus).
  - **9000X3D (Zen 5):** "the first X3D processor to be fully unlocked" (AMD press release, quoted at https://www.thefpsreview.com/2024/11/01/amd-ryzen-7-9800x3d-gaming-processor-officially-announced-including-complete-specifications-and-pricing/). Reviewers suggest CO -10 to -15 per CCD as a start. Up to -50 is allowed, "but not every CCD supports that" **(community)**: https://hwbusters.com/cpu/amd-ryzen-7-9800x3d-cpu-review-performance-thermals-power-analysis/3/
- **Zen 2:** there is no supported CO. ZenStates-Core labels its Zen 2 `SetDldoPsmMargin` IDs "// Not sure" (`Hardware/Smu/Settings/Zen2Settings.cs`), and AMD's FAQ lists Ryzen 5000 only.
- **SMU message IDs used by community tools** (ZenStates-Core, GPL-3.0, https://github.com/irusanov/ZenStates-Core):
  - Zen 3 MP1: `SetDldoPsmMargin` 0x35, `SetAllDldoPsmMargin` 0x36 (`Zen3Settings.cs`).
  - Zen 4 RSMU: 0x6 / 0x7, Get 0xD5, Curve Shaper 0xA6 (`Zen4Settings.cs`). Zen 5 uses the same family of IDs.
  - ZenStates-Core already talks to PawnIO's `RyzenSMU` module (`PawnIo/RyzenSmu.cs`, `Cpu.cs`).
- **Persistence:** runtime only. "You will have to do this again every time you restart or wake from sleep" **(community)**: PBO2 Tuner guide, above. Only BIOS CO persists. Ryzen Master warns that "a system restart maybe required" and that "Your system may restart during this process" (AMD FAQ and User Guide above).
- **Failure mode:** too much negative CO gives idle or light-load crashes, WHEA errors and reboots. AMD warns that derived values "applied as is may result in system stability issues" (AMD CO FAQ above).

### PBO limits (PPT, TDC, EDC, scalar, boost override)

- **Definitions:** PPT is socket power, TDC is sustained current, and EDC is peak current. In "AMD Spec" mode, limits can only be set "up to but not exceeding the AMD spec limits"; PBO mode goes "up to the limits of the board". https://docs.amd.com/r/en-US/68886-ryzen-master-user-guide/CPU
- **Community SMU IDs:** for example, Zen 4 RSMU uses `SetFastLimit` (PPT) 0x56, `SetTDCVDDLimit` 0x57, `SetEDCVDDLimit` 0x58, `SetPBOScalar` 0x5B and `SetBoostLimitFrequencyAllCores` 0x70. The fused stock limits can be read with 0xDA to 0xDC, and the Zen 4 read "can be locked on some Zen 4 motherboards" (`Zen4Settings.cs`).
- **Mobile:** RyzenAdj (https://github.com/FlyGoat/RyzenAdj) sets STAPM, fast and slow PPT, TDC/EDC, Tctl, and `--set-coall` / `--set-coper` (`main.c`). It is LGPL-3.0. Its Windows build still bundles **WinRing0** and InpOut (`win32/`). Its README says "settings could get overwritten by power management features of your device, and you need to regularly set your values again".
- **Persistence:** runtime only, as with CO.

### Tools

| Tool | Does | Driver | Licence |
|---|---|---|---|
| Ryzen Master (AMD) | PBO, CO, Curve Shaper, auto-derive | AMD's own | Proprietary. The Monitoring SDK is read-only: https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html |
| ZenStates-Core | A C# library: CO, PBO limits, PM table | PawnIO (and others) | GPL-3.0 (repo `LICENSE`) |
| PBO2 Tuner (PJVol, overclock.net "Debug-cli") | Runtime CO and PBO limits, CLI for Task Scheduler | — | No licence stated, closed binary **(community)** |
| RyzenAdj | Mobile power limits, CO | WinRing0/InpOut on Windows | LGPL-3.0 |
| Universal x86 Tuning Utility | AMD and Intel tuning GUI | PawnIO (`Scripts/AMD Backend/AMDPawnIO.cs`, `Scripts/Intel Backend/IntelPawnIO.cs`) | GPL-3.0 |
| ryzen_smu | Linux kernel SMU driver | — (Linux only) | GPL-2.0 |

## Intel

### Undervolting through MSR 0x150 (OC mailbox)

- **Status:** undocumented by Intel. Linux keeps it off the MSR write allowlist, and the kernel maintainers treat it as "hobbyists poking and prodding at their CPUs" (https://lkml.iu.edu/hypermail/linux/kernel/2009.0/07275.html, a quote from Jason Donenfeld's message).
- **6th to 10th gen:**
  - Plundervolt (CVE-2019-11157, INTEL-SA-00289) affected 6th to 10th gen Core. The fix is a BIOS update: https://www.intel.com/content/www/us/en/security-center/advisory/intel-sa-00289.html
  - The microcode and BIOS update "allows disabling of the undervolting interface": https://plundervolt.com/
  - ThrottleStop then shows "Locked" in its FIVR window **(community)**: https://www.techpowerup.com/forums/goto/post?id=4705871
- **12th gen and newer:**
  - Undervolt Protection is **on by default**. Voltages cannot go below the BIOS/boot value. "Negative offsets that would drop voltage below the boot-time level are not applied." Only some K/KF/KS parts on Z-chipset boards can turn it off, in the BIOS, and Intel advises against it. https://www.intel.com/content/www/us/en/support/articles/000094219/processors/processor-utilities-and-programs.html
  - XTU will not start with VBS/HVCI on: https://www.intel.com/content/www/us/en/support/articles/000093813.html
- **Persistence:** runtime only.
- **Net:** on a typical Windows 11 PC (12th gen or newer, UVP on, VBS on by default), OS undervolting **does nothing**. On older parts it is usually locked.

### PL1/PL2/Tau through MSR 0x610 and MMIO

- **MSR 0x610:** `MSR_PKG_POWER_LIMIT` is documented in the Intel SDM Vol. 4. Bit 63 is a lock, which the BIOS can set until reset. https://www.intel.com/content/www/us/en/developer/articles/technical/intel-sdm.html
- **MMIO copy:** a second, memory-mapped copy exists (MCHBAR+0x59A0). The lower of the MSR and MMIO limits wins, which is why ThrottleStop has a "lock the memory mapped turbo power limits" option. https://www.techpowerup.com/download/techpowerup-throttlestop/ **(community)**
- **What this means for us:** PawnIO cannot write MCHBAR, so through PawnIO we can only **lower** limits reliably. Raising them could be capped by the MMIO copy **(inference)**.
- **Persistence:** runtime only.

### 13th/14th-gen instability (Vmin shift)

- **Root cause:** Intel says "a clock tree circuit within the IA core" degrades under elevated voltage and temperature. The fixes are:
  - microcode 0x125 (the eTVB fix),
  - 0x129 (high-voltage requests),
  - **0x12B** (idle/light-load voltage, delivered by a BIOS update),
  - plus "Intel Default Settings" for board power delivery.
  - Mobile parts are not affected.
  - Source: https://community.intel.com/t5/Blogs/Tech-Innovation/Client/Intel-Core-13th-and-14th-Gen-Desktop-Instability-Root-Cause/post/1633239
- **Default Settings values:** for example, i9-14900K "Performance" is PL1 125 W, PL2 253 W, IccMax 307 A. These figures come from press reports, not an Intel-published document **(community)**: https://www.igorslab.de/en/intel-spielt-mit-dem-namen-und-den-daten-das-intel-baseline-profile-wird-zu-intel-default-settings/
- **Safety for us:**
  - Never raise power or voltage on Raptor Lake desktop.
  - Offer "lower to Intel Default Settings PL1/PL2".
  - Read MSR 0x8B (allowed by `IntelMSR`) and warn if the microcode is below 0x12B, pointing the user to a BIOS update.

### Tools

| Tool | Does | Notes |
|---|---|---|
| Intel XTU | Undervolt, PL, ratios | Proprietary, no public API. Blocked by UVP and VBS (Intel links above) |
| ThrottleStop (unclewebb) | 0x150 offsets, PL1/PL2 MSR and MMIO, FIVR | Closed freeware. Historically used WinRing0 (TPU page above) **(community)** |
| UXTU | Intel PL through PawnIO `IntelMSR`, MCHBAR read | GPL-3.0 |

## Operations matrix

| Operation | CPUs | Persists | Route | Anti-cheat | Proposed hard limit |
|---|---|---|---|---|---|
| CO all-core / per-core, negative | Ryzen 5000/7000/9000 desktop including X3D (5800X3D unofficial) | No (reboot or sleep resets) | PawnIO `RyzenSMU` | FACEIT blocks PawnIO; others unknown | 0 to **-15** default, **-30** in Advanced, step 5. Never positive |
| Curve Shaper | Zen 5 | No | `RyzenSMU` (0xA6) | same | Not in v1 |
| PPT/TDC/EDC | Zen 2 to Zen 5 desktop | No | `RyzenSMU` | same | **Reduce only.** Floor at 50% of fused stock **(inference)**, ceiling = fused stock |
| PBO scalar / boost override | Zen 3 to Zen 5 (not 7000X3D) | No | `RyzenSMU` | same | **Not shipped** (BIOS guide) |
| Mobile STAPM/fast/slow | Ryzen mobile APUs | No, and the OEM may override | `RyzenSMU` | same | Not in v1 |
| Intel voltage offset (0x150) | 4th to 10th gen if not Plundervolt-locked; 11th gen and newer blocked by UVP | No | `IntelMSR` | same | **Not shipped**. If it is ever added: -50 mV core/cache max, readback check |
| Intel PL1/PL2 (0x610) | 2nd gen and newer, unless BIOS-locked (bit 63) | No | `IntelMSR` | same | **Reduce only**, floor 35 W **(inference)**, ceiling = current BIOS value |
| Microcode check (0x8B read) | 13th/14th desktop | — | `IntelMSR` | same | Warn below 0x12B |

## Recommendation

1. **Ship "CPU tuning (advanced)" as opt-in only.** The app installs PawnIO and loads the official signed `RyzenSMU`/`IntelMSR` modules. Show a FACEIT warning before PawnIO install.
   - Writes go through the elevated service, never the UI process.
   - Hold the `Access_PCI` mutex for SMU calls.
   - Hard-code an **allowlist of SMU message IDs per codename** (CO set/get and PPT/TDC/EDC set/get only). The module does not limit IDs, so this is our only guard.
2. **AMD:**
   - Negative-only CO: all-core by default, per-core in Advanced.
   - Clamps of -15 and -30. On CPUs where AMD did not enable CO, hide it unless the user ticks "unofficial" (5800X3D/5700X3D).
   - Power limits may only be reduced.
   - No Zen 2 CO, no scalar, no boost override, no mobile limits in v1.
3. **Intel:** PL1/PL2 reduce-only, with an "Intel Default Settings" preset and a microcode warning on 13th/14th gen. **No undervolting**: replace it with a BIOS guide that explains UVP and Plundervolt.
4. **Safeguards:**
   - **Apply-test-confirm:** apply, run a 60 s confirmation countdown, and revert automatically if it is not confirmed. A short in-app stress test must pass before a profile can be "auto-apply at logon".
   - **Delayed re-apply:** apply 60 to 120 s after logon and on resume from sleep, never at boot-start.
   - **Crash rule:** after an unclean shutdown (Kernel-Power 41 / EventLog 6008), skip re-apply, mark the profile "suspect" and step CO back by 5. After two strikes, disable the profile.
   - **Watch WHEA-Logger events.** Any WHEA event after applying CO also counts as a strike **(inference)**.
   - **Revert = reboot.** The "Restore defaults" button sends CO 0 and stock limits (read from the SMU at first run), and also tells the user that a reboot fully restores the BIOS values.
   - **Optional "gaming mode" (unverified):** settings stay applied after PawnIO stops. The service could apply them and then unload the module/driver so it is not resident while a FACEIT game runs. This needs testing.
5. **Licensing:** write our own thin SMU/MSR layer in C# over PawnIO's IOCTL interface. Read ZenStates-Core only as a reference for message IDs; do not link or copy it. Credit PawnIO and its LGPL modules in the About page.
