# Pick the tech stack

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/4

Researched 2026-10-15 against primary sources (Microsoft Learn, official framework docs, vendor SDK pages and repos). Each fact links to its source. Lines marked **(judgment)** are my opinion, not something a source says.

## Answer

**Use C# on .NET 10 (LTS), with WPF for the UI, plus a .NET Windows Service for the background and privileged work.**

- **Shape:** build three projects.
  - `Core` is a class library holding every tweak as an apply / describe / undo unit.
  - `Service` is a .NET Worker installed as a Windows Service. It runs elevated, applies per-game profiles when a game starts, and does every privileged write.
  - `App` is a WPF app (Fluent theme) with a tray icon. It talks to the service over a named pipe.
  - For a first milestone, `App` can simply run `requireAdministrator` and call `Core` in-process. The service can come in when per-game profiles land.
- **Why .NET:** it has first-party, managed access to almost everything the app touches:
  - registry, services, WMI (including the BCD WMI provider), in-process PowerShell 7, and the winget COM API (Microsoft ships a NuGet package for it);
  - any other Win32 call through CsWin32, which generates the P/Invoke code;
  - vendor C/C++ SDKs. AMD documents official C# bindings for ADLX. NVAPI and Intel IGCL are C headers you P/Invoke, or wrap in a small C++ shim DLL.
- **Why WPF over WinUI 3:**
  - Microsoft's own release notes record that elevated WinUI 3 apps crashed during drag-and-drop.
  - A single-file EXE needs WinUI to be both unpackaged and self-contained, and it extracts to a temp folder.
  - WinUI tests that touch XAML need a special test-app host.
  - WPF is mature and open source, and it has a native Windows 11 Fluent theme since .NET 9.
- **Rust + Tauri** is workable: it supports a custom `requireAdministrator` manifest, and Microsoft maintains `windows-rs`. But it splits the code into Rust and a web front end, and every COM/WMI/vendor call becomes `unsafe` FFI. **C++/Qt** carries LGPL obligations and is the hardest language for an AI to write safely.
- **Distribution without signing:**
  - SmartScreen will show "Windows protected your PC → Run anyway" on every new unsigned build. Reputation never carries over to a new unsigned version.
  - **Smart App Control**, if a friend has it on, blocks unsigned, unknown code outright. Signing with a Trusted Root CA certificate is the only clean fix. Microsoft's Artifact Signing (formerly Trusted Signing) starts at $9.99/month.
  - For a few friends: ship unsigned, tell them to click "More info → Run anyway", and check that Smart App Control is off. Revisit signing only if that becomes a problem.

## Requirements that drive the choice

| Need | What it implies |
|---|---|
| Registry, services, BCD, power plans, scheduled tasks, Windows Update policy | Admin rights and a lot of Win32/WMI calls |
| winget installs | winget COM API or the `winget.exe` CLI |
| PowerShell-only tweaks (e.g. Appx removal) | In-process PowerShell hosting or a `powershell.exe` child process |
| NVIDIA / AMD / Intel GPU tuning and driver settings | C/C++ vendor SDKs (NVAPI, ADLX, IGCL) |
| Per-game profiles that auto-apply on launch | Something running in the background, elevated, watching process starts |
| Every change reversible | A testable core where each tweak records its prior state, with no UI attached |
| AI writes the code, owner tests on real hardware | A mainstream language, strong typing, fast unit tests that run off-device |

## Running elevated (UAC and manifest)

