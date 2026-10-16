# NVIDIA clock control routes

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/11

Researched 2026-10-16. This file follows on from `research/overclocking-and-driver-tools.md`, which found that the public NVAPI SDK has no clock, voltage or power setters. Sources are listed per claim. Labels:

- **(community)**: only community tools, forums or press back the claim.
- **(inference)**: my own reading, with no source saying it.
- **(unverified)**: plausible, but not confirmed for GeForce on Windows. It needs a test on real hardware.

## Answer

- **Recommended route: NVML (`nvml.dll`), loaded from the installed driver and called through P/Invoke from C#, from an elevated helper.** It is NVIDIA's own documented and supported write API, and it covers most of what the app wants:
  - **Power limit:** `nvmlDeviceSetPowerManagementLimit(_v2)`, clamped to `nvmlDeviceGetPowerManagementLimitConstraints`.
  - **Core clock lock / max cap:** `nvmlDeviceSetGpuLockedClocks` (Volta and newer). This is also the documented way to do a "poor man's undervolt", because capping the max clock keeps the GPU on a lower point of the VF curve.
  - **Memory clock lock:** `nvmlDeviceSetMemoryLockedClocks` (Ampere and newer).
  - **Core and memory clock offsets:** `nvmlDeviceSetClockOffsets` (Maxwell and newer), checked against the range that `nvmlDeviceGetClockOffsets` reports. This is the first *documented* offset setter NVIDIA has published. The older `nvmlDeviceSetGpcClkVfOffset` / `SetMemClkVfOffset` are marked `DEPRECATED(13.0)` in favour of it.
- **Caveats:**
  - NVML gives GeForce only "limited support". Every setter can return `NVML_ERROR_NOT_SUPPORTED`, so the app must probe and hide each control the card refuses.
  - Clock offsets on GeForce **Windows** are **(unverified)**. They are confirmed working on GeForce **Linux** in LACT **(community)**.
  - Nothing persists across a reboot or driver reload. The app must re-apply its settings at logon from an elevated scheduled task.
- **Use `nvidia-smi` only as a fallback.** It wraps the same NVML calls (`-lgc`, `-lmc`, `-pl`, `-rgc`, `-rmc`). It has **no offset flag**, apart from a Rubin-only `--set-vf-derate`. NVIDIA staff call it "not a general overclocking utility with limited support on GeForce hardware".
- **Do not build on undocumented NVAPI for v1.** That is the route Afterburner and Inspector use, through `nvapi_QueryInterface` IDs. It is the only way to edit the full **VF curve** and the voltage boost. However, it relies on hand-written private structs that the driver can change, and NVIDIA neither documents nor supports it. If we ever add VF-curve editing, put it behind an "advanced / experimental" flag that probes per driver version.
- **NVIDIA App Automatic Tuning has no API.** Link to it as the "one-click safe overclock" option.

## Route comparison

| Route | Can change | GeForce on Win 11 | Admin | Persists reboot | Safety limits | Driver-update risk |
|---|---|---|---|---|---|---|
| `nvidia-smi` CLI | Lock core clock range, lock memory clock range, power limit, reset | Works on many GeForce cards **(community)**; NVIDIA says "limited support" | Yes | No | Driver clamps to design parameters and vBIOS power range | Low (documented CLI) |
| NVML `nvml.dll` | Same as nvidia-smi **plus** per-P-state core/memory offsets, fan speed | Limited support; offsets on Windows **(unverified)** | Yes | No | Range getters for power and offsets; errors on out-of-range values | Low to medium (documented; some functions deprecated with notice) |
| Undocumented NVAPI | P-state offsets, full VF curve, voltage boost %, power/thermal policies, fans | Yes; it is what Afterburner uses **(community)** | Yes for writes **(community)** | No; tools re-apply at startup | Only what the driver enforces internally; no public range contract | Medium to high: private structs and versions can change |
| NVIDIA App Auto Tuning | Applies NVIDIA's own scanned safe OC | Yes (RTX) | n/a | Yes, managed by NVIDIA App | NVIDIA's own scanner | None for us, but no API |
| Drive Afterburner / Inspector by CLI | Whatever the profile holds | Yes | Yes | Through that tool | That tool's | Depends on a third-party install |

