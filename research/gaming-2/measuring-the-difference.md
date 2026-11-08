# Find how the app can measure the difference it makes

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/88

Researched 2026-10-30. Most of it comes from primary sources: the PresentMon and CapFrameX source code and docs on GitHub, Microsoft's own docs (read from the MicrosoftDocs GitHub repos that feed Microsoft Learn), NVIDIA's FrameView and developer pages, and Gamers Nexus's published test method. Each claim has a link. Anything marked **(community)** comes from a secondary or community source. Anything marked **(inference)** is my own reasoning, and no source says it. The shared web-fetch and search budget ran out partway through, so some pages are listed but were not opened. Those are marked **not opened**, and **Gaps** at the end lists them.

## Answer

- **Use the PresentMon console tool for games, run by our elevated service.** It is MIT-licensed, so we can ship it free with a copyright notice. It reads Windows' own graphics events (ETW). It never touches the game process. It gives every frame's time, GPU Busy, CPU Busy, display latency, a click-to-photon estimate, the present mode and the frame type. CapFrameX, an MIT-licensed .NET 10 WPF app, does exactly this.
- **Don't use the PresentMon 2.x service and API.** The API only works when Intel's PresentMon service is installed on the PC. Intel says a client's own copy of the API DLL is "not guaranteed" to stay compatible. That means a second install for every user, for no gain we need.
- **The service does the capture, and the window shows the results.** Only admins, Performance Log Users and system services can start ETW sessions, so the window can't capture. The service already sees game .exe files start and stop for per-game profiles, so it can capture each play session automatically.
- **Don't draw an overlay inside the game.** Injected overlays are what anti-cheats object to. Even a separate overlay window can push a game out of "independent flip" and add latency, unless the GPU has multiplane overlay (MPO) support. NVIDIA warns about exactly this in FrameView 2.0. Show results in the app window, and record the present mode so the app only compares like with like.
- **Normal play is noisy, so compare in blocks and give an honest verdict.** Split each session into 1-minute blocks. Only match blocks with the same context: same game build, present mode, frame generation state, and GPU-bound vs CPU-bound. Compare medians across at least 3 sessions on each side, with a confidence range taken from whole sessions. The verdict is "better", "worse", "no clear difference" or "not enough play yet". Gamers Nexus shows that 1% lows, and above all 0.1% lows, are much noisier than average FPS.
- **The fairest test is to switch an instant change on and off between sessions.** A per-game profile can turn one instant change on in some sessions and off in others, so game patches and driver updates land on both sides. This runs only when the user asks to test that change.
- **Outside games, measure with Windows' own data:**
  - DPC and ISR times, from kernel ETW in a private session.
  - Boot time, from the Diagnostics-Performance event 100.
  - Idle CPU, memory and process count, from `GetSystemTimes` and `GetPerformanceInfo`.
  - Ping, from ICMP.
  - Input latency, from PresentMon's estimate, or Reflex PC Latency in games that support it.
- **Anti-cheat:** no first-party anti-cheat page mentions ETW capture or PresentMon. ETW capture reads OS events and doesn't touch the game, so I rate it low risk **(inference)**. CapFrameX does list "an anti-cheat that blocked PresentMon" as a known failure. The app should report a failed capture plainly and never try to work around it.

## 1. What to measure in a game

PresentMon writes one CSV row per frame, with all times in milliseconds ([console README](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)). These are the columns that matter for us:

| What the user sees | PresentMon column | What it means (from the README) |
|---|---|---|
| Frame time / FPS | `MsBetweenPresents` (and 2.x `FrameTime`) | Time between this `Present()` call and the previous one |
| Frames actually shown | `MsBetweenDisplayChange`, `DisplayedTime` | How long the previous frame stayed on screen; `NA` if the frame was never shown |
| GPU Busy | `MsGPUBusy` | Time "at least one GPU engine is executing work from the target process" |
| CPU Busy | `MsCPUBusy` | How long the CPU worked on the frame before presenting it |
| Display latency | `DisplayLatency` | From the start of the frame until it was on screen |
| Click-to-photon (estimate) | `MsClickToPhotonLatency`, `MsAllInputToPhotonLatency` | From the earliest mouse click (or any input) that fed this frame until it was shown |
| PC Latency (Reflex games only) | `MsPCLatency` with `--track_pc_latency` | Input reaching the PC until the frame is sent to the display. It "requires application instrumentation" |
| Frame generation | `FrameType` with `--track_frame_type` | Whether the app rendered the frame or a driver or SDK generated it |
| How the frame reached the screen | `PresentMode` | E.g. `Hardware: Independent Flip` versus `Composed: Flip` |
| Smoothness | `MsAnimationError` | The previous frame's CPU delta minus its display delta |

**Where the bottleneck is (inference):** if GPU Busy is close to the frame time, the game is GPU-bound. Windows and CPU tweaks then can't raise average FPS much. If GPU Busy is well below the frame time, the CPU or Windows is the limit, and that's where our tweaks can help. If the game is capped by vsync or a frame limiter, average FPS can't change. Only the lows, latency and CPU Busy can. The app should say which case each session was in, so a "no change" isn't misread.