- An embedded manifest sets the execution level: `asInvoker`, `highestAvailable` or `requireAdministrator`. Microsoft says to "always embed an explicit manifest", keep most apps `asInvoker`, and "separate elevated operations into a helper process", triggering UAC with `ShellExecute` `runas` or the COM elevation moniker. https://learn.microsoft.com/en-ie/Windows/Win32/secbp/running-with-administrator-privileges
- **.NET (WPF or WinUI):** add an `app.manifest` with `requestedExecutionLevel`. This is the standard Win32 mechanism above.
- **WinUI 3 caveat:** support for running elevated arrived in Windows App SDK 1.1. Its documented known limitations are that "Elevated WinUI 3 apps crash when dragging an element during a drag-and-drop interaction" and that notifications from elevated unpackaged apps are not supported. https://learn.microsoft.com/en-gb/windows/apps/windows-app-sdk/release-notes-archive/stable-channel-1.1 (These notes are from 1.1. Later releases may have fixed the crash, but I did not verify that.)
- **Tauri:** `tauri_build::WindowsAttributes::app_manifest()` embeds a custom manifest. The docs' own example uses `requireAdministrator`. If you replace the manifest and use dialogs, you must keep the Common Controls v6 dependency yourself. https://docs.rs/tauri-build/latest/tauri_build/struct.WindowsAttributes.html
- **C++ (Qt/Win32):** the same manifest mechanism, set through the linker or a resource file.
- **Implication:** a background service running as an elevated account avoids a UAC prompt on every game launch. With that design the UI can stay `asInvoker`, which also sidesteps elevated-UI quirks. A Windows service is created with `sc.exe create` from an elevated prompt, which an installer can do. https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service

## Calling Windows: Win32, WMI, registry, services, BCD, PowerShell, winget

| Surface | C# / .NET | Rust | C++ |
|---|---|---|---|
| Win32 P/Invoke | CsWin32 generates "strongly typed P/Invoke and COM Interop bindings" at compile time, with `SafeHandle` overloads and docs linking back to Learn. https://github.com/microsoft/CsWin32 | `windows` / `windows-sys` crates from Microsoft give typed Win32, COM and WinRT bindings. https://github.com/microsoft/windows-rs | Native |
| Registry | `Microsoft.Win32.Registry` / `RegistryKey` (built in) https://learn.microsoft.com/sv-se/dotnet/api/microsoft.win32.registry?view=netcore-3.1 | windows-rs has a safe registry crate (same repo) | Native |
| Services | `ServiceController`. A service host is built with `Microsoft.Extensions.Hosting.WindowsServices` and `AddWindowsService`. https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service | windows-rs has a crate for writing services (same repo) | Native |
| WMI | `System.Management` (`ManagementObjectSearcher`, `ManagementClass`) https://learn.microsoft.com/en-us/dotnet/api/system.management | Raw COM through windows-rs | Raw COM |
| BCD | The BCD WMI provider is "a set of classes exposed as COM objects that support programmatic access to BCD stores", so it is reachable through WMI. https://learn.microsoft.com/en-us/previous-versions/windows/desktop/bcd/about-bcd | Same, through COM | Same |
| PowerShell | `Microsoft.PowerShell.SDK` lets a self-contained .NET app "run arbitrary PowerShell functionality without depending on any external PowerShell installations". It hosts PowerShell 7 only; Windows PowerShell 5.1 needs the reference assemblies or a child process. https://learn.microsoft.com/en-us/powershell/scripting/dev-cross-plat/choosing-the-right-nuget-package | Spawn `powershell.exe` | Spawn `powershell.exe` |
| winget | NuGet `Microsoft.WindowsPackageManager.ComInterop` (Microsoft), described as "artifacts useful for integration with the Windows Package Manager COM interface". It targets `net8.0-windows10.0.26100`+. https://packages.nuget.org/packages/Microsoft.WindowsPackageManager.ComInterop The API is an out-of-process COM server (`Microsoft.Management.Deployment`) meant for "packaged apps with the packageManagement capability and other higher privilege processes". https://github.com/microsoft/winget-cli/blob/master/doc/specs/%23888%20-%20Com%20Api.md | COM through windows-rs, or the CLI | C++/WinRT (spec examples are C++/WinRT) |

**(judgment)** .NET is the only option where nearly every row is a first-party managed API. In Rust and C++, WMI and PowerShell mean hand-written COM code or shelling out.

## Vendor driver and overclocking SDKs