## 1. nvidia-smi (ships with the driver)

Source: https://docs.nvidia.com/deploy/nvidia-smi/index.html

- **Scope statement:** nvidia-smi manages "Tesla, Quadro, GRID and GeForce devices" from Fermi onward. "GeForce Titan series devices are supported for most functions with very limited information provided for the remainder of the Geforce brand."
- **`-lgc / --lock-gpu-clocks=min,max`:** "Supported on Volta+. Requires root."
  - `--mode=0` is the default and the most accurate.
  - `--mode=1` uses closed-loop controllers for better performance per watt.
  - Reset it with **`-rgc / --reset-gpu-clocks`**, which is also "Volta+. Requires root".
- **`-lmc / --lock-memory-clocks=min,max`:** "Requires root". The docs say it does not work on Hopper. Reset it with **`-rmc`**. The matching NVML function says "Ampere or newer".
- **`-pl / --power-limit=W`:** "Only on supported devices from Kepler family. Requires root."
- **`-ac` (application clocks):** deprecated and slated for removal. Do not use it.
- **No clock-offset flag.** The only offset option is `-svfd / --set-vf-derate`, a negative VF-curve offset, and it is Rubin and newer only (datacenter).
- **Admin:** on Windows "root" means an elevated console. NVIDIA's game-dev blog says to "Run commands on an administrator console on Windows". Source: https://developer.nvidia.com/blog/advanced-api-performance-setstablepowerstate/
- **Persistence:**
  - Settings revert on reboot or driver reload (see the NVML notes below).
  - `-pm` persistence mode is "Linux only".
  - On Windows a driver reset or TDR also reverts them **(inference)**.
- **GeForce in practice:**
  - NVIDIA recommends `--lock-gpu-clocks` and `--reset-gpu-clocks` to game developers for stable profiling, which implies GeForce support. Source: the blog above.
  - On the same blog's forum thread, NVIDIA staff say the tool "is configured to work within design parameters as a safety precaution" and that "nvidia-smi is not a general overclocking utility with limited support on GeForce hardware". This means you cannot push `-lgc` above stock boost. Source: https://forums.developer.nvidia.com/t/advanced-api-performance-setstablepowerstate/219080
  - Community undervolt guides use `nvidia-smi -lgc 0,<MHz>` on GeForce RTX under Windows **(community)**.
  - `-pl` is accepted on many desktop GeForce cards but often refused on laptops **(community, unverified)**.
- **Safety:**
  - Clock locks stay inside the design range.
  - The power limit must sit inside the vBIOS min/max, which `nvidia-smi -q -d POWER` shows.
- **Location:** `C:\Windows\System32\nvidia-smi.exe` on DCH drivers.
- **Integration:** run it as a child process from the elevated helper and parse its output with `--query-gpu=... --format=csv`.

## 2. NVML (`nvml.dll`)

Sources:

- API reference: https://docs.nvidia.com/deploy/nvml-api/group__nvmlDeviceCommands.html and https://docs.nvidia.com/deploy/nvml-api/group__nvmlDeviceQueries.html
- Header: NVML 13.4.92 `nvml.h`, mirrored at https://github.com/NVIDIA/go-nvml/blob/main/gen/nvml/nvml.h

**Product support (from the header):**

- "Full Support": Tesla, Quadro, vGPU, "Selected GeForce Titan products".
- "Limited Support": "All Geforce products, starting with the Fermi architecture".
- Every setter is documented "for ... fully supported devices", so on GeForce every call must be treated as best-effort and may return `NVML_ERROR_NOT_SUPPORTED`.
- The current reference page says: "NVML also has limited support for newer non-data-center GPUs (for example, NVIDIA® GeForce® or NVIDIA® RTX® GPUs)". Source: https://docs.nvidia.com/deploy/nvml-api/nvml-api-reference.html

