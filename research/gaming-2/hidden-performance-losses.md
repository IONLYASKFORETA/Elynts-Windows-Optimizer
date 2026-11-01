# Find which hidden performance losses the app can detect

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/91

Researched on 2026-10-30 from Microsoft Learn API pages (some read from Microsoft's own MicrosoftDocs source on
GitHub), NVIDIA, AMD and Intel SDK headers and docs, and measured tests from the hardware press. Nothing was run on a
real PC. **(community)** marks a secondary or community source. **(inference)** marks my own reasoning.

## Answer

- Every candidate on the list can be detected through documented Windows or GPU-vendor interfaces. None of the reads
  needs admin rights or a driver, except deeper CPU throttling reads through the optional PawnIO driver. Detection
  only reads; it is never a change.
- The surest wins are a monitor below its top refresh rate, a monitor cabled to the motherboard while a graphics card
  is fitted, a game running on the integrated GPU, and a laptop on battery or in a power-saving mode. Each is a plain
  read with few false positives.
- PCIe link: GPUs lower their link speed at idle by design ([Dell][dell-idle]), so "below max" at idle is not a fault.
  The app should compare what the slot allows with what the card supports (no load needed) and read the live link
  only while a game runs. A chipset x4 slot costs an RTX 5090 6–11%; PCIe 3.0 x16 costs 2–4% ([TechPowerUp][tpu-pcie]).
- Resizable BAR: AMD ([ADLX][adlx-sam]) and Intel ([IGCL][igcl]) report it directly. On NVIDIA it can be worked out
  from the BAR1 size **(inference)**. On NVIDIA it gains about 2–4% in supported games, and about 20% in one
  ([TechPowerUp][tpu-rebar]). Intel says Arc needs it for its best performance ([Intel][intel-rebar]). The fix is a
  BIOS guide.
- Memory: a single stick is certain from SMBIOS, which the app already reads. Two sticks in the same channel can only
  be guessed from slot names, which differ by board maker. Press tests report single channel costing up to about 20%
  **(community)**.
- Throttling: NVIDIA ([NVML][nvml-r], [NVAPI][nvapi]) and Intel ([IGCL][igcl]) report why clocks are held down. NVAPI
  even reports a missing GPU power cable. AMD's ADLX reports temperatures but no reasons. Without a driver, CPU
  throttling shows only through Windows counters and the event log.
- Three checks must run in the user's session, not in the service. They are the display reads, the mouse polling
  test and the per-user GPU choice. Services run in session 0 ([Microsoft][session0]).
- Some checks only mean something while a game runs: link speed, throttling, which GPU the game uses and background
  CPU. The app already sees games start for per-game profiles, so it can sample then and report afterwards
  **(inference)**.
- Most fixes are advice the user acts on, such as moving a cable, a card, a memory stick or a game. Two fit as new
  tweaks: "Run this game on the graphics card" (a per-game tweak) and Windows' variable refresh rate switch. Resizable
  BAR and the PCIe slot speed are BIOS guides.
- Evidence is weak for the costs of a hard drive, a nearly full SSD, Wi-Fi and background programs. No measured gaming
  numbers were read for them. Show them as advice, not as a loss with a number.
- No primary data on how common any of these mistakes is was found. Treat commonness as unknown, not as "rare".

## Checks at a glance

"Runs in" says where the read must happen. "Either" means the window or the service can do it without admin rights.

| # | Check | What it can cost | How the app reads it | False positives | Runs in | Fix |
|---|---|---|---|---|---|---|
| 1 | Monitor below its top refresh rate | A 144 Hz screen at 60 Hz shows at most 60 frames a second **(inference)** | Current rate from QueryDisplayConfig vs. the mode list | Low | Window | Advice, later a tweak |
| 2 | Monitor plugged into the motherboard | Game may run on the iGPU, or frames are copied between GPUs **(inference)** | Which adapter drives each display, from QueryDisplayConfig and DXGI | Low on desktops; skip laptop panels | Window | Advice |
| 3 | GPU on a slow or narrow PCIe link | 1–26% on an RTX 5090, by link ([TechPowerUp][tpu-pcie]) | NVML, ADLX, IGCL, or Windows PCI device properties | High at idle; low if read right | Either | Advice + BIOS guide |
| 4 | Memory in single channel or mismatched | Up to ~20% **(community)** | SMBIOS memory devices: count, slot names, sizes | Low for one stick; medium for slot names | Either | Advice |
| 5 | Resizable BAR off | 2–4% average, ~20% in one game on NVIDIA ([TechPowerUp][tpu-rebar]); more on Arc | ADLX, IGCL; NVML BAR1 size on NVIDIA | Low on AMD and Intel; medium on NVIDIA | Either | BIOS guide |
| 6 | Game installed on a hard drive | Longer loads, possible streaming stutter **(inference)** | Game folder → disk → media type | Low (unknown media type) | Either | Advice |
| 7 | Nearly full SSD | Slower writes and patches; failed updates **(inference)** | Free space per volume | Medium | Either | Clean up + advice |
| 8 | GPU throttling, or a missing power cable | Hardware slowdown halves clocks or worse ([NVML][nvml-r]) | NVML / NVAPI / IGCL reason flags; ADLX temperatures | Low (ignore power-limit alone) | Either | Advice |
| 9 | CPU throttling | Clocks capped below normal **(inference)** | Windows counters, event 37; PawnIO for more | Medium | Either; PawnIO in service | Advice |
| 10 | Battery, power-saving mode or a capped power plan | Lower CPU and GPU limits ([NVAPI][nvapi], [Microsoft][ws-power]) | Power status, effective power mode, plan values | Low | Either | Tweak exists + advice |
| 11 | Game running on the integrated GPU | Large **(inference)** | Per-process GPU engine counters; GPU choice per app | Low | Either; HKCU in window | Per-game tweak |
| 12 | Low mouse polling rate | 125 Hz adds up to 8 ms **(inference)** | Time between Raw Input reports while the mouse moves | Medium | Window | Advice |
| 13 | Variable refresh rate off | Tearing, or waits with V-Sync **(inference)** | NVAPI, ADLX, Windows switch | Medium | Window | Advice + tweak |
| 14 | Wi-Fi instead of Ethernet | Latency spikes online **(inference)** | Interface type of the default route | Medium | Either | Advice |
| 15 | Background programs eating CPU | Depends on the game **(inference)** | CPU time per process, sampled | Medium | Either | Per-game profile |
| 16 | Too little free memory (added) | Stutter from paging **(inference)** | Memory load, commit, hard faults | Medium | Either | Advice + per-game profile |