| SDK | What it is | How to use it from each stack |
|---|---|---|
| **NVIDIA NVAPI** | "Core software development kit, designed for direct access to NVIDIA GPUs and drivers" on Windows 10+. Covers driver, GPU (including thermal and cooling), display and driver-settings (DRS) management, and is on GitHub. https://developer.nvidia.com/nvapi The public repo is MIT-licensed C headers plus `nvapi64.lib`; "the actual NVAPI implementation is provided by the NVIDIA GPU driver". https://github.com/NVIDIA/nvapi | C/C++ headers, so you call them directly from C++, P/Invoke them from C#, or FFI them from Rust. **Not verified:** whether clock/voltage overclocking calls are in the public headers. NVIDIA's page does not mention overclocking. Treat NVIDIA GPU OC as a risk to prototype early. |
| **AMD ADLX** | "Access features and functionality of AMD systems": display, 3D graphics, performance monitoring, GPU tuning. https://github.com/GPUOpen-LibrariesAndSDKs/ADLX The docs list GPU auto/preset tuning and manual fan, graphics, power and VRAM tuning samples, plus official guides for **C#**, Java, Python and Rust bindings alongside C/C++. https://gpuopen.com/manuals/adlx/programming-with-adlx/adlx-programming-guide/quick-start | Officially supported from C# and Rust; native in C++. |
| **Intel IGCL** | Intel's recommended SDK, replacing the OEM-only CUI SDK. https://www.intel.com/content/www/us/en/support/articles/000097877/graphics.html It has Engine, Fan, Telemetry, Frequency, Memory, **Overclock**, PCI, Power and Temperature APIs, plus 3D/display/media controls. Performance and telemetry APIs are 64-bit only. https://github.com/intel/drivers.gpu.control-library | C header and wrapper code, so you P/Invoke from C# or FFI from Rust. |
| **AMD Ryzen Master Monitoring SDK** | "Read-only access" to processor metrics, with no control. https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html | CPU overclocking (and RAM overclocking) has no public vendor control SDK in what I found, so plan those features as "describe and link to the BIOS or the vendor tool" rather than "apply". |

**(judgment)** All the vendor SDKs are C/C++. The cleanest pattern for C# (and Rust) is a thin **C++ shim DLL per vendor** that exposes a few flat `extern "C"` functions, P/Invoked from the core. ADLX can skip the shim because AMD ships C# binding guidance.

## Background component for per-game profiles: service or tray app

- **Detecting a game launch:** the WMI event class `Win32_ProcessStartTrace` "indicates that a new process has started" and carries ProcessName, ProcessID and SessionID. https://learn.microsoft.com/ja-jp/previous-versions/windows/desktop/krnlprov/win32-processstarttrace In practice this kernel trace provider usually requires admin; the page does not say so, so this is unverified. A non-elevated fallback is polling the process list.
- **Session 0:** since Vista, "the operating system isolates services in Session 0 and runs applications in other sessions". https://learn.microsoft.com/en-us/previous-versions/windows/hardware/design/dn653293(v=vs.85) So a service cannot show UI. Any notifications or a tray icon must live in a user-session process.
- **.NET service hosting** is first-party: `AddWindowsService`, a `BackgroundService`, publish as a single file, register with `sc.exe create`, and set restart-on-failure with `sc.exe failure`. https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
- **IPC:** `System.IO.Pipes` (`NamedPipeServerStream`) is built in. https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstream
- **Recommendation:**
  - The service runs elevated. It watches process starts, applies and reverts profiles, and owns the undo journal.
  - The WPF app runs as `asInvoker`. It shows the UI and tray icon and sends commands over a named pipe that is ACL'd to local admins and the interactive user.
  - The alternative, an elevated tray app started at logon by a scheduled task, avoids writing a service. But it needs a "highest privileges" task and still dies if the user closes it. **(judgment)**

## Packaging a single installer or EXE for a few friends

- **.NET single-file:**
  - `PublishSingleFile` plus a self-contained runtime identifier gives one EXE, but only managed DLLs are bundled by default.
  - `IncludeNativeLibrariesForSelfExtract=true` folds native DLLs in; they are extracted to `%TEMP%/.net` at startup.
  - `Assembly.Location` returns an empty string, so use `AppContext.BaseDirectory`.
  - https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- **WinUI 3 single-file** works only when the app is **both unpackaged and self-contained** (Windows App SDK 1.5+). It needs `WindowsPackageType=None`, `WindowsAppSDKSelfContained`, `EnableMsixTooling` and `IncludeAllContentForSelfExtract`, and it "is not a zero-extraction binary". Unpackaged apps also lose manifest-based (package identity) features. https://learn.microsoft.com/en-us/WINDOWS/APPS/package-and-deploy/unpackage-winui-app