| Function | Doc text (abridged) | Notes for GeForce on Windows |
|---|---|---|
| `nvmlDeviceSetGpuLockedClocks(min,max)` | Volta+; "Requires root/admin"; "After system reboot or driver reload GPU clocks go back to their default value." Accepts MHz or `NVML_CLOCK_LIMIT_ID_TDP` / `_UNLIMITED`. | Same as `-lgc`. Works on GeForce **(community)**. Undo with `nvmlDeviceResetGpuLockedClocks`. |
| `nvmlDeviceSetMemoryLockedClocks(min,max)` | Ampere+; root/admin; reverts on reboot. | Undo with `nvmlDeviceResetMemoryLockedClocks`. |
| `nvmlDeviceSetPowerManagementLimit` / `_v2` | Kepler+; root/admin; "Limit is not persistent across reboots or driver unloads". `_v2` "replaces" v1. | Range from `nvmlDeviceGetPowerManagementLimitConstraints`; default from `GetPowerManagementDefaultLimit`. |
| `nvmlDeviceSetClockOffsets(nvmlClockOffset_t*)` | "Control current clock offset of some clock domain for a given PState. For Maxwell or newer fully supported devices. Requires privileged user." Returns `INVALID_ARGUMENT` if the offset "is out of allowed range". | **This is the officially exposed offset setter.** Read the range with `nvmlDeviceGetClockOffsets`, which gives min, max and current per clock type and P-state. GeForce Windows support is **(unverified)**. |
| `nvmlDeviceSetGpcClkVfOffset(int)` / `nvmlDeviceSetMemClkVfOffset(int)` | `DEPRECATED(13.0)`: "Use nvmlDeviceSetClockOffsets instead." | Use them only as a fallback for older drivers. |
| `nvmlDeviceSetFanSpeed_v2` | Maxwell+ discrete GPUs with fans; switches the fan policy to manual ("you can burn your GPU"). | Undo with `nvmlDeviceSetDefaultFanSpeed_v2`. Out of scope, but available. |
| `nvmlDeviceSetPersistenceMode` | "For Linux only." | Not on Windows. |

**Evidence that offsets work on GeForce:**

- LACT, a popular Linux GPU tool, applies GeForce core and memory offsets with NVML `set_clock_offset` per P-state, and locked clocks with `set_gpu_locked_clocks` / `set_mem_locked_clocks`. It also notes that "on some driver versions, the applied offset values are not reported". **(community)** Source: https://github.com/ilya-zlobintsev/LACT/blob/master/lact-daemon/src/server/gpu_controller/nvidia.rs
- A forum report (Linux, driver 565/570, GTX 1650 Super and RTX 4060 Laptop) found:
  - Offsets apply to all P-states, not only the one requested.
  - Only P0 can be overclocked.
  - No NVIDIA staff replied.
  - **(community)** Source: https://forums.developer.nvidia.com/t/nvmldevicegetminmaxclockofpstate-nvmldevicesetclockoffsets-issues/318332
- No Windows report found. This needs a test on the owner's card **(unverified)**.

**Other NVML notes:**

- **No VF-curve editing and no voltage control in NVML.** The closest it gets is a max-clock lock combined with a positive offset, which approximates an undervolt **(inference)**.
- **Licence:** the `nvml.h` header grants a "nonexclusive, royalty-free license to use this code in individual and commercial software". Code using it must keep NVIDIA's disclaimer and the U.S. Government End Users notice in the docs and code comments.
- **Do not ship `nvml.dll`.** It comes with the driver, and the GeForce driver licence §2.7 forbids redistributing it. Load it at runtime:
  - DCH drivers: `\Windows\System32\nvml.dll`.
  - Older standard drivers: `%ProgramW6432%\NVIDIA Corporation\NVSMI\nvml.dll`.
  - The reference notes it is "not ... added to the path on Windows".