## How the checks fit the app

- **Where results show.** Each finding fits as a waiting item on the Home page, beside restarts, driver updates and
  drift, and opens the page that deals with it. The glossary has no word yet for a detected problem; a new term
  should be agreed before building **(inference)**.
- **Warn, never block.** A check only reports what it read and what the user can do. Like anti-cheat conflicts, it
  never stops anything. Each finding needs a "this is on purpose" dismiss, because several have honest exceptions
  **(inference)**.
- **Sampling during play.** Checks 3, 8, 9, 11 and 15 need the PC under game load. The service already sees games
  start for per-game profiles, so it can sample during a game and report after it closes. Nothing should pop up
  mid-game **(inference)**.
- **Window vs service.** All changes still go through the service ([ADR 0001](../../docs/adr/0001-elevated-service-makes-every-change.md)).
  Reads that need the user's desktop (displays, Raw Input, the user's registry) run in the window or in a
  user-session helper. The service already has one: `UserProcess` starts a process with the signed-in user's token
  on `winsta0\default` (`src/Elynt.Windows/UserProcess.cs`).
- **What the app already has.** SMBIOS memory devices (`src/Elynt.Core/Bios/SmbiosTable.cs`), NVML readings
  (`src/Elynt.Windows/NvmlSensors.cs`), ADLX and IGCL bindings for tuning, Windows CPU counters
  (`src/Elynt.Windows/WindowsSensors.cs`), the System event log (`src/Elynt.Windows/WindowsCpuStabilityEvents.cs`),
  game install paths (`src/Elynt.Core/Games/InstalledGame.cs`), display adapters from the driver inventory and
  background apps to close in per-game profiles. Most checks extend these instead of adding new readers.

## The checks in detail

### 1. Monitor below its top refresh rate

- **Cost.** The refresh rate caps how many frames reach the screen. A 144 Hz monitor left at 60 Hz shows at most
  60 frames a second, 16.7 ms apart instead of 6.9 ms, however fast the GPU is **(inference)**.
- **Detect.** `QueryDisplayConfig` lists each active display path. Each path's target carries the current
  `refreshRate` and the connector type in `outputTechnology` ([DISPLAYCONFIG_PATH_TARGET_INFO][dc-target]). The
  connector values include HDMI, external DisplayPort and internal panels ([output technology][dc-tech]).
  `EnumDisplaySettingsEx` lists the modes Windows offers that display; take the highest rate at the current
  resolution ([EnumDisplaySettingsEx][enumds]). Flag when the current rate is clearly below that highest rate.
- **Reliability.** High. The values are what Windows itself uses. Treat 59.94 and 60 as equal. Ignore interlaced
  modes **(inference)**.
- **False positives.** A laptop on Windows' dynamic refresh rate may show 60 Hz at the desktop and switch up in games
  **(inference)**. Some players lock a 60 Hz game on purpose. Custom modes made in a GPU control panel can appear in
  the list **(inference)**.
- **Second step.** If a cable or port limits the monitor, the higher rate never appears in the mode list at all, for
  example a 1440p 144 Hz monitor on an old HDMI port. Catching that needs the monitor's EDID, parsed against the
  current connector type. Leave it for later **(inference)**.
- **Runs in.** The window. Services run in session 0 and can't see the user's desktop ([Microsoft][session0]).
- **Fix.** Advice first, with a button that opens Windows' advanced display settings. It could become a tweak later.
  The service could apply the mode through its user-session helper and save the old mode for undo **(inference)**.
- **How common.** Unknown; no data found.

### 2. Monitor plugged into the motherboard

- **Cost.** Two outcomes, both bad **(inference)**. The game may run on the integrated GPU, because many games pick
  the adapter that owns the display. Or Windows renders on the graphics card and copies each frame to the integrated
  GPU for display. That copy is the hybrid design Microsoft documents, where the integrated GPU drives the panel and
  the discrete GPU is render-only ([Microsoft hybrid systems][hybrid]). G-SYNC and similar features are then usually
  lost too **(inference)**. No measured numbers were read.
- **Detect.** Each display path from `QueryDisplayConfig` names its adapter by LUID ([DISPLAYCONFIG_PATH_TARGET_INFO][dc-target]).
  Match the LUID to the DXGI adapter list. An adapter is integrated when Direct3D 12 reports it as UMA
  ([D3D12_FEATURE_DATA_ARCHITECTURE][d3d12-arch]); ADLX and IGCL also say integrated or discrete. Flag an external
  display on an integrated GPU while a discrete GPU is present and working.