- **Tauri:**
  - It builds an NSIS `.exe` or WiX `.msi`; MSI can only be built on Windows.
  - WebView2 ships with Windows 11, and the installer can bootstrap it (0 MB) or embed the offline installer (~127 MB).
  - The default per-user install needs no admin; `perMachine` does.
  - https://v2.tauri.app/distribute/windows-installer/
  - Extra binaries (for example a vendor shim EXE) ship as "sidecars" with target-triple names and shell-plugin permissions. https://v2.tauri.app/develop/sidecar
- **C++/Qt:** the LGPL requires dynamic linking (or giving users the app source), that users can relink a modified Qt, and that Qt's source or a written offer ships with the app. https://www.qt.io/licensing/open-source-lgpl-obligations
- **SmartScreen without signing:**
  - Unsigned files show "Windows protected your PC" and need "Run anyway". "When a file is not signed, SmartScreen reputation must build for each new version … starting with zero reputation."
  - Building reputation "can take several weeks and hundreds of clean installs", so a few friends will never clear it. Self-signed certificates count as unsigned.
  - EV certificates "no longer bypass SmartScreen".
  - Artifact Signing (formerly Trusted Signing) starts at $9.99/month and lets reputation accumulate across versions.
  - https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation
- **Smart App Control (Windows 11):** in enforcement mode, "unknown, unsigned code [is] blocked by default". Apps run only if Microsoft's cloud recognises them or they are signed by a Trusted Root Program CA. It can be On, Evaluation or Off. https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview A friend with it **On** cannot run an unsigned build at all. This applies to every stack equally.
- **Plan (judgment):**
  - Ship a self-contained single-file EXE (or a simple installer that copies it and registers the service).
  - Document the "More info → Run anyway" step and the Smart App Control check.
  - Treat Artifact Signing as the escape hatch if it ever matters.

## Framework comparison

| | **C# / .NET 10 + WPF** | **C# / .NET 10 + WinUI 3** | **Rust + Tauri 2** | **C++ + Qt 6 / Win32** |
|---|---|---|---|---|
| Maturity / status | Open source on GitHub, Windows-only. https://learn.microsoft.com/dotnet/desktop/wpf/overview Fluent (Windows 11) theme with light/dark and accent colour since .NET 9. https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net90 | Microsoft's current WinUI / Windows App SDK track. https://learn.microsoft.com/windows/apps/get-started | Tauri v2 stable; UI in WebView2 | Very mature |
| Running elevated | Plain manifest, no known framework issues | Supported since WinAppSDK 1.1, with documented drag-drop crash and notification limits when elevated | Custom manifest through `tauri-build` | Plain manifest |
| Win32/WMI/PS/winget | First-party managed APIs for nearly all of it | Same as WPF | `windows-rs` covers Win32/COM; WMI/PS by hand or by shelling out | Native but verbose COM |
| Vendor SDKs (C/C++) | P/Invoke or a C++ shim; ADLX has official C# bindings | Same | FFI; ADLX has an official Rust guide | Native, easiest |
| Background service | First-party Worker/Service template | Same (service is separate from UI anyway) | windows-rs service crate | Native |
| Single EXE | Yes (self-contained single-file) | Yes, only unpackaged + self-contained, with extraction | NSIS installer, WebView2 dependency | Yes; LGPL rules for Qt; Win32-only UI is tiny |
| Testability | Core library tests with xUnit/MSTest off-UI; MVVM over WPF binding | Core tests the same; XAML tests need a WinUI unit-test app with `[UITestMethod]` on a UI thread. https://learn.microsoft.com/en-us/windows/apps/winui/winui3/testing | Strong `cargo test` for core; front-end tested separately | GoogleTest/Qt Test; slower build loop |
| Support window | .NET 10 LTS to **14 Nov 2028**; .NET 8 and 9 both end **10 Nov 2026**. https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core | Same | n/a | n/a |
| AI writing most of it **(judgment)** | Best fit. Huge public corpus of C#/WPF and Win32-from-C#; memory-safe; compiler catches most mistakes; one language end to end | Good, but less public sample code and more version churn, so more confident-but-wrong API use | Good Rust, but two languages (Rust + TS/JS) and lots of `unsafe` COM/FFI, where AI mistakes are costly | Weakest. Manual memory/COM lifetime errors are easy to write and hard for a non-programmer to spot |

## Recommendation and trade-offs