- **C#:** P/Invoke `nvmlInit_v2`, `nvmlDeviceGetHandleByIndex_v2`, the getters and setters, and `nvmlShutdown`. Check the return codes; there is no maintained .NET binding that we know of. Struct versioning such as `nvmlClockOffset_v1` must match the header (`NVML_STRUCT_VERSION` macro).
- **Persistence:** none. Re-apply at logon from an elevated scheduled task, and keep "reset" as the undo action. That fits `research/undo-mechanism.md`.

## 3. Undocumented NVAPI (Afterburner, Inspector, open-source wrappers)

**How it works:**

- `nvapi64.dll` exports only `nvapi_QueryInterface(uint id)`. Each function, public or private, is fetched by a 32-bit ID.
- The public SDK lists the public IDs. The private ones (setters for P-states, VF curve, voltage, power policies and fans) were found by the community by reverse-engineering the driver and NVIDIA's partner tools **(community)**.

**IDs as used by open-source projects (community):**

| Private function | ID | Seen in |
|---|---|---|
| `NvAPI_GPU_SetPstates20` (per-P-state base core/memory offset; what Inspector and Afterburner's "core clock +X" map to) | `0x0F4DAE6B` | NvAPIWrapper `FunctionId.cs` |
| `NvAPI_GPU_GetVFPCurve` / `ClockClientClkVfPointsGetStatus` | `0x21537AD4` | NvAPIWrapper, LACT |
| `NvAPI_GPU_SetClockBoostTable` / `ClockClientClkVfPointsSetControl` (per-point VF-curve offsets) | `0x0733E009` | NvAPIWrapper, LACT |
| `NvAPI_GPU_GetClockBoostRanges` | `0x64B43A6A` | NvAPIWrapper |
| `NvAPI_GPU_SetClockBoostLock` (lock a VF point) | `0x39442CFB` | NvAPIWrapper |
| `ClientVoltRailsSetControl` (voltage boost %) | `0xB9306D9B` | LACT |
| `NvAPI_GPU_ClientPowerPoliciesSetStatus` (power target %) | `0xAD95F5ED` | NvAPIWrapper |
| `NvAPI_GPU_ClientFanCoolersSetControl` | `0xA58971A5` | NvAPIWrapper |

Sources:

- https://github.com/falahati/NvAPIWrapper/blob/master/NvAPIWrapper/Native/Helpers/FunctionId.cs
- https://github.com/ilya-zlobintsev/LACT/blob/master/lact-daemon/src/server/gpu_controller/nvidia/nvapi.rs (Linux `libnvidia-api.so.1`, under the comment "Undocumented calls")

**Projects:**

- **NvAPIWrapper (C#, LGPL-3.0).** It exposes `GPUApi.SetPerformanceStates20`, `SetClockBoostTable`, `SetClockBoostLock`, `SetCoreVoltageBoostPercent`, `SetThermalPoliciesStatus` and `SetClientFanCoolersControl`. The last NuGet release is 0.8.1.101, from 2020-12-09, so it is **unmaintained** and predates Ada and Blackwell. LGPL is fine to link dynamically from a private app. Sources:
  - https://github.com/falahati/NvAPIWrapper
  - https://www.nuget.org/packages/NvAPIWrapper.Net
- **nvapi-rs (Rust, MIT).** It wraps the same private calls on Windows. The crate is at 0.2.0 and old. Source: https://github.com/arcnmx/nvapi-rs
- **LACT (Linux, MIT).** It is current in 2025–26 and uses NVML for offsets plus private NVAPI for the VF curve and voltage. This is the best live reference for struct layouts on new GPUs **(community)**. Source: https://github.com/ilya-zlobintsev/LACT
- **MSI Afterburner.** Closed source; it uses these private NVAPI calls for NVIDIA tuning **(community)**.
  - NVIDIA gave partners a "Scanner API" (OC Scanner) in 2018. That confirms partner tools receive non-public NVAPI. Source: https://www.techspot.com/article/1704-geforce-rtx-2080-overclocking/
  - Afterburner can apply saved profiles with `MSIAfterburner.exe -Profile1..5`, which we could drive from our app **(community)**. Source: https://github.com/ethanperrine/MSIAfterburnerScript
- **NVIDIA Profile Inspector / NVIDIA Inspector.** Closed source. The command line has `-setBaseClockOffset`, `-setMemoryClockOffset` and `-setPowerTarget`, built on SetPstates20 **(community)**.

**Stability across driver versions:**

- The IDs have stayed the same for years. For example, `0x0733E009` and `0x21537AD4` appear both in NvAPIWrapper (2017–2020, Windows) and in LACT (2025, Linux), and still work **(community)**.
- The **structs** are the risk. They are versioned (V1, V2, V3), their sizes change on new architectures, and LACT ships per-architecture handling. A wrong struct size returns `NVAPI_INCOMPATIBLE_STRUCT_VERSION` at best, or writes garbage at worst **(inference)**.
- Every new GPU generation needs re-validation, as Afterburner's own Blackwell update cycle shows **(community)**.

**Legal and licence risk:**

- The public NVAPI SDK is MIT, but private IDs are not part of it.
- The GeForce driver licence (§2.2) says: "You may not reverse engineer, decompile, or disassemble the SOFTWARE". Source: https://www.nvidia.com/en-us/drivers/geforce-license
- Using IDs that others published is not the same as reverse-engineering the driver ourselves. Still, it is outside any NVIDIA grant, and NVIDIA can break it at will. Many free tools have done this for more than 10 years with no known enforcement **(inference / community)**.
- For a private, non-distributed app the practical risk is low. If the app is ever published, re-assess.

**Safety:** there is no public range contract. The driver still enforces vBIOS voltage and power caps (Afterburner's "voltage" slider only reaches the vBIOS max) **(community)**. Bad VF-curve writes can cause crashes or TDRs, but not hardware damage under those caps **(community)**.

## 4. NVIDIA App Automatic Tuning

- NVIDIA App → System → Performance → Automatic Tuning.
  - It scans for about 10–20 minutes, applies a "safe overclock" and re-checks periodically.
  - It is for GeForce RTX desktops and laptops.
  - Sources: https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-update-av1-performance-tuning/ and https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/
- **There is no public API, CLI or SDK.** The scanner technology exists for partners as the "OC Scanner/Scanner API" through NDA NVAPI (see section 3). It is not available to us.
- **What we can do:** deep-link or launch the NVIDIA App and tell the user where the toggle is. The NVIDIA App's tuning and our NVML settings would fight each other. The app should warn the user to use one or the other **(inference)**.

## Anti-cheat notes

- nvidia-smi, NVML and NVAPI are all user-mode calls into the already-loaded, signed NVIDIA driver. None of them loads a kernel driver of ours, so they do not trip the vulnerable-driver checks described in `research/anti-cheat-requirements.md` **(inference)**.
- The anti-cheat risk around overclocking tools has been their **kernel drivers**, not NVAPI. One example is Afterburner's old RTCore64.sys (CVE-2019-16098), which Vanguard blocks in vulnerable versions. Another is the RTSS overlay injection. Our NVML route avoids both.
- Applying clocks while a game runs is fine for anti-cheat, but should be done before launch to avoid stutter or a TDR mid-match **(inference)**.

## Implementation sketch (for the ticket)

1. Run an elevated helper (the existing admin path). Load `nvml.dll` from System32 and call `nvmlInit_v2`.
2. For each GPU, probe the getters: power constraints, clock-offset ranges per P-state, and supported clocks. Show only the controls that do not return `NOT_SUPPORTED`.
3. Apply settings in this order: power limit, then offsets, then locked clocks. Store the previous and default values for undo, which is `ResetGpuLockedClocks`, `ResetMemoryLockedClocks`, the default power limit and offset 0.
4. Re-apply at logon from an elevated scheduled task. Offer "apply on boot" as an opt-in, with a safe-mode skip such as holding Shift or a crash counter.
5. VF curve and voltage boost go to a backlog item: "experimental, private NVAPI". Port the struct layouts from LACT under MIT and test per GPU generation.