- **False positives.** Laptops: the internal panel on the integrated GPU is the normal hybrid design
  ([Microsoft][hybrid]). Skip internal connectors ([output technology][dc-tech]). A second monitor on the motherboard
  for chat or video is a choice, so only flag the main display hard **(inference)**.
- **Runs in.** The window, for the same session reason as check 1.
- **Fix.** Advice: move the cable to the graphics card.
- **How common.** Unknown. Forum threads show people asking about it **(community)** ([Tom's Hardware forum][toms-forum]).

### 3. GPU on a slower or narrower PCIe link

- **Cost.** TechPowerUp measured the RTX 5090 on every link ([TechPowerUp][tpu-pcie]). Against PCIe 5.0 x16:
  - PCIe 4.0 x16 or 5.0 x8: about 1% slower.
  - PCIe 3.0 x16 or 4.0 x8: about 4% at 1080p and 1440p, 2% at 4K.
  - PCIe 4.0 x4, a typical chipset slot: 6–11% slower.
  - PCIe 3.0 x4 and the same bandwidth: 16–26% slower.
  A Gen 5 SSD that splits the main slot to x8 costs about 1% on that card. Cards with only 8 lanes on an older board
  probably lose more, but that was not measured here **(inference)**.
- **The idle trap.** GPUs lower their link speed when idle to save power, and raise it again under load. Dell calls
  this "by design" ([Dell][dell-idle]). So a link read at the desktop will often look slow. Never flag the live speed
  unless the GPU is busy.
- **Detect without load.** Compare two static values: the most the card supports and the most this card and slot can
  reach together. NVML gives both: `nvmlDeviceGetGpuMaxPcieLinkGeneration` (the card) and
  `nvmlDeviceGetMaxPcieLinkGeneration` / `nvmlDeviceGetMaxPcieLinkWidth` ("possible with this device and system")
  ([NVML][nvml-q]). A gap means the slot, a riser or a BIOS setting holds the card back. This works at idle.
- **Detect under load.** During a game, read the current generation and width: `nvmlDeviceGetCurrPcieLinkGeneration`
  and `nvmlDeviceGetCurrPcieLinkWidth` on NVIDIA ([NVML][nvml-q]). AMD's ADLX gives the bus type and lane width
  ([ADLX ISystem1.h][adlx-sys1]). Intel's IGCL gives the maximum and current generation and width
  ([IGCL][igcl]). For any vendor, Windows stores `DEVPKEY_PciDevice_CurrentLinkSpeed`, `CurrentLinkWidth`,
  `MaxLinkSpeed` and `MaxLinkWidth` on each PCIe device ([DEVPKEY][devpkey], [width key][devpkey-w]), readable with
  `CM_Get_DevNode_Property` ([CM_Get_DevNode_PropertyW][cm-prop]). Microsoft defines max width as "implemented by an
  express link" and max speed as the link's maximum ([NDIS PCI properties][ndis-pci]).
- **Reliability.** High from vendor APIs. NVML marks these calls "for fully supported devices"; whether every
  GeForce card answers needs a test on real hardware **(inference)**. The Windows properties have a known trap. Intel
  Arc cards contain an internal PCIe switch, so Device Manager shows Gen1 x1 on the GPU itself. Intel says only the
  bottom-most bridge reports the true link ([Intel][intel-arc-link]). Read the link of the card's upstream port
  instead, and prefer vendor APIs where they exist **(inference)**. The speed value is a link code. Windows' headers
  name only 2.5 and 5 GT/s; higher generations probably use the PCIe register codes 3, 4 and 5 **(inference)**.
- **Runs in.** Either. The service already loads NVML, ADLX and IGCL.
- **Fix.** Advice: move the card to the top slot, check the riser cable, check whether an M.2 drive shares the slot's
  lanes. A BIOS guide for a slot forced to an older generation fits the existing guides **(inference)**.
- **How common.** Unknown.

### 4. Memory in single channel or mismatched

- **Cost.** Two channels give the CPU twice the memory bus width of one **(inference)**. Press tests range from
  "fine for some games, a disaster for others" ([PCWorld headline][pcw-ram]) to "up to 20%"
  ([TechSpot headline][ts-ram]) **(community)**. Only the headlines were read; the full articles were not, because
  the web budget ran out. TechSpot's dedicated test ([TechSpot][ts-ram-article]) should be read before quoting a
  number in the app.
- **Detect.** The app already parses SMBIOS type 17 memory devices: size, type and speeds per stick
  (`SmbiosTable.cs`).
  - **One stick:** count the populated devices. One stick on a desktop board means single channel. High reliability.
  - **Two sticks, same channel:** read each stick's Device Locator and Bank Locator. Windows exposes the same
    strings as `DeviceLocator` and `BankLabel` ([Win32_PhysicalMemory][physmem]). If both names show the same
    channel letter, the sticks share one channel. The app's parser does not read these strings yet.
  - **Mismatched sizes:** different sizes run part of the memory in single channel on many platforms
    **(inference)**.
  - `InterleavePosition` would answer directly, but it comes from SMBIOS type 20 ([Win32_PhysicalMemory][physmem]),
    which many consumer boards leave out **(inference)**.
- **Reliability.** High for one stick and for sizes. Medium for channel names, which each board maker writes
  differently. When the names carry no channel letter, report "can't tell" **(inference)**.
- **False positives.** Laptops with soldered memory plus one slot show two devices and may be dual channel already.
  Some small laptops have one channel by design, which nobody can fix **(inference)**.
- **Runs in.** Either. The service already reads SMBIOS.
- **Fix.** Advice: fit a second matching stick, or move sticks to the slots the board manual names. Not a BIOS guide.
- **How common.** Unknown. Budget prebuilts and laptops shipping with one stick is a common complaint
  **(community)**, but no figures were found.

### 5. Resizable BAR off

- **What it is.** Without it, a graphics card lets the CPU see only a small window of its memory, commonly 256 MB.
  Windows can resize that window at boot so the CPU sees all video memory ([Microsoft][ms-rebar]).
- **Cost.** On NVIDIA RTX 30 cards it averaged about 2–4% in the games NVIDIA enables it for. Gears 5 gained about
  20%, and one game (Watch Dogs Legion at 1080p) got worse ([TechPowerUp][tpu-rebar]). NVIDIA turns it on per game,
  from its own cautious list ([TechPowerUp][tpu-rebar]). Intel says it "must be enabled for optimal performance" on
  Arc, though Arc still works without it ([Intel][intel-rebar]). No AMD figure was read.
- **Detect.**
  - **AMD:** ADLX `IADLXSmartAccessMemory::IsSupported` and `IsEnabled`. Its `SetEnabled` is deprecated; AMD says to
    change it in the BIOS ([ADLX header][adlx-sam]).
  - **Intel:** IGCL `ctlPciGetProperties` returns `resizable_bar_supported` and `resizable_bar_enabled` ([IGCL][igcl]).
  - **NVIDIA:** no direct flag was found. NVML reports the BAR1 total, the window the CPU uses to reach video
    memory ([NVML][nvml-q]). BAR1 close to the card's memory size means on; about 256 MB means off **(inference
    from [Microsoft][ms-rebar])**.
  - **Any vendor:** the size of the card's large memory range in its Windows resource list would show the same thing
    **(inference)**.
- **Reliability.** High on AMD and Intel. Medium on NVIDIA until the BAR1 rule is checked on real cards.
- **Runs in.** Either.
- **Fix.** A BIOS guide: turn on Above 4G Decoding and Re-Size BAR, with CSM off **(inference)**. Intel calls it a
  motherboard feature set in the BIOS ([Intel][intel-rebar]). It belongs next to the XMP/EXPO and PBO guides, as a
  readout plus a guide. Say plainly that NVIDIA uses it only in some games.
- **How common.** Unknown.

### 6. Game installed on a hard drive

- **Cost.** Longer loading and possible stutter where a game streams data while you play **(inference)**.
  Microsoft's DirectStorage exists to use fast NVMe drives that deliver "multiple gigabytes a second" of small reads
  ([DirectStorage][dstorage]), which a hard drive can't. No measured gaming numbers were read.
- **Detect.** The app knows each game's folder (`InstalledGame.InstallPath`). Map the folder to its volume and the
  volume to its physical disk(s). Then either:
  - read `MSFT_PhysicalDisk.MediaType`: 3 is HDD, 4 is SSD, 0 is unspecified ([MSFT_PhysicalDisk][msft-pd]); its
    `BusType` also shows USB drives; or
  - ask the disk for `DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty` ([seek penalty][seek]). A drive can be
    opened with no access rights just to query it ([CreateFile][createfile]).
- **Reliability.** High. Unknown media type (some USB boxes, RAID) should show nothing **(inference)**.
- **Runs in.** Either.
- **Fix.** Advice: move the game with its launcher's own move feature. The app doesn't move games.
- **How common.** Unknown.

### 7. Nearly full SSD

- **Cost.** Uncertain for frame rates. A full SSD has less spare room for fast writes, so installs, patches and
  shader caches write slower **(inference)**. Windows updates and game patches also need free space **(inference)**.
  No vendor or measured source was read; treat this as advice.
- **Detect.** Free and total bytes per fixed volume from `GetDiskFreeSpaceEx` ([GetDiskFreeSpaceEx][diskfree]); SSD
  or not from check 6.
- **False positives.** Medium. A data-only drive kept full on purpose is fine. Flag the Windows drive and drives that
  hold games, below roughly 10% free **(inference)**.
- **Runs in.** Either.
- **Fix.** The existing Clean up, plus advice to move or remove games.
- **How common.** Unknown.

### 8. GPU throttling, or a missing power cable

- **Cost.** NVIDIA's hardware slowdown "reduc[es] the core clocks by a factor of 2 or more" ([NVML][nvml-r]).
- **Detect.**
  - **NVIDIA:** `nvmlDeviceGetCurrentClocksEventReasons` returns flags ([NVML][nvml-q]). The ones worth a finding are
    HW Slowdown (heat, a power-supply brake or a power spike), SW Thermal Slowdown, HW Thermal Slowdown and HW Power
    Brake Slowdown ([NVML reasons][nvml-r]). NVAPI's `NvAPI_GPU_GetPerfDecreaseInfo` adds thermal protection, power
    capping, an AC-to-battery event and "power connector missing" ([NVAPI][nvapi]).
  - **Intel:** IGCL `ctlFrequencyGetState` returns throttle reasons: average and burst power caps, current limit,
    thermal limit and a power-supply alert ([IGCL][igcl]).
  - **AMD:** ADLX reports GPU and hotspot temperatures and their ranges ([ADLX metrics][adlx-perf]), but no throttle
    reason was found in its headers. Use temperature near the top of the reported range plus falling clocks
    **(inference)**.
- **False positives.** NVIDIA's "SW Power Cap" means clocks are held to the power limit. GPU boost always runs up to
  a limit, so this flag alone is normal at full load and must not be a finding **(inference)**. Ignore samples where
  the "GPU Idle" reason is set ([NVML reasons][nvml-r]). Flag only when it lasts a meaningful part of a game.
- **Runs in.** Either. The app's NVML binding already reads clocks and power.
- **Fix.** Advice: airflow, dust, fan curve, and power cables. A missing power connector is urgent advice.
- **How common.** Unknown.

### 9. CPU throttling

- **Cost.** A held-down CPU runs below its normal clocks, which hurts CPU-bound games **(inference)**.
- **Detect without a driver.**
  - The Windows counter "% of maximum frequency" shows when a frequency cap is applied ([Microsoft][ws-power]). The
    app already reads `% Processor Performance` and `Processor Frequency` (`WindowsSensors.cs`). A clock well below
    normal while load is high, during a game, is a sign **(inference)**. The "% Performance Limit" and "Performance
    Limit Flags" counters may name the limit; no Microsoft page explaining them was found **(inference)**.
  - Windows logs Kernel-Processor-Power event 37 when firmware limits the processor speed
    ([Microsoft support][ev37]; [Microsoft Q&A][ev37-qa] **(community)**). The app already reads the System log.
- **Detect with PawnIO.** CPU thermal and power-limit status lives in model-specific registers that only a kernel
  driver can read. The optional PawnIO driver could read them later **(inference)**.
- **False positives.** Medium. Laptops are built to run at their heat limit. Flag only large, lasting drops during a
  game, or event 37 **(inference)**.
- **Runs in.** Either; PawnIO reads in the service.
- **Fix.** Advice: cooling, plug in a laptop, check the power plan (check 10). CPU tuning may help on some CPUs
  **(inference)**.
- **How common.** Unknown.

### 10. Battery, power-saving mode, or a capped power plan

- **Cost.** NVIDIA's own API lists "AC to battery" as a GPU slowdown reason ([NVAPI][nvapi]). A power plan can cap
  the CPU at a share of its top speed; Microsoft's example is 75% ([Microsoft][ws-power]).
- **Detect.**
  - **On battery:** `GetSystemPowerStatus` gives `ACLineStatus`. Its `SystemStatusFlag` says whether battery saver
    is on ([SYSTEM_POWER_STATUS][pwr-status]).
  - **Power mode:** the effective power mode API reports battery saver, energy saver, better battery, balanced,
    maximum performance and game mode ([EFFECTIVE_POWER_MODE][epm]); it needs Windows 10 1809 or later
    ([registration][epm-reg]).
  - **Capped plan:** read the active plan's maximum processor state. Below 100% caps the CPU ([Microsoft][ws-power]).
- **Reliability.** High. These are Windows' own values.
- **False positives.** Low. A user on battery may know; say it once per session and only while a game runs
  **(inference)**.
- **Runs in.** Either.
- **Fix.** Advice to plug in and pick Best performance. The app already has tweaks for the High performance and
  Ultimate Performance plans. A capped maximum processor state could be an undoable power-setting tweak through the
  existing power-setting step **(inference)**.
- **How common.** Unknown.

### 11. Game running on the integrated GPU

- **Cost.** Large: an integrated GPU is far slower than a graphics card **(inference)**.
- **Detect.**
  - **Which GPU a running game uses:** Windows keeps per-process GPU engine use, which Task Manager shows per GPU
    ([DirectX blog][taskmgr-gpu]). The "GPU Engine" performance counters carry the process id and the adapter LUID
    in their instance names **(inference)**. Match the game's process to its LUID, then the LUID to an integrated
    adapter (check 2).
  - **Why:** Windows' per-app graphics choice is stored per user under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`,
    the same key the app already writes for windowed games (`GamingTweaks.cs`); the per-app value format is
    **(inference)**. Games can also ask for a GPU by preference ([EnumAdapterByGpuPreference][dxgi-pref]).
  - **Laptop dGPU switched off:** a graphics card disabled by an OEM "eco" mode shows as a disabled device. A card
    that is merely asleep is normal and must not be flagged **(inference)**.
- **Reliability.** High once the counter format is confirmed on a real PC.
- **Runs in.** Counters: either. The user's GPU choice is in that user's registry, so read it in the window or from
  the user's hive **(inference)**.
- **Fix.** A new per-game tweak: "Run this game on the graphics card". It sets the game's GPU choice to high
  performance, is an account change, and undo puts back the old value. It fits the glossary's per-game tweak.
- **How common.** Unknown.

### 12. Low mouse polling rate

- **Cost.** At 125 Hz the mouse reports every 8 ms, so movement waits up to 8 ms (about 4 ms on average) before the
  game sees it. At 1000 Hz the wait is under 1 ms **(inference)**. Motion also looks less smooth on fast monitors
  **(inference)**.
- **Detect.** No Windows interface reports a mouse's polling rate. Raw Input delivers each mouse report as a
  `WM_INPUT` message with the device's handle ([Raw Input][rawin]), but the report itself has no timestamp
  ([RAWMOUSE][rawmouse]). The app must time arrivals itself. Ask the user to move the mouse in circles for two
  seconds, stamp each report with the high-resolution clock, and take the median gap per device. `RIDEV_INPUTSINK`
  lets the window receive input while not in front ([RAWINPUTDEVICE][rawdev]).
- **Why not USB descriptors.** The endpoint's interval is the fastest rate the device asks for. Gaming mice change
  their real rate in firmware, so it would mislead **(inference)**.
- **Reliability.** Medium. At 1000 Hz and above, messages can arrive in bunches; read them in batches. A wireless
  mouse in power saving or a Bluetooth mouse gives lower numbers **(inference)**.
- **Runs in.** The window. Raw Input needs the user's desktop.
- **Fix.** Advice: set the rate in the mouse's own app or with its button. Windows can't change it.
- **How common.** Unknown.

### 13. Variable refresh rate off

- **Cost.** Not a frame-rate loss. Without VRR the game either tears or waits for the screen with V-Sync, which
  adds delay and stutter when the frame rate dips **(inference)**.
- **Detect.**
  - **NVIDIA:** `NvAPI_Disp_GetVRRInfo` says whether VRR is possible, requested and active on a display ([NVAPI][nvapi]).
  - **AMD:** ADLX `IADLXDisplayFreeSync` has `IsSupported` and `IsEnabled` per display ([ADLX][adlx-disp]).
  - **Intel:** IGCL notes that "in Windows, OS controls state of adaptive sync" ([IGCL][igcl]). Rely on the display
    capability and the Windows switch.
  - **Windows switch:** Microsoft ties Optimizations for windowed games to VRR support ([Microsoft Support][win-opt]).
    The app already has a tweak to keep that on.
- **False positives.** Medium. Many monitors have no VRR, and the monitor's own menu can turn it off; then the GPU
  reports "not possible", which is not a finding **(inference)**. Only flag "possible but off".
- **Runs in.** The window is safest, because these calls are tied to displays **(inference)**.
- **Fix.** Advice pointing to the GPU control panel (driver settings belong to the GPU driver research).
- **How common.** Unknown.

### 14. Wi-Fi instead of Ethernet

- **Cost.** Mostly online: latency spikes and packet loss. That belongs to the online-play research **(inference)**.
- **Detect.** Find the interface that carries the default route, then read its `MIB_IF_ROW2.Type`, an IANA interface
  type; Wi-Fi is 802.11 ([MIB_IF_ROW2][ifrow]). The same row gives `MediaConnectState`, so the app can also see an
  Ethernet port that is present but unplugged, and `ReceiveLinkSpeed` ([MIB_IF_ROW2][ifrow]).
- **Avoid the Wi-Fi API.** Since Windows 11 24H2, `WlanQueryInterface` for the current connection returns access
  denied unless the user allows precise location, and the call can raise a location prompt
  ([Microsoft][wifi-loc]). Interface rows don't need it.
- **False positives.** Medium. Many laptops have no Ethernet port, and a good Wi-Fi 6 or 7 setup can be fine
  **(inference)**. Phrase it as "if you play online, a cable helps", and only when an Ethernet port exists.
- **Runs in.** Either.
- **Fix.** Advice.
- **How common.** Unknown.

### 15. Background programs eating CPU

- **Cost.** Depends on the game. A CPU-bound game loses whatever another program takes; a GPU-bound game may lose
  nothing **(inference)**.
- **Detect.** Sample CPU time per process for 10–30 seconds while a game runs. Flag non-game processes that hold a
  steady share. Known heavy ones include search indexing, antivirus scans, update workers, launcher shader
  compiling, browsers and RGB tools **(inference)**.
- **False positives.** Medium. Shader compiling on a game's first start is real work that ends on its own. Anti-cheat
  and the launcher belong to the game **(inference)**.
- **Runs in.** Either. The service sees processes a standard user can't open **(inference)**.
- **Fix.** Per-game profiles already close chosen background apps. A finding can suggest adding the process to the
  game's profile, or turning off its startup item. Both are existing, undoable changes.
- **How common.** Unknown.

### 16. Too little free memory (added)

- **Cost.** When memory runs out, Windows pages to disk and games stutter **(inference)**.
- **Detect.** Memory load and commit charge against the commit limit, plus hard faults per second, sampled during a
  game **(inference)**. The app already knows installed memory from SMBIOS.
- **Runs in.** Either.
- **Fix.** Advice (more memory, fewer background apps) and the per-game profile's background apps.
- **How common.** Unknown.

## Left out on purpose

- **XMP/EXPO:** already a readout and BIOS guide in v1.
- **Memory integrity, Game Bar recording, HAGS, Game Mode:** Windows settings, covered by the Windows tweaks research.
- **GPU driver settings** (G-SYNC mode, frame caps, Battery Boost): covered by the GPU driver research.
- **Measuring frame rates:** covered by the FPS research. Several checks above would be stronger with real frame
  times **(inference)**.

## Gaps

- The web budget ran out during this research. Single-channel and hard-drive costs rest on headlines or reasoning;
  read [TechSpot][ts-ram-article] before quoting a number.
- No AMD Smart Access Memory gain figure and no AMD throttle-reason interface were found.
- The NVIDIA BAR1 rule, the GPU Engine counter format and NVML's PCIe calls on GeForce need a check on real PCs.
- No primary data on how common any of these mistakes is.
- No measured cost was found for a monitor on the motherboard, Wi-Fi or a low polling rate.

## Sources

Pages marked † were cited for well-known API names but not opened in this session.

- **nvml-q:** [NVIDIA, NVML API Reference: Device Queries (R595)](https://docs.nvidia.com/deploy/nvml-api/group__nvmlDeviceQueries.html)
- **nvml-r:** [NVIDIA, NVML API Reference: Clocks Event Reasons](https://docs.nvidia.com/deploy/nvml-api/group__nvmlClocksEventReasons.html)
- **nvapi:** [NVIDIA, NVAPI public header nvapi.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi.h)
- **adlx-sam:** [AMD ADLX, ISmartAccessMemory.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISmartAccessMemory.h)
- **adlx-disp:** [AMD ADLX, IDisplaySettings.h (IADLXDisplayFreeSync)](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IDisplaySettings.h)
- **adlx-sys1:** [AMD ADLX, ISystem1.h (PCI bus type and lane width)](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISystem1.h)
- **adlx-perf:** [AMD ADLX, IPerformanceMonitoring.h](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IPerformanceMonitoring.h)
- **igcl:** [Intel Graphics Control Library, igcl_api.h](https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h)
- **ms-rebar:** [Microsoft Learn, System and driver support for Resizable BAR](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/resizable-bar-support)
- **intel-rebar:** [Intel support 000092416, Resizable BAR for Intel Arc](https://www.intel.com/content/www/us/en/support/articles/000092416/graphics.html)
- **intel-arc-link:** [Intel support 000094587, wrong PCIe link shown for Arc](https://www.intel.com/content/www/us/en/support/articles/000094587/graphics.html)
- **dell-idle:** [Dell KB 000235789, nvidia-smi current vs max PCIe generation](https://www.dell.com/support/kbdoc/en-uk/000235789/nvidia-smi-output)
- **tpu-pcie:** [TechPowerUp, NVIDIA GeForce RTX 5090 PCI-Express Scaling](https://techpowerup.com/review/nvidia-geforce-rtx-5090-pci-express-scaling/33.html)
- **tpu-rebar:** [TechPowerUp, NVIDIA PCI-Express Resizable BAR Performance Test](https://techpowerup.com/review/nvidia-pci-express-resizable-bar-performance-test/27.html)
- **ndis-pci:** [Microsoft Learn, NDIS_PCI_DEVICE_CUSTOM_PROPERTIES](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddndis/ns-ntddndis-_ndis_pci_device_custom_properties)
- **devpkey:** [Microsoft Windows metadata, DEVPKEY_PciDevice_CurrentLinkSpeed](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/NetworkManagement/WiFi/constant.DEVPKEY_PciDevice_CurrentLinkSpeed.html)
- **devpkey-w:** [Microsoft Windows metadata, DEVPKEY_PciDevice_MaxLinkWidth](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/NetworkManagement/WiFi/constant.DEVPKEY_PciDevice_MaxLinkWidth.html)
- **cm-prop:** [Microsoft Learn, CM_Get_DevNode_PropertyW](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_devnode_propertyw)
- **dc-target:** [Microsoft Learn, DISPLAYCONFIG_PATH_TARGET_INFO](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_path_target_info)
- **dc-tech:** [Microsoft Learn, DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_video_output_technology)
- **enumds:** † [Microsoft Learn, EnumDisplaySettingsExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsexw)
- **hybrid:** [Microsoft Learn, Using cross-adapter resources in a hybrid system](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/using-cross-adapter-resources-in-a-hybrid-system)
- **d3d12-arch:** [Microsoft Learn, D3D12_FEATURE_DATA_ARCHITECTURE](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ns-d3d12-d3d12_feature_data_architecture)
- **dxgi-pref:** [Microsoft Learn, IDXGIFactory6::EnumAdapterByGpuPreference](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_6/nf-dxgi1_6-idxgifactory6-enumadapterbygpupreference)
- **session0:** [Microsoft Learn, Interactive services (session 0)](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services)
- **pwr-status:** [Microsoft Learn, SYSTEM_POWER_STATUS](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status)
- **epm:** [Microsoft Learn, EFFECTIVE_POWER_MODE](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/ne-powersetting-effective_power_mode)
- **epm-reg:** [Microsoft Learn, PowerRegisterForEffectivePowerModeNotifications](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powerregisterforeffectivepowermodenotifications)
- **ws-power:** [Microsoft Learn, Power and performance tuning (Windows Server)](https://learn.microsoft.com/en-us/windows-server/administration/performance-tuning/hardware/power/power-performance-tuning)
- **ev37:** † [Microsoft Support 4043839, Kernel processor power event ID 37](https://support.microsoft.com/en-us/help/4043839)
- **ev37-qa:** † [Microsoft Q&A, WHEA event 17 with Kernel-Processor-Power event 37](https://learn.microsoft.com/en-us/answers/questions/1403855/getting-multipple-whea-logger-event-id-17-task-non) **(community)**
- **physmem:** [Microsoft Learn, Win32_PhysicalMemory](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory)
- **msft-pd:** [Microsoft Learn, MSFT_PhysicalDisk](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk)
- **seek:** [Microsoft Learn, DEVICE_SEEK_PENALTY_DESCRIPTOR](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-device_seek_penalty_descriptor)
- **createfile:** † [Microsoft Learn, CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
- **diskfree:** † [Microsoft Learn, GetDiskFreeSpaceExW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw)
- **dstorage:** [Microsoft, DirectStorage README](https://github.com/microsoft/DirectStorage/blob/main/README.md)
- **rawin:** [Microsoft Learn, About Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)
- **rawmouse:** [Microsoft Learn, RAWMOUSE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse)
- **rawdev:** [Microsoft Learn, RAWINPUTDEVICE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice)
- **ifrow:** [Microsoft Learn, MIB_IF_ROW2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2)
- **wifi-loc:** [Microsoft Learn, Changes to API behavior for Wi-Fi access and location](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes)
- **taskmgr-gpu:** † [Microsoft DirectX blog, GPUs in the Task Manager](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)
- **win-opt:** † [Microsoft Support, Optimizations for windowed games in Windows 11](https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952) (already cited by the app's tweak)
- **ts-ram:** [TechSpot news, single RAM stick setup can cost up to 20% (headline only)](https://techspot.com/news/112959-steam-machine-benchmarks-show-valve-single-ram-stick.html) **(community)**
- **pcw-ram:** [PCWorld, single-stick RAM tested: fine for some games, a disaster for others (headline only)](https://pcworld.com/article/3106376/single-stick-ram-tested-fine-for-some-games-a-disaster-for-others.html) **(community)**
- **ts-ram-article:** † [TechSpot, Single stick vs dual-channel RAM](https://www.techspot.com/article/3066-single-stick-vs-dual-channel-ram/) **(community)**
- **toms-forum:** [Tom's Hardware forum, one monitor on the GPU and one on the motherboard (title only)](https://forums.tomshardware.com/threads/plugging-one-monitor-in-the-gpu-displayport-and-another-monitor-in-the-motherboard-hdmi-will-it-affect-fps.3820666) **(community)**

[nvml-q]: https://docs.nvidia.com/deploy/nvml-api/group__nvmlDeviceQueries.html
[nvml-r]: https://docs.nvidia.com/deploy/nvml-api/group__nvmlClocksEventReasons.html
[nvapi]: https://github.com/NVIDIA/nvapi/blob/main/nvapi.h
[adlx-sam]: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISmartAccessMemory.h
[adlx-disp]: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IDisplaySettings.h
[adlx-sys1]: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/ISystem1.h
[adlx-perf]: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/main/SDK/Include/IPerformanceMonitoring.h
[igcl]: https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h
[ms-rebar]: https://learn.microsoft.com/en-us/windows-hardware/drivers/display/resizable-bar-support
[intel-rebar]: https://www.intel.com/content/www/us/en/support/articles/000092416/graphics.html
[intel-arc-link]: https://www.intel.com/content/www/us/en/support/articles/000094587/graphics.html
[dell-idle]: https://www.dell.com/support/kbdoc/en-uk/000235789/nvidia-smi-output
[tpu-pcie]: https://techpowerup.com/review/nvidia-geforce-rtx-5090-pci-express-scaling/33.html
[tpu-rebar]: https://techpowerup.com/review/nvidia-pci-express-resizable-bar-performance-test/27.html
[ndis-pci]: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddndis/ns-ntddndis-_ndis_pci_device_custom_properties
[devpkey]: https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/NetworkManagement/WiFi/constant.DEVPKEY_PciDevice_CurrentLinkSpeed.html
[devpkey-w]: https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/NetworkManagement/WiFi/constant.DEVPKEY_PciDevice_MaxLinkWidth.html
[cm-prop]: https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_devnode_propertyw
[dc-target]: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_path_target_info
[dc-tech]: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_video_output_technology
[enumds]: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsexw
[hybrid]: https://learn.microsoft.com/en-us/windows-hardware/drivers/display/using-cross-adapter-resources-in-a-hybrid-system
[d3d12-arch]: https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ns-d3d12-d3d12_feature_data_architecture
[dxgi-pref]: https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_6/nf-dxgi1_6-idxgifactory6-enumadapterbygpupreference
[session0]: https://learn.microsoft.com/en-us/windows/win32/services/interactive-services
[pwr-status]: https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status
[epm]: https://learn.microsoft.com/en-us/windows/win32/api/powersetting/ne-powersetting-effective_power_mode
[epm-reg]: https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powerregisterforeffectivepowermodenotifications
[ws-power]: https://learn.microsoft.com/en-us/windows-server/administration/performance-tuning/hardware/power/power-performance-tuning
[ev37]: https://support.microsoft.com/en-us/help/4043839
[ev37-qa]: https://learn.microsoft.com/en-us/answers/questions/1403855/getting-multipple-whea-logger-event-id-17-task-non
[physmem]: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory
[msft-pd]: https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk
[seek]: https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-device_seek_penalty_descriptor
[createfile]: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew
[diskfree]: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw
[dstorage]: https://github.com/microsoft/DirectStorage/blob/main/README.md
[rawin]: https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input
[rawmouse]: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse
[rawdev]: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice
[ifrow]: https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2
[wifi-loc]: https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes
[taskmgr-gpu]: https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/
[win-opt]: https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952
[ts-ram]: https://techspot.com/news/112959-steam-machine-benchmarks-show-valve-single-ram-stick.html
[pcw-ram]: https://pcworld.com/article/3106376/single-stick-ram-tested-fine-for-some-games-a-disaster-for-others.html
[ts-ram-article]: https://www.techspot.com/article/3066-single-stick-vs-dual-channel-ram/
[toms-forum]: https://forums.tomshardware.com/threads/plugging-one-monitor-in-the-gpu-displayport-and-another-monitor-in-the-motherboard-hdmi-will-it-affect-fps.3820666