**Frame generation:** CapFrameX has a switch that computes metrics from display times (`MsBetweenDisplayChange`) "when analyzing displayed frames with Frame Generation" ([CapFrameX README](https://github.com/CXWorld/CapFrameX)). FrameView 2.0 now "correctly counts dropped and zero-display presents". NVIDIA says this makes 1% lows better match what the player sees ([FrameView release notes](https://www.nvidia.com/en-eu/geforce/technologies/frameview/release-notes/)). We should compute from displayed frames, and never compare a frame-generation session with a session without it.

### How 1% and 0.1% lows are calculated

Tools disagree on what "1% low" means. The app must pick one definition and name it in the window. The CapFrameX blog says comparisons should state "which tools you use and especially which metrics you use" ([CapFrameX blog, 2020, updated 2022](https://www.capframex.com/blog/post/Explanation%20of%20different%20performance%20metrics)).

| Name | How it's calculated | Used by |
|---|---|---|
| **Average FPS** | Frame count × 1000 ÷ sum of frame times. This weights by time; it is *not* the mean of each frame's FPS | CapFrameX code: `sequence.Count * 1000 / sequence.Sum()` ([source](https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.Statistics.NetStandard/FrametimeStatisticProvider.cs)) |
| **P1 / P0.1 (percentile)** | The FPS that separates the slowest 1% (or 0.1%) of frames from the rest | CapFrameX `P1` = 0.01 quantile of per-frame FPS ([source](https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.Statistics.NetStandard/FrametimeStatisticProvider.cs)); FrameView "percentile" ([FrameView guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-8-user-guide-web-version.pdf)) |
| **1% / 0.1% low average** | Take the slowest 1% of frames and average them | FrameView "1% Low": "Takes the slowest 1% frames and averages them" ([guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-8-user-guide-web-version.pdf)); CapFrameX "1% low average" |
| **1% low integral** | The FPS you were below for 1% of the total *time* | CapFrameX ([blog](https://www.capframex.com/blog/post/Explanation%20of%20different%20performance%20metrics)) |
| **Stutter** | Frames longer than 2.5× the average frame time (default) | CapFrameX ([README](https://github.com/CXWorld/CapFrameX)) |

**Recommendation (inference):**
- Use **average FPS** and **P1** (the percentile) for verdicts, because a percentile isn't thrown off by one huge spike. The CapFrameX blog says the 1st percentile "is much more stable" than min FPS.
- Count **stutters** separately, so big hitches stay visible.
- Show **0.1% lows** only for a whole session, never per block. The CapFrameX blog warns that a 0.1% low built from 2–3 frames gives "a different result every time". For P0.1 it says you need well over 1,000 frames.

## 2. Ways to capture frames, compared

| Option | Licence / cost | Admin and ETW | Can we ship it in a free app? | Notes |
|---|---|---|---|---|
| **PresentMon console** (`PresentMon-<ver>-x64.exe`) | MIT, © Intel 2017–2024 ([LICENSE](https://github.com/GameTechDev/PresentMon/blob/main/LICENSE.txt)) | Needs admin or Performance Log Users ([README](https://github.com/GameTechDev/PresentMon)) | **Yes.** Include the copyright and permission notice | Ships as a release asset, e.g. `PresentMon-2.3.1-x64.exe` ([console README](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)). It writes CSV to a file or to standard output (`--output_stdout`) |
| **PresentMon 2.x service + PresentMon API (SDK)** | MIT source | The service runs as Windows service `PresentMonSharedService` ([service README](https://github.com/GameTechDev/PresentMon/blob/main/README-Service.md)) | **Not well.** Clients "should… dynamically load PresentMonAPI2.dll" installed by the service. "If a client ships their own copy of PresentMonAPI2.dll, binary compatibility with the service will not be guaranteed" ([service README](https://github.com/GameTechDev/PresentMon/blob/main/README-Service.md)) | Needs Intel's PresentMon installed. It adds GPU telemetry, but we already have our own sensors. C API, so it would need P/Invoke |
| **Our own ETW consumer** (C#, TraceEvent) | TraceEvent is MIT, © .NET Foundation ([LICENSE](https://github.com/microsoft/perfview/blob/main/LICENSE.TXT)) | Same ETW rules | Yes | We'd have to re-create PresentMon's matching of present events across DXGI, DxgKrnl, DWM and Win32k ([PresentMonTraceSession.cpp](https://github.com/GameTechDev/PresentMon/blob/main/PresentData/PresentMonTraceSession.cpp)). Too much work for frames. It does fit DPC/ISR (section 5) |
| **CapFrameX** (reference design) | MIT, © 2018–2026 DevTechProfile ([LICENSE](https://github.com/CXWorld/CapFrameX/blob/master/LICENSE)) | Starts its bundled `PresentMon\PresentMon*.exe` elevated (`Verb = "runas"`), with `--stop_existing_session --output_stdout --qpc_time_ms` ([PresentMonServiceConfiguration.cs](https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.PresentMonInterface/PresentMonServiceConfiguration.cs), [PresentMonCaptureService.cs](https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.PresentMonInterface/PresentMonCaptureService.cs)) | It's a separate app. Its design is worth copying | v1.9.1 is on .NET 10 and bundles PresentMon 2.6.0 ([README](https://github.com/CXWorld/CapFrameX)). Its integration tests need "administrator privileges (ETW)" ([CLAUDE.md](https://github.com/CXWorld/CapFrameX/blob/master/CLAUDE.md)) |
| **NVIDIA FrameView** | Free NVIDIA tool. The guide gives no licence or redistribution terms ([guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-8-user-guide-web-version.pdf)) | — | **No.** No redistribution rights found | Uses "the open-source PresentMon project" plus Reflex PC Latency ([NVIDIA blog, 2023](https://developer.nvidia.com/blog/understanding-and-measuring-pc-latency/)). The guide's 1% Low is an average, not a percentile. FrameView 2.0 (page dated Sept 2026) has a new overlay window (section 6) |
| **NVIDIA App overlay** | Free, NVIDIA only (inference) | — | No; it is a separate app with no API found | Shows "GPU and CPU utilization, FPS 1% Low, and PC and system latency", with "integrated NVIDIA Reflex Analyzer support" ([NVIDIA App page](https://www.nvidia.com/en-us/software/nvidia-app/)). How it captures isn't documented. Users can check our numbers against it |

**PresentMon versions:**
- The latest release is **v2.6.0**, released "21 Sep". The page shows no year; 2026 is my inference from the order of releases and CapFrameX already bundling it.
- v2.6.0 adds tracking of "D3D12 pipeline state object compilation via ETW" (shader compiles) and cuts the service's CPU use under load ([releases](https://github.com/GameTechDev/PresentMon/releases)).
- v2.2.0 cut ETW event delay "from about 1000 ms to about 30 ms".
- v2.4.0 added `MsPCLatency` as beta ([releases](https://github.com/GameTechDev/PresentMon/releases)).

### How to ship PresentMon inside the app

1. Bundle a pinned version of the official console exe from the GitHub release, with its SHA-256 written into our code. Put the MIT notice in our third-party notices ([LICENSE](https://github.com/GameTechDev/PresentMon/blob/main/LICENSE.txt)). If we'd rather build it ourselves, the standalone console builds without CEF, WiX or Node.js, and those builds are unsigned ([BUILDING.md](https://github.com/GameTechDev/PresentMon/blob/main/BUILDING.md)).
2. The service copies it into `%ProgramData%\ElyntOptimizer\tools\presentmon\` and checks the hash before every start **(inference)**. Velopack installs the app per user, and a SYSTEM service must never run an exe that a normal user could swap. The repo already does this for driver installers and PawnIO ("Users can't write here, so a verified file can't be swapped", `src/Elynt.Service/ServicePaths.cs`).
3. Start it with a **name of our own**: `--session_name ElyntPresentMon`.
   - The default name is "PresentMon", and distinct names let captures run at the same time ([console README](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)).
   - Use `--stop_existing_session` only on our own name, or we'd kill CapFrameX's or another tool's capture.
   - Windows allows at most 64 ETW sessions at once ([Event Tracing Sessions](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/event-tracing-sessions.md)).
   - CapFrameX warns that HWiNFO and AIDA64 FPS features "may conflict… as they also rely on PresentMon-based mechanisms". It also names a leftover FrameView SDK as a cause of "ETW session conflict" ([README](https://github.com/CXWorld/CapFrameX)).
4. Suggested arguments: `--process_id <pid> --output_stdout --no_console_stats --qpc_time_ms --terminate_on_proc_exit --track_frame_type`.
   - Add `--track_pc_latency` only if the game uses Reflex (all are in the [console README](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)).
   - Leave input tracking on, so we get click-to-photon.
5. Read standard output line by line in the service. Turn rows into per-second and per-minute summaries, and keep the raw frames only for the last few sessions **(inference)**. At 200 FPS an hour of play is 720,000 rows.

## 3. What runs in the service and what runs in the window

| Job | Where | Why |
|---|---|---|
| Starting and stopping PresentMon, reading its output | **Service** | ETW control needs admin, Performance Log Users or a system service account ([Microsoft Learn](https://learn.microsoft.com/windows/desktop/etw/controlling-event-tracing-sessions)). Without admin, PresentMon also loses other accounts' process names ([README](https://github.com/GameTechDev/PresentMon)) |
| Knowing when a game starts and stops | **Service** (already done) | `WindowsProcesses` uses WMI process traces fed by kernel ETW (`src/Elynt.Windows/WindowsProcesses.cs`) |
| Per-block summaries, storage, verdicts | **Service** | It keeps working when the window is closed, like per-game profiles. Store per user SID under `%ProgramData%\ElyntOptimizer\measurements` (inference) |
| Tagging each session with context | **Service** | It knows which changes are in the Undo history and which per-game profile and tuning profile were on. It also knows the Windows build, driver version and game .exe version (inference) |
| DPC/ISR test, boot-time read, idle snapshot | **Service** | Kernel ETW and some event logs need admin (inference for the event log) |
| Charts, before/after cards, verdict text, "Test this change" flow | **Window** | The window shows things; it never captures. It can show a live readout over the pipe on a second screen |
| In-game overlay | **None in v1** (see section 6) | The must-have is to show the difference "IN THE PROGRAM ITSELF". The app window covers that, with no anti-cheat or latency risk (inference) |

## 4. Fair before-and-after

### How noisy are the numbers?

- **Fixed benchmark runs are fairly steady for average FPS, but less so for the lows.** Gamers Nexus measured how much repeat runs of the same scene differ, as a standard deviation ([Gamers Nexus, 16 Feb 2018](https://gamersnexus.net/guides/3240-bench-theory-reliability-standard-deviation-game-benchmarks)):
  - Average FPS varied from "functionally 0" (Metro: Last Light) to about 1–1.5 FPS (Ashes of the Singularity), and up to 2.6 FPS (Hellblade).
  - 1% lows were mostly about 1 FPS, with some runs at 2.5–4 FPS.
  - 0.1% lows ranged from 0 to 8 FPS in Hellblade. One Destiny 2 spike came from a stutter at the start of the test.
- **1% lows are less accurate than averages.** GN says: "The nature of a 99th-percentile metric is that its accuracy is reduced, as you're averaging from fewer data points." Multiplayer (Overwatch) varied more over short runs, so GN uses 5-minute runs there. Very high-FPS cards also varied more (same article).
- **GN judges each game against its own error margin.** It builds a confidence interval per game from the standard deviation. Its example: a 64 vs 66 FPS gap is "measurably better" in Metro, but it is inside the noise in Overwatch, so those cards count as "functionally the same" (same article).
- **CapFrameX's run history builds the same idea into a tool:**
  - It keeps the last N runs and marks outliers against "the median of a selectable metric" by a set percentage.
  - It can make you redo outlier runs ("Mark & replace").
  - It merges the runs' raw frame times rather than averaging their results ([README](https://github.com/CXWorld/CapFrameX)).
- **Normal play is far noisier than a fixed scene (inference).** Each session has a different map, player count, scene and length. Game patches, driver updates, temperatures and background jobs change between sessions. Shader compiles stutter on first runs, and PresentMon 2.6.0 can now flag PSO compiles ([releases](https://github.com/GameTechDev/PresentMon/releases)).
- **Frame times next to each other are linked (inference).** So 100,000 frames are *not* 100,000 independent samples. Confidence must come from comparing whole sessions or blocks, not single frames.
- **The academic method is the same (titles only, not opened):** report confidence intervals over repeated, independent runs, not single best-of runs. See [Georges, Buytaert & Eeckhout, "Statistically rigorous Java performance evaluation", OOPSLA 2007](https://dri.es/files/oopsla07-georges.pdf) and [Kalibera & Jones, "Quantifying Performance Changes with Effect Size Confidence Intervals"](https://ar5iv.labs.arxiv.org/html/2007.10899).

### How much play does a verdict need? (inference)

This is the standard two-group sample size rule:

> sessions per side ≈ 16 × (session-to-session spread ÷ smallest change worth finding)²

This is for about 80% power at the 5% level. Both "spread" and "change" are percentages of the metric. Some examples:

| Kind of comparison | Spread between runs (assumed) | Change we want to see | Sessions per side |
|---|---|---|---|
| Fixed spot or built-in benchmark, average FPS | ~1–2% (GN's spreads were mostly ≤1–2 FPS) | 3% | ~2–7 |
| Fixed spot, 1% low (P1) | ~3–5% | 5% | ~6–16 |
| Normal play, matched blocks, average FPS | ~5–10% (assumption, measure per PC) | 5% | ~16–64 |
| Normal play, matched blocks, average FPS | ~5–10% | 10% | ~4–16 |

The lesson is plain. Normal play can only show **big** changes, such as turning Memory Integrity off, which the catalogue puts at 4–6% FPS and more in some games. Most tweaks will honestly come out as "no clear difference". The app must say that, not invent a gain. Small effects need the fixed-spot quick test or the on/off test.

### Recommended method

The app should offer three levels, all automatic, so the user never has to set anything up.

**Level 1: automatic session tracking (always on for games with a per-game profile or with "Measure this game" switched on). (inference)**
1. When the game's .exe starts, the service starts PresentMon for that PID. It stops when the game exits; the per-game profile runner already sees both.
2. Throw away the first 2 minutes, which hold loading and the first shader compiles. Throw away any gap longer than 1 s (alt-tab, loading screens), and any block that is mostly PSO compiles.
3. Cut the rest into **60-second blocks**. For each block, store:
   - average FPS, P1 FPS and stutter count;
   - median GPU Busy ÷ frame time;
   - median CPU Busy, median display latency and median click-to-photon;
   - present mode and frame type share;
   - whether the game looked capped: frame times bunched at a fixed value.
4. Tag every session with what stayed the same or changed:
   - which changes from the Undo history were on;
   - the per-game profile and tuning profile;
   - Windows build, GPU driver version and the game .exe's file version.
5. **Only compare like with like.** A block is matched only when the game build, present mode, frame-generation state, cap state and GPU-bound/CPU-bound class (GPU Busy ÷ frame time ≥ 0.9 vs < 0.9) are all the same.
6. "Before" is matched blocks from sessions before a change, and "after" is matched blocks from sessions after it. If several changes were made at once, say the result is for all of them together. The app can't split them.
7. Statistic: the difference in the median block value, with a 95% range from a **bootstrap over whole sessions**. Resampling sessions, not blocks, keeps one long session from counting as many.
8. **Noise floor for this PC and game:** split the "before" sessions into two halves and compare them the same way (an A/A test). The difference you get is what "nothing changed" looks like here. If there isn't enough "before" data, use 3% for average FPS and 5% for P1.
9. The verdict:
   - **"Better"**: the whole 95% range is above the noise floor.
   - **"Worse"**: the whole range is below minus the noise floor.
   - **"No clear difference"**: the whole range is inside ± the noise floor.
   - **"Not enough play yet"**: anything else, or fewer than 3 sessions or 20 matched minutes on either side. In that case, show how much more play is needed.

**Level 2: "Test this change" (on/off test, opt-in, instant changes only).** (inference)
- For one instant change, the per-game profile switches it on in a random half of the sessions and leaves it off in the rest. It runs until both sides have enough matched minutes, for example 6 sessions each.
- Patches, drivers and the weather then land on both sides equally. This is the fairest test you can get from normal play.
- It uses the same temporary-change mechanism as per-game profiles, so nothing new needs undoing.
- The user is told the change will be off in some sessions.

**Level 3: "Quick test" (a fixed spot).** (inference, following CapFrameX's run history and GN's repeated passes)
- The app asks the player to stand in one spot, or run a built-in benchmark. It records 3 runs of 60 s with the change off and 3 with it on, in the order off-on-off-on-off-on, and drops the very first run as warm-up.
- The app flags outlier runs against the median, as CapFrameX does, and asks for a re-run.
- This is the only level that can find changes of a few percent.

**What the window shows (inference).**
- A card for each game and each change, with:
  - a before/after bar for average FPS, P1 and latency;
  - a small frame-time chart;
  - a verdict in plain words;
  - the conditions, for example "GPU-bound: Windows tweaks can't raise FPS here".
- Show the metric names and definitions next to the numbers.
- A good result is evidence for *this PC* only. It never changes a tweak's evidence tier.

## 5. Measuring outside games

| What | How (primary source) | Notes and fairness |
|---|---|---|
| **DPC and ISR time per driver** | Kernel ETW. The System Interrupt Provider has `SYSTEM_INTERRUPT_KW_DPC` (`EVENT_TRACE_FLAG_DPC`) and `SYSTEM_INTERRUPT_KW_GENERAL` (`EVENT_TRACE_FLAG_INTERRUPT`) ([System Providers](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/system-providers.md)). Each DPC event carries `InitialTime` ("DPC entry time") and `Routine` ("Use the address with the Image events to find which image started") ([DPC class](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/dpc.md)); the ISR event is similar ([ISR class](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/isr.md)). Duration = event time − entry time. TraceEvent already does this: `DPCTraceData.ElapsedTimeMSec` and `PerfInfoISR` ([KernelTraceEventParser.cs](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Parsers/KernelTraceEventParser.cs)) | **Use a private SystemTraceProvider session, not the NT Kernel Logger.** "There is only one NT Kernel Logger session", and if it's in use, StartTrace fails with `ERROR_ALREADY_EXISTS` ([NT Kernel Logger](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/configuring-and-starting-the-nt-kernel-logger-session.md)). Since Windows 8, SystemTraceProvider can feed "up to 8 logger sessions" ([SystemTraceProvider](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/configuring-and-starting-a-systemtraceprovider-session.md)). Run it as a fixed 2-minute idle test before and after a change. Don't run it during game capture; DPC tracing has a cost (inference). Report the longest DPC and ISR per driver, and how many went over 0.5 ms and 1 ms (inference) |
| **How LatencyMon does it** | **Not opened** (budget ran out). That it uses the same kernel DPC/ISR ETW events is my **inference** from the above | If LatencyMon uses the NT Kernel Logger, it and xperf would clash with each other. Our private session would not clash with them (inference) |
| **Boot time** | Event log `Microsoft-Windows-Diagnostics-Performance/Operational`, event ID 100. The search results show its fields `BootTime`, `MainPathBootTime` and `BootPostBootTime` ([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/2422685/diagnostics-perfomance-event-id-100-slow-boot), **community, not opened**). Microsoft's assessment docs define "main path boot" and "post on/off" ([Main Path Boot Duration](https://learn.microsoft.com/en-us/windows-hardware/test/assessments/main-path-boot-duration-and-main-path-resume-duration), [Post On/Off Duration](https://learn.microsoft.com/en-us/windows-hardware/test/assessments/post-on-off-duration), **not opened**) | Compare only real restarts. Fast Startup "shutdowns" resume a saved kernel, so they aren't the same kind of boot (inference). Use the median of at least 5 boots on each side, because pending updates make single boots swing (inference). Startup items are the obvious thing to test here |
| **Idle CPU** | `GetSystemTimes` returns idle, kernel and user time "in ticks" ([docs](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/processthreadsapi/nf-processthreadsapi-getsystemtimes.md)) | Sample for 2 minutes, starting 5 minutes after sign-in, and take the median (inference) |
| **Memory, process, thread and handle count** | `GetPerformanceInfo` → `PERFORMANCE_INFORMATION` has `CommitTotal`, `PhysicalAvailable`, `ProcessCount`, `ThreadCount` and `HandleCount` ([docs](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/psapi/ns-psapi-performance_information.md)) | The same idle snapshot. This shows what Startup & Services changes did, as numbers a user understands: "38 fewer background processes, 1.1 GB more free memory" |
| **Input latency** | PresentMon's `MsClickToPhotonLatency` and `MsAllInputToPhotonLatency` estimates ([console README](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)). In games that build in NVIDIA's PC Latency Stats (Reflex SDK or the Unreal plugin), PC Latency is measured "without any external devices" ([NVIDIA blog](https://developer.nvidia.com/blog/understanding-and-measuring-pc-latency/)) | Full click-to-screen latency needs hardware (a Reflex Analyzer monitor or similar). That costs money, so it's out of scope. Label our number "estimated" |
| **Ping** | ICMP echo with `IcmpSendEcho2` ([docs](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/icmpapi/nf-icmpapi-icmpsendecho2.md)). TCP round-trip time per connection with `GetPerTcpConnectionEStats`, but turning that on needs an administrator ([docs](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/iphlpapi/nf-iphlpapi-getpertcpconnectionestats.md)) | Most game traffic is UDP, so a game's own ping can't be read this way (inference). Ping, jitter and packet loss to a fixed host, before and after, is the fair proxy. Online-play settings are covered in another ticket |
| **Sleep and timer precision** | The `MeasureSleep` approach from the earlier catalogue ([valleyofdoom/TimerResolution](https://github.com/valleyofdoom/TimerResolution), **community**) | Fits the timer-resolution part of per-game profiles |

## 6. Anti-cheat and overlays

### Does ETW capture trip anti-cheat?

- **What PresentMon does:** it starts an ETW session and turns on Windows' own providers. These are D3D9, Direct3D12, DXGI, DxgKrnl, Dwm-Core, Win32k and Kernel-Process ([PresentMonTraceSession.cpp](https://github.com/GameTechDev/PresentMon/blob/main/PresentData/PresentMonTraceSession.cpp)). It doesn't open, read or write the game process **(inference from the source)**.
- **What anti-cheat vendors say:** I found no EAC, BattlEye, Vanguard or FACEIT page that mentions ETW, PresentMon, FrameView or CapFrameX (see Gaps).
  - The earlier anti-cheat note also found no first-party statement on monitoring tools (`research/anti-cheat-requirements.md`, row 12).
  - BattlEye says it "will not block popular programs" such as Discord, Fraps, OBS and XSplit ([BattlEye FAQ via Ubisoft](https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq), read for the earlier note).
- **Real-world signal:**
  - CapFrameX's diagnostics include "an anti-cheat that blocked PresentMon" as a known capture failure, but don't name the anti-cheat ([README](https://github.com/CXWorld/CapFrameX)).
  - NVIDIA ships FrameView, which is built on PresentMon, to reviewers for every GPU ([NVIDIA blog](https://developer.nvidia.com/blog/understanding-and-measuring-pc-latency/)).
- **What the app should do (inference):**
  - Treat ETW capture as low risk.
  - If capture fails or returns no frames for a game, say "Couldn't measure this game" and stop. Don't retry in other ways.
  - Under ADR 0005, a game with a known objection gets a warning, never a block.

### Overlay methods

| Method | Touches the game? | Examples | Anti-cheat evidence | Effect on the measurement |
|---|---|---|---|---|
| **In-process hook** (DXGI `Present` hook, DLL injection, proxy `d3d9.dll`/`dxgi.dll`) | **Yes** | RTSS; CapFrameX "in-game (Experimental)", which "injects into a game" ([README](https://github.com/CXWorld/CapFrameX)); the FrameView legacy overlay | BattlEye blocks injected graphics mods such as ReShade (earlier note, row 14). Riot blocks memory access by unknown third-party apps from 6 Oct 2026 (row 13). "FrameView overlay is not available with Battlefield 6 anticheat" ([FrameView release notes](https://www.nvidia.com/en-eu/geforce/technologies/frameview/release-notes/)) | Adds work inside the game's frame |
| **Vulkan implicit layer** | **Yes.** The Vulkan loader loads our DLL into the game | CapFrameX `VK_LAYER_CAPFRAMEX_overlay`, registered in HKLM ([AGENTS.md](https://github.com/CXWorld/CapFrameX/blob/master/AGENTS.md), [CLAUDE.md](https://github.com/CXWorld/CapFrameX/blob/master/CLAUDE.md)) | It's code inside the game process, so treat it like injection (inference) | Same as above |
| **Separate topmost window** | No | CapFrameX "hook-free": "Built-in overlay without injecting into the game" ([README](https://github.com/CXWorld/CapFrameX)). FrameView 2.0's overlay "renders as an independent window rather than drawing into the application's back buffer" ([release notes](https://www.nvidia.com/en-eu/geforce/technologies/frameview/release-notes/)) | No anti-cheat objection found | **Can change what it measures.** Microsoft: when other content sits on top, DWM can "transition back to composed mode", "reverse compose", or "leverage MPO to maintain the independent flip mode" ([DXGI flip model](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/direct3ddxgi/for-best-performance--use-dxgi-flip-model.md)). NVIDIA: without MPO the overlay "may force the game out of iFlip and increase presentation latency" ([release notes](https://www.nvidia.com/en-eu/geforce/technologies/frameview/release-notes/)). MPO is hardware composition of planes ([MPO support](https://github.com/MicrosoftDocs/windows-driver-docs/blob/staging/windows-driver-docs-pr/display/multiplane-overlay-support.md)) |
| **uiAccess window in a higher Z-band** | No | Intel PresentMon capture app (`CreateWindowInBand`, `uiAccess=true`) ([capture README](https://github.com/GameTechDev/PresentMon/blob/main/README-CaptureApplication.md)) | No objection found | Same flip caveat. **Not open to us:** uiAccess needs the exe to be "cryptographically signed" and run "from a trusted location (such as Program Files)" (same README). Our builds are unsigned (ADR 0003) and Velopack installs per user (inference) |
| **No overlay; results in the app window** | No | — | None | No effect on the game |

**Recommendation (inference):**
- **v1 has no in-game overlay.** Results appear in the app window after each session, with a live readout in the window for players with a second screen.
- If an overlay is added later, use a separate click-through window. Keep it off by default and never inject.
- Log `PresentMode` every frame, so a session where the overlay (or anything else) knocked the game into `Composed: Flip` is never compared with an independent-flip session.

## 7. Practical cautions

- **ETW session limits and clashes:**
  - 64 sessions system-wide ([docs](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/event-tracing-sessions.md)).
  - One NT Kernel Logger ([docs](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/configuring-and-starting-the-nt-kernel-logger-session.md)).
  - PresentMon's default session name is shared ([console README](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)).
  - Use our own names. If a session fails to start, say which tool may be holding it.
- **Crash cleanup:** CapFrameX notes that PresentMon "exits within milliseconds when it cannot open its ETW session". It cleans up a session orphaned by a crash with `--stop_existing_session` at the next start ([PresentMonCaptureService.cs](https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.PresentMonInterface/PresentMonCaptureService.cs)). We should do the same with our own session name, when the service starts.
- **Measuring changes the thing measured.**
  - Don't run DPC tracing during game capture.
  - Don't show an overlay during measured sessions.
  - Keep our sensor polling the same in both "before" and "after" (inference).
- **Things that confound a comparison and must be tagged:** game updates, GPU driver updates, Windows quality updates, a tuning profile, a different monitor or refresh rate, and frame generation on or off. The app already sees most of these through drift checks and driver checks (inference).

## Gaps

- **Anti-cheat statements on ETW and overlays:**
  - I found no first-party EAC, BattlEye, Vanguard or FACEIT text on ETW capture, PresentMon, FrameView or separate-window overlays.
  - CapFrameX doesn't name the anti-cheat that "blocked PresentMon".
  - The search budget ran out before EA and FACEIT forum threads could be read.
- **LatencyMon's method:** Resplendence's page was not opened (budget). The DPC/ISR method above comes from Microsoft's ETW docs, not from LatencyMon.
- **Boot event fields:** Event 100's field names come from search results (community, not opened). Microsoft's assessment pages on "main path boot" were found but not opened. Check a real event on Windows 11 before building this.
- **Academic sources:** the Georges et al. and Kalibera & Jones papers are cited by title only. The sample-size table is my own arithmetic, built on assumed spreads. Measure real spreads on test PCs before fixing thresholds.
- **Game frame-time studies:** I found no academic study of how much *normal play* varies from session to session. The Claypool group's work on frame-time variation and player experience was not reached.
- **Not covered:**
  - **NVIDIA App capture:** no public description of how it captures.
  - **FrameView SDK:** no licence found.
  - **AMD and Intel overlays:** AMD Adrenalin's metrics overlay and Intel's own overlay were not researched.
- **PresentMon:**
  - **Release year:** the release page shows "21 Sep" with no year; 2026 is inference.
  - **Signing:** whether the official console exe is Authenticode-signed was not checked.

## Sources

**PresentMon (Intel, GameTechDev)**
- README (components, Performance Log Users, admin): https://github.com/GameTechDev/PresentMon
- LICENSE (MIT, © 2017–2024 Intel): https://github.com/GameTechDev/PresentMon/blob/main/LICENSE.txt
- Console app options and CSV columns: https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md
- Service, SDK, PresentMonAPI2.dll rule: https://github.com/GameTechDev/PresentMon/blob/main/README-Service.md
- Capture app, metric table, uiAccess overlay: https://github.com/GameTechDev/PresentMon/blob/main/README-CaptureApplication.md
- Building the standalone console: https://github.com/GameTechDev/PresentMon/blob/main/BUILDING.md
- ETW providers used: https://github.com/GameTechDev/PresentMon/blob/main/PresentData/PresentMonTraceSession.cpp
- Releases (v2.6.0 and earlier notes): https://github.com/GameTechDev/PresentMon/releases

**CapFrameX**
- README (v1.9.1, PresentMon 2.6.0, overlays, run history, conflicts): https://github.com/CXWorld/CapFrameX
- LICENSE (MIT): https://github.com/CXWorld/CapFrameX/blob/master/LICENSE
- Architecture notes (ETW needs admin, hook-free OSD, hook ladder): https://github.com/CXWorld/CapFrameX/blob/master/CLAUDE.md and https://github.com/CXWorld/CapFrameX/blob/master/AGENTS.md
- PresentMon arguments: https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.PresentMonInterface/PresentMonServiceConfiguration.cs
- Starting PresentMon elevated, session cleanup: https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.PresentMonInterface/PresentMonCaptureService.cs
- Metric code (average, P1, low average, integral, stutter): https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.Statistics.NetStandard/FrametimeStatisticProvider.cs
- Metric list: https://github.com/CXWorld/CapFrameX/blob/master/source/CapFrameX.Statistics.NetStandard/Contracts/EMetric.cs
- Metric definitions blog (2020, updated 2022): https://www.capframex.com/blog/post/Explanation%20of%20different%20performance%20metrics

**NVIDIA**
- FrameView user guide (the 1-8 PDF, which describes itself as 1.7): https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-8-user-guide-web-version.pdf
- FrameView 2.0 release notes (overlay window, MPO, iFlip, Battlefield 6): https://www.nvidia.com/en-eu/geforce/technologies/frameview/release-notes/
- Understanding and Measuring PC Latency (5 May 2023): https://developer.nvidia.com/blog/understanding-and-measuring-pc-latency/
- NVIDIA App page (overlay metrics): https://www.nvidia.com/en-us/software/nvidia-app/
- NVIDIA App statistics overlay 11.0.9 **(community)**: https://www.guru3d.com/story/nvidia-statistics-overlay-adds-cpu-temperature-power-and-clock-monitoring/

**Microsoft**
- Who can control ETW sessions: https://learn.microsoft.com/windows/desktop/etw/controlling-event-tracing-sessions
- 64-session limit: https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/event-tracing-sessions.md
- NT Kernel Logger (only one): https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/configuring-and-starting-the-nt-kernel-logger-session.md
- SystemTraceProvider (up to 8 sessions): https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/configuring-and-starting-a-systemtraceprovider-session.md
- System providers (DPC and interrupt keywords): https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/system-providers.md
- DPC event: https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/dpc.md
- ISR event: https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ETW/isr.md
- DXGI flip model, independent flip, MPO: https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/direct3ddxgi/for-best-performance--use-dxgi-flip-model.md
- Multiplane overlay support: https://github.com/MicrosoftDocs/windows-driver-docs/blob/staging/windows-driver-docs-pr/display/multiplane-overlay-support.md
- GetSystemTimes: https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/processthreadsapi/nf-processthreadsapi-getsystemtimes.md
- PERFORMANCE_INFORMATION: https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/psapi/ns-psapi-performance_information.md
- IcmpSendEcho2: https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/icmpapi/nf-icmpapi-icmpsendecho2.md
- GetPerTcpConnectionEStats: https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/iphlpapi/nf-iphlpapi-getpertcpconnectionestats.md
- Event 100 discussion **(community, not opened)**: https://learn.microsoft.com/en-us/answers/questions/2422685/diagnostics-perfomance-event-id-100-slow-boot
- Main Path Boot Duration **(not opened)**: https://learn.microsoft.com/en-us/windows-hardware/test/assessments/main-path-boot-duration-and-main-path-resume-duration
- Post On/Off Duration **(not opened)**: https://learn.microsoft.com/en-us/windows-hardware/test/assessments/post-on-off-duration

**TraceEvent (Microsoft PerfView)**
- LICENSE (MIT, .NET Foundation): https://github.com/microsoft/perfview/blob/main/LICENSE.TXT
- Kernel parser (DPC/ISR events, ElapsedTimeMSec): https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Parsers/KernelTraceEventParser.cs

**Method and statistics**
- Gamers Nexus, "Bench Theory: How Reliable Are Benchmarks?" (16 Feb 2018): https://gamersnexus.net/guides/3240-bench-theory-reliability-standard-deviation-game-benchmarks
- Georges, Buytaert & Eeckhout, OOPSLA 2007 **(title only, not opened)**: https://dri.es/files/oopsla07-georges.pdf
- Kalibera & Jones, effect-size confidence intervals **(title only, not opened)**: https://ar5iv.labs.arxiv.org/html/2007.10899

**Anti-cheat (read 2026-10-15 for `research/anti-cheat-requirements.md` on branch `claude/project-thread-5ggkan`, not re-opened)**
- BattlEye FAQ (Ubisoft): https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq
- BattlEye and injected DLLs (Rockstar): https://support.rockstargames.com/articles/2qX3EkMbz2ahbzhgJXSZ29/failed-to-initialize-battleye-service-driver-load-error-1072
- Riot, game memory access removed for third-party apps: https://support.riotgames.com/ar-ae/riot/performance/game-memory-access-removed-for-third-party-apps

**Community**
- valleyofdoom TimerResolution / MeasureSleep: https://github.com/valleyofdoom/TimerResolution

**This repo**
- `src/Elynt.Windows/WindowsProcesses.cs` (game start/stop via WMI process traces backed by ETW)
- `src/Elynt.Service/ServicePaths.cs` (folders users can't write, for verified files)
- `docs/adr/0001-elevated-service-makes-every-change.md`, `docs/adr/0003-unsigned-builds-from-a-public-releases-repo.md`, `docs/adr/0005-anti-cheat-conflicts-warn-never-block.md`