**Pick C# on .NET 10 LTS, with a WPF UI using the Fluent theme, a .NET Worker Windows Service, and a shared `Core` library.** Put every tweak in `Core` behind one interface: `Describe`, `Detect current state`, `Apply` (which records the prior state), and `Revert`. Unit-test it against fakes for the registry, services and WMI, so most of the code is tested without touching a real PC. Add small C++ shim DLLs for NVAPI and IGCL when GPU tuning starts, and use AMD's C# ADLX bindings.

Trade-offs accepted:

- **WPF over WinUI 3:** you lose the newest WinUI controls and the "Microsoft's current framework" status. You gain no elevated drag-drop crash, simpler single-file publishing and simpler tests. The Fluent theme closes most of the look gap.
- **.NET over Rust/Tauri:** the EXE is bigger (self-contained runtime) than a Tauri binary, and memory safety comes from a runtime rather than the borrow checker. You gain one language, first-party WMI/PowerShell/winget/service APIs, and the best AI code quality.
- **.NET over C++:** vendor SDK calls need P/Invoke or shims instead of direct `#include`. You avoid manual memory/COM bugs and Qt LGPL obligations.
- **Service + unelevated UI over a single elevated app:** more moving parts (IPC, install and uninstall of a service). You gain per-game auto-apply without UAC prompts, and a clean privilege split. It is fine to start with a single `requireAdministrator` WPF app and add the service later, because `Core` doesn't change.
- **Unsigned distribution:** friends get a SmartScreen warning on every build, and anyone with Smart App Control On is blocked. Artifact Signing ($9.99/month) is the fix if that matters.

Open risks to prototype early:

1. Whether NVIDIA clock and voltage overclocking is reachable through the **public** NVAPI. It is unverified, and there may be no supported path.
2. CPU and RAM overclocking: no public AMD or Intel control SDK was found. These are likely limited to "describe and open the vendor tool or BIOS".
3. Whether `Win32_ProcessStartTrace` needs admin. It does not matter if the service runs elevated.

## Sources

- https://learn.microsoft.com/en-ie/Windows/Win32/secbp/running-with-administrator-privileges
- https://learn.microsoft.com/en-gb/windows/apps/windows-app-sdk/release-notes-archive/stable-channel-1.1
- https://learn.microsoft.com/en-us/WINDOWS/APPS/package-and-deploy/unpackage-winui-app
- https://learn.microsoft.com/en-us/windows/apps/winui/winui3/testing
- https://learn.microsoft.com/windows/apps/get-started
- https://learn.microsoft.com/dotnet/desktop/wpf/overview
- https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net90
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
- https://learn.microsoft.com/en-us/dotnet/api/system.management
- https://learn.microsoft.com/sv-se/dotnet/api/microsoft.win32.registry?view=netcore-3.1
- https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstream
- https://learn.microsoft.com/en-us/powershell/scripting/dev-cross-plat/choosing-the-right-nuget-package
- https://learn.microsoft.com/en-us/previous-versions/windows/desktop/bcd/about-bcd
- https://learn.microsoft.com/ja-jp/previous-versions/windows/desktop/krnlprov/win32-processstarttrace
- https://learn.microsoft.com/en-us/previous-versions/windows/hardware/design/dn653293(v=vs.85)
- https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation
- https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://github.com/microsoft/CsWin32
- https://github.com/microsoft/windows-rs
- https://github.com/microsoft/winget-cli/blob/master/doc/specs/%23888%20-%20Com%20Api.md
- https://packages.nuget.org/packages/Microsoft.WindowsPackageManager.ComInterop
- https://docs.rs/tauri-build/latest/tauri_build/struct.WindowsAttributes.html
- https://v2.tauri.app/distribute/windows-installer/
- https://v2.tauri.app/develop/sidecar
- https://www.qt.io/licensing/open-source-lgpl-obligations
- https://developer.nvidia.com/nvapi
- https://github.com/NVIDIA/nvapi
- https://github.com/GPUOpen-LibrariesAndSDKs/ADLX
- https://gpuopen.com/manuals/adlx/programming-with-adlx/adlx-programming-guide/quick-start
- https://www.intel.com/content/www/us/en/support/articles/000097877/graphics.html
- https://github.com/intel/drivers.gpu.control-library
- https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html
