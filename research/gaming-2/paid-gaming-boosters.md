# Survey paid gaming boosters

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/86

Researched 2026-10-30 by reading vendor product pages, manuals, support answers, press releases and independent tests on the web (WebSearch and WebFetch). No booster was run: this session has no Windows PC. Anything from a secondary or community source is marked **(community)**. My own reasoning is marked **(inference)**. Vendor pages are first-party sources for what a vendor *says*, not proof that the claim is true.

## Answer

- **They all do roughly the same five things.** They close or "sleep" background apps and services while a game runs. They "clean" RAM. They switch the power plan or stop CPU sleep states. They raise the game's priority or pin it to cores. Many also bundle junk, registry and driver tools that have nothing to do with frame rate. A few go further: Hone applies dozens of registry, network and driver tweaks and can lock an NVIDIA GPU at its top clock. Razer Booster Prime and Hone rewrite in-game settings. Smart Game Booster sells a one-click GPU overclock.
- **On a normal gaming PC the frame-rate gain is about zero.** Independent tests found results within run-to-run noise for five boosters, +1–2 fps for IObit's old Game Booster, and a median of +2.4 fps across seven apps in 2026, with some apps a little *slower* than doing nothing **(community)**. Gains only showed up when the PC was already starved, for example 8 GB of RAM with 47 Chrome tabs open **(community)**. No vendor publishes a test anyone can repeat.
- **The big numbers do not hold up.** Examples: IObit's "up to 200% quicker PC" and "up to 300% Internet speedup", CCleaner's "up to an 11% boost in FPS", and Norton's "up to 30%" FPS (measured *while downloading*). Each comes from the vendor's own or vendor-promoted testing, with no method anyone can repeat.
- **Known snake oil is still sold as a feature.** RAM cleaning (Cortex, IObit), registry cleaning and registry "defrag" (IObit, AVG, Ashampoo, CCleaner) and blanket service disabling are all still sold. Bitsum, who make Process Lasso (one of the boosters), say RAM cleaners lower performance. Microsoft does not support registry cleaners.
- **What they do right, and we should match:** they switch on by themselves when a game starts and put everything back when it closes. They work per game. Booster Prime shows which settings it changed and has a Revert button. IObit's Rescue Center undoes registry and startup changes. Process Lasso makes no FPS promises and warns users off its own riskier features.
- **What they do wrong, and we can do better:**
  - claims with no evidence;
  - "health" scores and issue counts that push a purchase;
  - undo that is only a System Restore point (Hone);
  - global settings that break some games (Cortex's "Enable CPU Core");
  - power-plan forcing that can break AMD X3D core parking;
  - risky hardware changes with no safety net (GPU clock lock, one-click overclock, forced MSI mode);
  - subscriptions that hold back "optimizations" behind a paywall.
- **What they leave out, and gamers need:** a fair before/after measurement (frame times and 1% lows, booster on vs off). Checks for the losses that really cost frames: XMP/EXPO off, monitor left at 60 Hz, laptop game on the iGPU, thermal or power throttling, Memory Integrity on. Per-change evidence and tiers. Exact per-change undo. Drift detection. Anti-cheat warnings. CPU-aware advice (X3D, Intel hybrid).
- **For us:** v1's per-game profile already does the honest core of a booster, with real undo. The biggest wins over the paid products are area **C** (measure the difference) and area **D** (hidden-loss checks). Never sell a RAM or registry cleaner as a "boost". Show no score.

## What a "game booster" does, mechanism by mechanism

Every product below is a mix of these mechanisms. The verdict here is for frame rate and smoothness, not for disk space or privacy.

| Mechanism | Who does it | What primary sources say | Verdict | Area |
|---|---|---|---|---|
| Close or suspend background apps and services during play | Cortex, IObit Turbo Boost, Wise, Smart Game Booster, CCleaner Sleep, AVG Sleep Mode, Ashampoo Profile Booster | Helps only when something heavy is running. Windows' own Game Mode already "temporarily reallocates the resources" of background processes such as Windows Update and silences their notifications, and turns itself on for most games ([Microsoft Learn, updated 2026-10-28](https://learn.microsoft.com/en-us/training/modules/optimize-windows-gaming/4-performance-gaming-features)) | Situational | A |
| Free or "clean" RAM | Cortex ("clean RAM"), IObit (Clean RAM, Auto RAM Clean, Smart RAM), Smart Game Booster | Bitsum: "Most operate on the incorrect premise that you want more 'free' RAM", and repeated trimming "will only serve to reduce performance" ([Bitsum](https://bitsum.com/?p=231589)). Russinovich called memory optimizers a hoax as far back as 2004 ([ITPro Today](https://itprotoday.com/cloud-computing/memory-optimization-hoax), not re-read) | Possible placebo at best; can hurt | A |
| Switch power plan, stop CPU sleep, unpark cores | Cortex ("Disable CPU sleep mode", "game power solutions" plan), Process Lasso Performance Mode, Hone ("Optimize Windows Power Plan", "Disable Power Saving Features") | Our own catalogue rates power plans as situational. On AMD dual-CCD X3D CPUs, core parking needs the Balanced plan and Game Mode, so forcing a high-performance plan defeats it (repo note `gaming-tweak-catalogue.md`, items 5, 8 and 10) | Situational; harmful on X3D | A |
| Raise game priority or pin it to cores | Cortex ("Enable CPU Core"), Norton Game Optimizer, Ashampoo, Process Lasso, IObit | Norton moves non-game apps onto one core ([Norton press release](https://www.scoop.co.nz/stories/BU2109/S00276.htm)). Razer support told a user whose game fell to 1–5 fps to turn off "Enable CPU Core", which fixed it ([Razer Insider](https://insider.razer.com/razer-cortex-39/razer-cortex-boost-tanks-frame-rate-52359)) **(community)** | Situational; can hurt | A |
| Junk clean, registry clean or "defrag", disk defrag | IObit, AVG TuneUp, CCleaner, Ashampoo, Wise Care 365, Cortex System Booster | Microsoft does not support registry cleaners ([Microsoft KB2563254](https://support.microsoft.com/en-us/topic/2563254), not re-read). No source ties any of these to frame rate | No FPS effect | other |
| Driver updater | IObit (Driver Booster), AVG Driver Updater, CCleaner Pro, Smart Game Booster | Out of scope here; the GPU-driver agent covers drivers. Third-party driver sources add risk **(inference)** | Not a booster gain | B |
| TCP/IP and network "boost" | IObit Internet Booster ("up to 300%"), Hone Network optimizations | Our catalogue rates Nagle/TCPNoDelay, NetworkThrottlingIndex and NIC interrupt moderation as possible placebo. Hone itself warns its network changes "may increase latency or lower speeds" ([Hone support](https://support.hone.gg/en/recommended-optimizations)) | Possible placebo | E |
| Rewrite in-game settings | Razer Booster Prime, Hone presets | Booster Prime sets a game's settings file to a target FPS, shows changes and can revert ([Razer](https://www.razer.com/cortex/booster-prime)) | Real but ordinary: it is a settings preset | other |
| GPU clock changes | Hone ("Lock NVIDIA GPU to max boost clock"), Smart Game Booster (one-click overclock) | Hone warns the clock lock "can run very hot on laptops" ([Hone support](https://support.hone.gg/en/recommended-optimizations)) | Risky; not a booster job | B |
| FPS counter or hardware monitor | Cortex, Smart Game Booster, IObit Performance Monitor, Process Lasso responsiveness graph | Shows numbers but never compares booster on vs off | Useful but incomplete | C |

## Product by product

### Razer Cortex (free, by Razer)

- **What it is:** Razer's free game launcher, booster and deals app for Windows 10 and 11. No paid tier is shown on its pages. TechRadar's 2022 review calls it free, with no ads or premium upsell ([TechRadar](https://www.techradar.com/reviews/razer-cortex)) **(community)**. It belongs in this survey because it is the best-known booster and the paid ones offer the same switches **(inference)**.
- **What it changes:**
  - Game Booster "stops non-essential processes and apps". It frees RAM, disables CPU sleep mode and "enables the CPU core to prioritize gaming". It turns on when a game launches and "restores your PC to its previous state" afterwards ([Razer Game Booster](https://www.razer.com/cortex/booster)).
  - A 2023 user report lists the actual boost switches: Enable CPU Core, Disable CPU sleep mode, Enable game power solutions, clear clipboard, clean RAM, Windows key, Sticky Keys, disable Cortana, disable background apps, disable telemetry and disable problem reports ([Razer Insider](https://insider.razer.com/razer-cortex-39/razer-cortex-boost-tanks-frame-rate-52359)) **(community)**.
  - "Enable game power solutions" installs and switches to a Razer Cortex power plan ([Razer Insider, staff reply 2023-08-28](https://insider.razer.com/razer-cortex-39/razer-cortex-power-plan-boost-default-settings-50206)) **(community)**.
  - System Booster deletes junk files ([Razer Boost](https://www.razer.com/ca-en/cortex/boost)). TechRadar adds that it defragments game files, disables unused services and tunes disk caches **(community)**.
  - Booster Prime uses "advanced AIs" to pick in-game settings for a few titles (Valorant, Fortnite, PUBG, Apex, LoL, CoD MW, Cyberpunk). It predicts FPS, lets you set a target FPS, highlights changed settings in green and has a Revert button ([Razer Booster Prime](https://www.razer.com/cortex/booster-prime); [Razer support](https://mysupport.razer.com/app/answers/detail/a_id/3854)).
- **What it claims:** it "increases your FPS by micro-managing your Windows OS and non-essential applications", with no number given ([Razer](https://www.razer.com/cortex/booster)).
- **Does it hold up?**
  - ghacks measured Cortex against doing nothing. 3DMark and Resident Evil 6 results were within 1% (Fire Strike 3072 vs 3074). Star Swarm moved from 22.65 to 24.69 fps ([ghacks](https://www.ghacks.net/2015/05/23/do-game-boosters-improve-pc-gaming-performance/)) **(community)**. Star Swarm also ranged from 20.39 to 24.69 fps across the other boosters, so a 2 fps swing there looks like noise **(inference)**.
  - A 2026 test found +2 to +4 fps only in CPU-limited 240 fps Valorant lobbies, and nothing at GPU-bound 1440p ([BetterFPS](https://betterfps.com/blog/do-fps-boosters-actually-work-tested)) **(community; the site sells a settings service, so it has a motive to run boosters down)**.
  - The "Enable CPU Core" setting dropped one game to 1–5 fps until it was turned off. It is one switch for all games, so the user had to lose it everywhere and asked Razer for per-game profiles ([Razer Insider](https://insider.razer.com/razer-cortex-39/razer-cortex-boost-tanks-frame-rate-52359)) **(community)**.
  - Verdict: small gains in CPU-bound cases, occasional harm, and the restore-on-exit design is good.

### IObit Advanced SystemCare Pro (paid) and IObit's game boosters

- **What it is:** an all-in-one "PC care" suite. Pro is listed at "$14.77 (was $29.99)", 1 year, 1 PC ([IObit compare page](https://www.iobit.com/en/compare/asc/)). Version 19 shipped in October 2025 ([BetaNews](https://betanews.com/2025/10/14/iobit-advanced-systemcare-19-improves-windows-performance-and-security-heres-whats-new/)) **(community)**. IObit's old standalone Game Booster and Game Assistant are gone. The game features now live inside Advanced SystemCare as Turbo Boost with a "Game Mode" **(inference from the manual and the ghacks note that Game Assistant is "no longer available")**.
- **What it changes** (from IObit's own manual: [ASC manual](https://www.iobit.com/product-manuals/asc-help/)):
  - Turbo Boost has Work, Game and Economy modes. It switches off background items that you pick under Configure; the manual does not list them.
  - Clean RAM releases memory. Auto RAM Clean and Smart RAM are Pro-only.
  - Registry Clean and Registry Defrag "remove registry junk" and compact the registry.
  - Disk Optimization defragments hard drives and trims SSDs.
  - Internet Booster (Pro) "optimizes your TCP/IP, IE, Microsoft Edge, Firefox, and Opera settings".
  - Driver updates go through Driver Booster. Startup optimization disables or delays items.
  - **Rescue Center** can undo registry fixes, system optimization, Internet Boost, Registry Defrag and startup changes, and manages System Restore points.
- **What it claims:** "Up to 200% quicker PC", "Up to 300% Internet speedup with Internet Booster" and "Auto RAM clean to stop unused programs and processes" ([IObit compare page](https://www.iobit.com/en/compare/asc/)). IObit's 2026 gaming blog says Turbo Boost makes games run "closer to peak performance", with no figure ([IObit blog, 2026-04-27](https://www.iobit.com/en-blog/how-to-fix-lag-in-games.php)).
- **Does it hold up?**
  - No method is given for 200% or 300%.
  - PC Games Hardware tested IObit's Game Booster in 2009: about 52 MiB freed, +1–2 fps in two games and +3% in 3DMark ([PCGH](https://www.pcgameshardware.de/Tools-Software-156186/Tests/Game-Booster-Nutzlose-Software-681891/)) **(community)**.
  - IObit Game Assistant in ghacks' test scored within 1% of default, and *lower* in Star Swarm (20.39 vs 22.65 fps) ([ghacks](https://www.ghacks.net/2015/05/23/do-game-boosters-improve-pc-gaming-performance/)) **(community)**.
  - RAM and registry cleaning have no supported benefit (see "Snake oil" below).
  - Verdict: the claims do not hold. The Rescue Center undo is the one thing worth copying.

### Hone (free tier plus Hone Premium subscription)

- **What it is:** a gaming optimizer from AuraSide Inc., also listed on Overwolf ([Overwolf](https://overwolf.com/app/AuraSide_Inc-Hone)).
  - Premium is billed monthly or yearly, renews automatically, and its price varies by region.
  - Free gets "10 optimizations (up to 15 with referrals)", the balanced game mode and ads.
  - Premium unlocks "unlimited optimizations", "Premium-exclusive optimizations", custom and performance game modes, and "Premium game presets" ([Hone Premium](https://hone.gg/es/premium)).
- **What it changes** (named on Hone's own support page: [Hone recommended optimizations](https://support.hone.gg/en/recommended-optimizations)):
  - Windows timer resolution, power plan and "Disable Power Saving Features".
  - "Optimize Message Signal Interrupts" (MSI mode), "Optimize Device Affinities", "Optimize Memory Management" and "Optimize I/O Operations".
  - "Disable Windows Services", "Disable Xbox", DiagTrack, transparency and visual effects.
  - Browser hardware acceleration off, and OneDrive off.
  - NVIDIA, AMD and Intel GPU optimizations, plus "Lock NVIDIA GPU to max boost clock".
  - Network: bandwidth limits, congestion provider, netsh, TCP, adapter settings and DSCP.
  - Optionally installing a Samsung NVMe driver, and "Hone recommended" BIOS options.
- **How you undo it:** Hone's guide says to create a restore point first and to roll back to it. Network changes are undone by "reset your Internet settings or restore the restore point" ([Hone support](https://support.hone.gg/en/recommended-optimizations)).
- **What it claims:**
  - The Premium page promises "FPS increase" and "reduced and stable latency" with no number ([Hone Premium](https://hone.gg/es/premium)).
  - The Premium FAQ says there is no fixed result for every PC ([Hone Premium](https://hone.gg/es/premium)). Its support page says it does not guarantee any FPS gain ([Hone support](https://support.hone.gg/en/recommended-optimizations)).
  - The home page has carried a testimonial of "up to 45% FPS" (repo note `gaming-tweak-catalogue.md`, item 22).
- **Does it hold up?**
  - Most of what Hone applies is already rated in our catalogue. Power plan and timer resolution are situational. MSI mode, Nagle, NetworkThrottlingIndex, interrupt moderation, MMCSS, service disabling and visual effects are possible placebo. Forced MSI mode is high risk and can stop boot.
  - If "Disable Xbox" turns off or removes Game Bar, it breaks X3D core parking, which needs Game Bar (repo note `gaming-tweak-catalogue.md`, item 19) **(inference: Hone does not say what the switch changes)**.
  - The free/premium split sorts tweaks by price, not by evidence **(inference)**.
  - Verdict: a big bundle of mostly unproven tweaks, with a restore point as the only undo.

### Ashampoo WinOptimizer (paid, lifetime licence per version)

- **What it is:** a German tune-up suite. The current page starts at $14, with a 30-day trial and a 30-day refund ([Ashampoo WinOptimizer](https://www.ashampoo.com/en-us/winoptimizer-27/detail)). A "Game Booster" module was added in version 11 (2014) ([BetaNews](https://betanews.com/2014/04/03/ashampoo-winoptimizer-11-adds-game-booster-user-rights-manager)) **(community)**.
- **What it changes:**
  - Today the gaming feature is **Profile Booster**. It "activates only those processes you really need", with presets for games, office work or a chosen program.
  - It can also give a program "the highest priority on your processor's cores".
  - Cleaners, registry tools and a **Backup Manager** ("Save and restore all changes") complete the suite ([Ashampoo](https://www.ashampoo.com/en-us/winoptimizer-27/detail)).
- **What it claims:** "up to 50% faster" for the cleaner modules. There is no gaming number.
- **Does it hold up?** No independent gaming test was found. Closing processes and raising priority are the same mechanisms as the boosters above, so expect the same near-zero effect on a clean PC **(inference)**.

### CCleaner Professional: Performance Optimizer (paid; launched under Gen Digital in 2022)

- **What it is:** a "Sleep mode" feature that is only in the paid editions. Professional is $44.95 a year and Pro Plus (3 PCs) is $64.95. Both renew at the undiscounted price ([CCleaner Performance Optimizer](https://ccleaner.com/ccleaner/performance-optimizer-pro)).
- **What it changes:**
  - It puts "programs the user isn't working with" to sleep, covering their "start-up items, background processes, and services", and wakes them when needed ([Gen Digital press release, 2022-05-12](https://newsroom.gendigital.com/2022-05-12-ccleaner-unveils-6-0-release-with-new-patented-performance-optimizer)).
  - ghacks reported that sleeping disables "all startup items, background tasks, scheduled tasks, and services" of the app. Readers reported Office apps staying stopped and a service that runs all the time ([ghacks](https://www.ghacks.net/?p=178552)) **(community)**.
- **What it claims:** "up to 34% more speed + 30% more battery life", "72% faster startup speed", and "up to an 11% boost in FPS and up to a 19% reduction in lag". The tests were done in October 2021 with the Windows ADK on **three computers** ([CCleaner](https://ccleaner.com/ccleaner/performance-optimizer-pro)). The press release says the 34% came from "an older PC from 2015" in "internal tests" ([Gen Digital](https://newsroom.gendigital.com/2022-05-12-ccleaner-unveils-6-0-release-with-new-patented-performance-optimizer)).
- **Does it hold up?** Three internal machines with no published method is not evidence for a modern gaming PC **(inference)**. The idea of pausing an app's background parts until you open it is reasonable for a laptop's battery. Nothing shows it raises FPS beyond closing those apps yourself **(inference)**.

### AVG TuneUp (paid, Gen Digital; Avast Cleanup is its sibling)

- **What it is:** a yearly subscription tune-up suite. A reseller lists it from $29.99 per year per device ([TrustRadius](https://www.trustradius.com/products/avg-tuneup-for-pc)) **(community)**.
- **What it changes:**
  - **Sleep Mode** ("Programs-On-Demand") cuts a program's impact "to nearly zero".
  - **Turbo Mode** turns off "more than 70" Windows features, such as Media Player sharing, pen input and remote assistance, either always or on demand ([AVG guide](https://avg.com/en-ww/avg-pctuneup-guide-pc-speed)).
  - It also has registry and disk cleaners and a separate Driver Updater product.
- **What it claims:** faster PC. No gaming figure was found on the page that was read.
- **Does it hold up?** Turning off pen input or media sharing does not touch frame rate on a gaming desktop **(inference)**. The changes are broad, and the user cannot judge them one by one against evidence **(inference)**.

### Wise Game Booster (free) and Wise Care 365 Pro (paid)

- **What it is:** WiseCleaner's free booster, still updated in 2026 (version 1.5x per download sites, e.g. [TechSpot](https://www.techspot.com/downloads/6156-wise-game-booster.html)) **(community)**. The paid Wise Care 365 Pro adds cleaning and registry tools.
- **What it changes:** it ends chosen processes and services, applies "system optimization" registry items, and restores them afterwards ([TheWindowsClub](https://www.thewindowsclub.com/optimize-gameplay-wise-game-booster)) **(community; WiseCleaner's own page could not be opened this session)**.
- **Does it hold up?**
  - In ghacks' test Wise was within 1% of default in 3DMark, and up slightly in Star Swarm ([ghacks](https://www.ghacks.net/2015/05/23/do-game-boosters-improve-pc-gaming-performance/)) **(community)**.
  - The 2026 test found "WiseCleaner and smaller apps" 1–2 fps *worse* than doing nothing ([BetterFPS](https://betterfps.com/blog/do-fps-boosters-actually-work-tested)) **(community)**.

### Smart Game Booster PRO (paid add-on, on Steam)

- **What it is:** a booster sold on Steam. The PRO add-on is $19.99 as a lifetime licence and needs the free base app. Reviews are "Mostly Positive", 74% of 62 ([Steam](https://store.steampowered.com/app/1289950)).
- **What it changes:**
  - It "ends unnecessary processes and services with one click".
  - It updates graphics drivers, cleans disk space and "defragments games".
  - It overclocks "most Nvidia and AMD graphics cards with one click while keeping them at a safe temperature".
  - It shows CPU and GPU temperatures ([Steam](https://store.steampowered.com/app/1289950)).
- **What it claims:** "Normally, you will get higher than 60 FPS in your games at a safe temperature." No hardware or method is given.
- **Does it hold up?**
  - The 60 FPS line cannot be true for every PC and game **(inference)**.
  - A one-click overclock with no stability test is a crash and data-loss risk. v1's own GPU tuning uses confirm-or-revert for exactly this reason (GLOSSARY: Confirm-or-revert) **(inference)**.
  - A 2026 test found it "nearly identical" to Game Fire, with the median gain across apps at 2.4 fps ([BetterFPS](https://betterfps.com/blog/do-fps-boosters-actually-work-tested)) **(community)**.

### Norton 360 for Gamers: Game Optimizer (paid, Gen Digital)

- **What it is:** a feature of Norton's gamer antivirus subscription, announced in June 2021 ([Norton press release via Nasdaq, 2021-06-08](https://www.nasdaq.com/press-release/norton-levels-up-performance-for-pc-gamers-2021-06-08), not re-read).
- **What it changes:** it "isolates unessential apps to a single central processing unit core" so the other cores run the game. It turns on when it detects a game ([Norton press release, 2021-09-13](https://www.scoop.co.nz/stories/BU2109/S00276.htm)).
- **What it claims:** "increases FPS up to 30% while downloading and playing games simultaneously". A PassMark comparison ranked it first against five unnamed rivals "with and without a variety of different background tasks" ([same](https://www.scoop.co.nz/stories/BU2109/S00276.htm)).
- **Does it hold up?** The 30% was measured with a download running in the background, the case where any booster looks best. The rivals and hardware are not named in the release, and Norton's blog post now redirects ([NortonLifeLock blog](https://www.nortonlifelock.com/blogs/gaming/passmark-ranks-norton-game-optimizer)). Fencing off background work is a real idea. The headline number does not describe a normal gaming session **(inference)**.

### Process Lasso Pro (paid, Bitsum)

- **What it is:** a process manager. The free version is fully usable. "Pro is a one-time payment for a lifetime license" ([Bitsum](https://bitsum.com/)). A 2026 deal listed it at $23.97 (40% off) ([Slickdeals](https://slickdeals.net/f/17971464-process-lasso-pro-40-sale-23-97-lifetime-license)) **(community)**.
- **What it changes** ([Bitsum](https://bitsum.com/); [FAQ](https://bitsum.com/pl-faq/)):
  - **ProBalance** temporarily lowers background hogs to Below Normal priority.
  - **Performance Mode** switches to Bitsum's top-performance power plan while chosen apps run.
  - Persistent per-app priorities, affinities and **CPU Sets**.
  - Power plan per process, IdleSaver, CPU Limiter, Efficiency Mode, per-process SMT off, Instance Balancer, and SmartTrim.
- **What it claims:** no FPS number at all. The FAQ talks about responsiveness and says "using Process Lasso will not cause bans". It suggests setting priorities through the registry for games protected by Easy Anti-Cheat ([FAQ](https://bitsum.com/pl-faq/)).
- **Does it hold up?**
  - Its claims are modest and match its mechanisms.
  - Its own author warns against RAM cleaners ([Bitsum](https://bitsum.com/?p=231589)). He has also said most people should not use its standby-list purge (repo note `gaming-tweak-catalogue.md`, item 20).
  - The one trap is Performance Mode's power plan on X3D CPUs (see the mechanism table).
  - Verdict: the most honest product in this survey.

### Others seen in tests

- **Game Fire** (Smart PC Utilities, free with a paid Pro). It was in both the 2015 and 2026 tests. Results were within 1% of default in 2015, and "nearly identical" to Smart Game Booster in 2026 ([ghacks](https://www.ghacks.net/2015/05/23/do-game-boosters-improve-pc-gaming-performance/); [BetterFPS](https://betterfps.com/blog/do-fps-boosters-actually-work-tested)) **(community)**. Its own site was not read.
- **Avast Cleanup Premium** is Gen Digital's twin of AVG TuneUp. It was not read separately **(inference: same owner, same feature names)**.

### Network "boosters" (ExitLag, GearUP, LagoFast, NoPing and similar)

These are paid route-changing services, not Windows optimizers. Hone now sells against them ([Hone comparison](https://hone.gg/comparison/gearup)). They belong to area **E**, which another agent covers.

## Feature table

Legend: Y = does it; P = Pro or paid only; – = not found in first-party material; ? = unclear.

| Product (2026) | Price model | Close/sleep apps & services | RAM clean | Power plan / CPU sleep | Priority / cores | Registry & junk clean | Driver updater | Network tweaks | In-game settings | GPU clock change | FPS / HW monitor | Auto on game start, restore after | Per-change undo | Evidence shown per change |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Razer Cortex | Free | Y | Y | Y | Y | Y (junk) | – | – | Y (Booster Prime) | – | Y | Y | Revert for Booster Prime only | – |
| IObit Advanced SystemCare | Subscription | Y | Y (auto RAM is P) | ? | Y | Y | Y (Driver Booster) | P | – | – | Y | ? | Y (Rescue Center) | – |
| Hone | Free + subscription | Y (services) | ? ("memory management") | Y | Y (affinities) | – | – (installs Samsung NVMe driver) | P | P (presets) | Y (NVIDIA lock) | – | ? | Restore point only | – (warnings only) |
| Ashampoo WinOptimizer | Licence per version | Y | – | – | Y | Y | – | – | – | – | – | Y (profiles) | Y (Backup Manager) | – |
| CCleaner Pro | Subscription | P (Sleep) | – | – | – | Y | P | – | – | – | – | Wakes app on use | Remove the app from the sleep list | – |
| AVG TuneUp | Subscription | Y (Sleep, Turbo) | – | – | – | Y | Separate product | – | – | – | – | Turbo on demand | ? | – |
| Wise Game Booster / Care 365 | Free / Pro | Y | – | ? | – | Y (Care 365) | – | ? | – | – | – | Y | Restore after session | – |
| Smart Game Booster PRO | Lifetime $19.99 | Y | ? | – | – | Y (disk) | Y | – | – | Y (one-click OC) | Y | ? | ? | – |
| Norton Game Optimizer | Subscription | Fences apps | – | – | Y | – | – | – | – | – | – | Y | – | – |
| Process Lasso Pro | Lifetime | Lowers priority (ProBalance) | SmartTrim (opt-in) | Y | Y (affinity, CPU Sets) | – | – | – | – | – | Responsiveness graph | Y (rules) | Rules removed on uninstall | – |
| **Elynt v1 (for comparison)** | Free | Y (per-game profile) | Standby-list purge, possible placebo section | Situational tweaks | Game priority in profile | Cleanup (no registry cleaner) | Drivers page | Possible placebo section | – | Overclocking page, confirm-or-revert | Live temperatures on Home page | Y (per-game profile) | Y (Undo history) | Y (tier + sources) |

## Claims against evidence

| Product | Claim (verbatim) | Where | Holds up? |
|---|---|---|---|
| IObit ASC Pro | "Up to 200% quicker PC" | [IObit](https://www.iobit.com/en/compare/asc/) | No. No method published; independent booster tests show 0–3% **(community)** |
| IObit ASC Pro | "Up to 300% Internet speedup with Internet Booster" | [IObit](https://www.iobit.com/en/compare/asc/) | No. TCP/browser tweaks cannot raise line speed; our catalogue rates TCP tweaks possible placebo **(inference)** |
| CCleaner Pro | "up to an 11% boost in FPS and up to a 19% reduction in lag" | [CCleaner](https://ccleaner.com/ccleaner/performance-optimizer-pro) | Unproven. Internal, Oct 2021, three PCs, no method |
| Norton 360 for Gamers | "increases FPS up to 30% while downloading and playing games simultaneously" | [Norton PR](https://www.scoop.co.nz/stories/BU2109/S00276.htm) | Only under a heavy background load; vendor-promoted test with rivals unnamed |
| Smart Game Booster PRO | "Normally, you will get higher than 60 FPS in your games" | [Steam](https://store.steampowered.com/app/1289950) | No. Depends on the PC and game **(inference)** |
| Razer Cortex | "increases your FPS by micro-managing your Windows OS" | [Razer](https://www.razer.com/cortex/booster) | Small: within noise to +4 fps when CPU-bound **(community)** |
| Hone | "FPS increase", "reduced and stable latency" | [Hone](https://hone.gg/es/premium) | Unquantified; Hone's own FAQ says no fixed result |
| Ashampoo | "up to 50% faster" (cleaners) | [Ashampoo](https://www.ashampoo.com/en-us/winoptimizer-27/detail) | Not a gaming claim; no method |
| Process Lasso | No FPS claim; "responsiveness" | [Bitsum FAQ](https://bitsum.com/pl-faq/) | Consistent with what it does |

## Independent tests

| Test | When | Products | Result |
|---|---|---|---|
| [ghacks](https://www.ghacks.net/2015/05/23/do-game-boosters-improve-pc-gaming-performance/) **(community)** | 2015 (Windows 10 preview) | Game Fire, IObit Game Assistant, Razer Cortex, ToolWiz, Wise | All within about 1% of default in 3DMark and RE6. Some slightly worse. Only Star Swarm moved, by up to 2 fps either way |
| [PC Games Hardware](https://www.pcgameshardware.de/Tools-Software-156186/Tests/Game-Booster-Nutzlose-Software-681891/) **(community)** | 2009 | IObit Game Booster | +1–2 fps in CoD4 and NFS:MW, +3% 3DMark, 52 MiB RAM freed |
| [BetterFPS](https://betterfps.com/blog/do-fps-boosters-actually-work-tested) **(community; sells a competing service)** | 2026-06-13 | 7 apps incl. Cortex, Game Fire, Smart Game Booster, Wise | Median +2.4 fps (error ±1.8). Cortex +2–4 fps CPU-bound only. Wise 1–2 fps worse. Double digits only on a starved 8 GB PC |
| [TechRadar review](https://www.techradar.com/reviews/razer-cortex) **(community)** | 2022-03-15 | Razer Cortex | "a few more frames per second", no numbers |

GameStar and BGR also published booster round-ups ([GameStar](https://www.gamestar.de/artikel/game-booster-test,3383476.html), [BGR](https://www.bgr.com/2248618/stop-downloading-fps-booster-pc/)), but neither page could be opened this session. No YouTube benchmark could be checked first-hand: transcripts are not available through these tools.

## Snake oil and trust problems

- **RAM cleaners.**
  - Windows keeps RAM full of useful cache on purpose. Forcing memory out makes the app fault it back in.
  - Bitsum: "RAM should be as occupied as possible with commonly accessed virtual memory and file system caches" ([Bitsum, 2017-10-28](https://bitsum.com/?p=231589)).
  - Cortex, IObit and Smart Game Booster still sell RAM cleaning as a boost.
  - v1 keeps standby-list purging in the possible placebo section. That is the right home.
- **Registry cleaners and registry defrag.** Microsoft's support policy says it does not support registry cleaners and is not responsible for problems they cause ([Microsoft KB2563254](https://support.microsoft.com/en-us/topic/2563254), not re-read this session). None of the vendors link a registry cleanup to frame rate.
- **Scare scores and fake scans.**
  - Since 1 March 2018 Microsoft's security products class as unwanted software, and remove, programs that show "alarming or coercive messages or misleading content". Examples include reporting errors "in an exaggerated or alarming manner", suggesting nothing else will fix them, setting a deadline, or demanding payment to fix them. The post names free scanners that push paid "cleaner or optimizer applications" ([Microsoft Security blog, 2018-01-30](https://www.microsoft.com/en-us/security/blog/2018/01/30/protecting-customers-from-being-intimidated-into-making-an-unnecessary-purchase/); current criteria at [Microsoft Learn](https://learn.microsoft.com/en-us/defender-xdr/criteria), not re-read).
  - In 2024 the FTC made Restoro and Reimage pay $26 million for "free" scans that told people their PCs had problems in order to sell repairs ([FTC, 2024-03](https://www.ftc.gov/news-events/news/press-releases/2024/03/tech-support-firms-will-pay-26-million-settle-ftc-charges-they-deceived-consumers-buying-repair), not re-read). That case is about a different company, but it shows where "health scores" lead.
  - Advanced SystemCare's "Health Checker" and AI scan, and CCleaner's list of "problems", sit in that pattern. A CCleaner user complained that its Performance Optimizer reports programs he needs as problems ([CCleaner community](https://community.ccleaner.com/t/performance-optimizer-reports-mandatory-programs-as-problems/80431)) **(community)**.
- **Trust record of the big vendors.**
  - CCleaner 5.33 shipped with a backdoor in 2017, reaching about 2.27 million installs ([The Register](https://www.theregister.com/2017/09/18/tainted_ccleaner_downloads)) **(community)**.
  - Microsoft Defender flagged CCleaner as a potentially unwanted app in 2020 over bundled offers ([TechSpot](https://www.techspot.com/news/86216-windows-defender-flagging-ccleaner-potentially-unwanted-application.html)) **(community)**.
  - The FTC ordered Avast (part of Gen Digital, with AVG and Norton; Gen also published CCleaner's 2022 launch) to pay $16.5 million and stop selling browsing data, finalised June 2024 ([FTC](https://www.ftc.gov/news-events/news/press-releases/2024/06/ftc-finalizes-order-avast-banning-it-selling-or-licensing-web-browsing-data-advertising-requiring-it), not re-read).
  - A free, honest tool with no data sales is itself a selling point **(inference)**.

## What they do RIGHT that we should match

| # | What | Who does it | Elynt today | Area |
|---|---|---|---|---|
| R1 | Switch on by themselves when a game starts and put everything back when it closes | Cortex, Norton, Process Lasso rules, CCleaner wake-on-use | Done: per-game profile, temporary changes | A |
| R2 | Rules per game, not one global boost (Cortex users asked for exactly this after "Enable CPU Core" broke one game) | Process Lasso, Ashampoo profiles; requested in [Razer Insider](https://insider.razer.com/razer-cortex-39/razer-cortex-boost-tanks-frame-rate-52359) **(community)** | Done: per-game profile | A |
| R3 | Lower a background hog's priority instead of killing it | Process Lasso ProBalance | Not offered. Could be a situational per-game option ("lower these apps to Below Normal") **(inference)** | A |
| R4 | CPU Sets or affinity per game, softer than hard pinning | Process Lasso | Not offered. Candidate for situational, mainly for X3D and hybrid CPUs, needs evidence first | A |
| R5 | Show exactly what changed, with one Revert | Booster Prime (green highlights, Revert); IObit Rescue Center; Ashampoo Backup Manager | Done: detail pane and Undo history | A |
| R6 | Honest framing: no fixed FPS promise; warn about own risky features | Process Lasso; Hone's warnings on clock lock, network and BIOS | Done: evidence tiers and risk flags | other |
| R7 | In-game FPS counter | Cortex, Smart Game Booster | Not in v1 | C |
| R8 | Pick in-game settings toward a target FPS, with revert | Razer Booster Prime | Not in v1. Big, game-by-game work; maybe link to vendor tools instead **(inference)** | other |
| R9 | State anti-cheat safety plainly, and route around protected processes (registry priority for EAC games) | Process Lasso FAQ; Booster Prime says it is not a cheat tool | v1 warns on anti-cheat conflicts | E |
| R10 | Pause an app's background parts (startup items, tasks, services) until the user opens it | CCleaner Sleep | Startup & Services page switches items off; a "sleep until opened" idea is untested **(inference)** | other |

## What they do WRONG that we can do better

| # | Problem | Who | Better approach | Area |
|---|---|---|---|---|
| W1 | Headline numbers with no repeatable method ("200%", "300%", "11% FPS", "30%") | IObit, CCleaner, Norton, Smart Game Booster | Never print a gain we did not measure on *this* PC; show tiers and sources | C |
| W2 | RAM cleaning sold as a boost | Cortex, IObit, Smart Game Booster | Keep standby-list purge in possible placebo only; never call it a boost | A |
| W3 | Registry clean and registry defrag | IObit, AVG, CCleaner, Ashampoo, Wise Care | Do not ship a registry cleaner | other |
| W4 | Health scores and problem counts that push a purchase | IObit, CCleaner; FTC Restoro pattern | No score; the Home page shows facts and what is waiting | other |
| W5 | Undo is only a System Restore point | Hone | Per-change undo with the exact old value (v1 has this) | A |
| W6 | One global setting that breaks some games | Cortex "Enable CPU Core" | Per-game, with the game's own off switch | A |
| W7 | Forcing a power plan or removing Xbox/Game Bar, which breaks X3D core parking | Cortex power plan, Process Lasso Performance Mode, Hone | Detect X3D and warn or hide (v1's catalogue already notes this) | A |
| W8 | Risky hardware changes with no safety net: GPU clock lock, one-click overclock, forced MSI mode, a swapped NVMe driver | Hone, Smart Game Booster | Tuning only with confirm-or-revert and vendor warnings; forced MSI kept in possible placebo with "can stop boot" | B |
| W9 | Blanket disabling of services and Windows features (70+ in AVG Turbo Mode) | AVG, Hone, Cortex | Reviewed services list, Advanced switch, one tweak per switch | A |
| W10 | Network tweaks sold as "Internet speedup" | IObit, Hone | Leave TCP tweaks in possible placebo; measure ping and jitter instead | E |
| W11 | Tweaks sorted by price (free 10, Premium unlimited), not evidence | Hone | No paywall; sort by evidence tier | other |
| W12 | Third-party driver updaters | IObit, AVG, CCleaner, Smart Game Booster | Drivers from the GPU vendor or Windows Update (Drivers page) | B |
| W13 | Bundles, data sales, supply-chain history | Gen Digital brands, CCleaner | Free, no bundles, no telemetry sale | other |

## What they are MISSING that gamers need

| # | Gap | Why it matters | Area |
|---|---|---|---|
| M1 | A fair before/after test: same scene, booster on vs off, frame times and 1% lows, several runs | Every independent test above had to do this by hand; no product does it, so users cannot tell placebo from gain | C |
| M2 | Checks for hidden losses that cost far more than a booster gains: XMP/EXPO off, monitor at 60 Hz or wrong cable, laptop game on the iGPU, thermal or power throttling, a reduced PCIe link, Memory Integrity on | No booster looks for these. Our catalogue puts Memory Integrity off at about 4–6% average FPS in CPU-bound games, more than any independent booster test found **(inference from repo note `gaming-tweak-catalogue.md`, item 1)** | D |
| M3 | Evidence and tier on every change | Users cannot judge what Hone's "Optimize Memory Management" or AVG's 70 features do | A |
| M4 | Exact per-change undo and drift detection | Restore points expire and do not track what a Windows update put back **(inference)** | A |
| M5 | CPU-aware advice (AMD X3D parking, Intel hybrid P/E cores) | Global power-plan and core tricks can hurt these CPUs | A |
| M6 | Anti-cheat awareness per game | Only Process Lasso addresses it | E |
| M7 | Diagnose the network instead of tweaking TCP: ping, jitter, packet loss, bufferbloat | "Lag" is usually route or Wi-Fi, which TCP tweaks do not fix **(inference)** | E |
| M8 | Equal NVIDIA, AMD and Intel coverage | Smart Game Booster overclocks NVIDIA and AMD only; Hone's clock lock is NVIDIA only | B |
| M9 | Plain statement of what *not* to expect (GPU-bound games will not gain) | The 2026 test found no gain at GPU-bound 1440p **(community)** | C |

## Ideas by area

| Area | Ideas from this survey |
|---|---|
| **A tweaks** | Keep R1, R2 and R5 as they are. Consider R3 (lower background priority per game) and R4 (CPU Sets per game) as situational, only after measuring. Keep RAM purge and TCP tweaks in possible placebo. Add X3D detection to power-plan and Game Bar tweaks (W7, M5). |
| **B GPU driver settings** | No one-click overclocks. Equal vendor coverage (M8). Vendor drivers only (W12). |
| **C measuring the difference** | Built-in before/after with frame times and 1% lows (M1). An FPS overlay or PresentMon capture (R7). Say plainly when a game is GPU-bound and will not gain (M9). |
| **D hidden-loss checks** | The honest "boost": find XMP off, wrong refresh rate, iGPU, throttling, PCIe link and Memory Integrity (M2). |
| **E online play** | Network diagnosis over TCP tweaks (W10, M7). Anti-cheat notes per game (R9, M6). |
| **other** | No registry cleaner or health score (W3, W4). No paywall (W11). In-game settings helper only if cheap to maintain (R8). |

## Gaps in this research

- The shared web-fetch and web-search budgets ran out mid-session. These pages could not be opened and are cited from search listings or the repo's earlier notes (each is marked "not re-read" or "not opened" where used): WiseCleaner's own Wise Game Booster page, Microsoft's current PUA criteria, Microsoft KB2563254, the FTC press releases, Norton's June 2021 launch release and PassMark report, Russinovich's article, Game Fire's site, and GameStar's and BGR's booster tests.
- Hone Premium's price is regional and was not shown. AVG TuneUp's price comes from a reseller listing.
- No YouTube benchmark was checked first-hand.
- No booster was run or traced (for example with Process Monitor), so "what it changes" relies on vendor descriptions and user reports.

## Sources

Vendor and first-party:

- Razer Cortex Game Booster: https://www.razer.com/cortex/booster
- Razer Cortex Boost: https://www.razer.com/ca-en/cortex/boost
- Razer Booster Prime: https://www.razer.com/cortex/booster-prime
- Razer support, "What is Booster Prime?": https://mysupport.razer.com/app/answers/detail/a_id/3854
- IObit Advanced SystemCare compare (Free vs Pro): https://www.iobit.com/en/compare/asc/
- IObit Advanced SystemCare manual: https://www.iobit.com/product-manuals/asc-help/
- IObit blog, how to fix lag in games (2026-04-27): https://www.iobit.com/en-blog/how-to-fix-lag-in-games.php
- Hone Premium: https://hone.gg/es/premium
- Hone recommended optimizations: https://support.hone.gg/en/recommended-optimizations
- Hone vs GearUP comparison: https://hone.gg/comparison/gearup
- Hone on Overwolf: https://overwolf.com/app/AuraSide_Inc-Hone
- Ashampoo WinOptimizer: https://www.ashampoo.com/en-us/winoptimizer-27/detail
- CCleaner Performance Optimizer: https://ccleaner.com/ccleaner/performance-optimizer-pro
- Gen Digital, CCleaner 6.0 press release (2022-05-12): https://newsroom.gendigital.com/2022-05-12-ccleaner-unveils-6-0-release-with-new-patented-performance-optimizer
- AVG TuneUp guide: https://avg.com/en-ww/avg-pctuneup-guide-pc-speed
- Smart Game Booster PRO on Steam: https://store.steampowered.com/app/1289950
- Norton Game Optimizer press release (2021-09-13): https://www.scoop.co.nz/stories/BU2109/S00276.htm
- Norton launch press release via Nasdaq (2021-06-08, not re-read): https://www.nasdaq.com/press-release/norton-levels-up-performance-for-pc-gamers-2021-06-08
- Norton blog, PassMark ranking (now redirects): https://www.nortonlifelock.com/blogs/gaming/passmark-ranks-norton-game-optimizer
- Bitsum Process Lasso: https://bitsum.com/
- Bitsum Process Lasso FAQ: https://bitsum.com/pl-faq/
- Bitsum, "Stop using RAM Cleaners" (2017-10-28): https://bitsum.com/?p=231589
- Microsoft Learn, Windows gaming performance features: https://learn.microsoft.com/en-us/training/modules/optimize-windows-gaming/4-performance-gaming-features
- Microsoft, registry cleaner support policy (KB2563254): https://support.microsoft.com/en-us/topic/2563254
- Microsoft Security blog, coercive optimizers (2018-01-30): https://www.microsoft.com/en-us/security/blog/2018/01/30/protecting-customers-from-being-intimidated-into-making-an-unnecessary-purchase/
- Microsoft, malware and PUA criteria: https://learn.microsoft.com/en-us/defender-xdr/criteria
- FTC, Restoro and Reimage $26M (2024-03): https://www.ftc.gov/news-events/news/press-releases/2024/03/tech-support-firms-will-pay-26-million-settle-ftc-charges-they-deceived-consumers-buying-repair
- FTC, Avast final order (2024-06): https://www.ftc.gov/news-events/news/press-releases/2024/06/ftc-finalizes-order-avast-banning-it-selling-or-licensing-web-browsing-data-advertising-requiring-it
- Russinovich, "The Memory-Optimization Hoax": https://itprotoday.com/cloud-computing/memory-optimization-hoax

Independent tests and community **(community)**:

- ghacks, booster benchmarks (2015): https://www.ghacks.net/2015/05/23/do-game-boosters-improve-pc-gaming-performance/
- PC Games Hardware, IObit Game Booster test (2009-04-18): https://www.pcgameshardware.de/Tools-Software-156186/Tests/Game-Booster-Nutzlose-Software-681891/
- BetterFPS, seven boosters tested (2026-06-13): https://betterfps.com/blog/do-fps-boosters-actually-work-tested
- TechRadar, Razer Cortex review (2022-03-15): https://www.techradar.com/reviews/razer-cortex
- GameStar booster test (not opened): https://www.gamestar.de/artikel/game-booster-test,3383476.html
- BGR, stop downloading FPS boosters (not opened): https://www.bgr.com/2248618/stop-downloading-fps-booster-pc/
- Razer Insider, Cortex boost tanks frame rate (2023-11): https://insider.razer.com/razer-cortex-39/razer-cortex-boost-tanks-frame-rate-52359
- Razer Insider, power plan and boost defaults (2023-08): https://insider.razer.com/razer-cortex-39/razer-cortex-power-plan-boost-default-settings-50206
- ghacks, CCleaner 6 Performance Optimizer (2022): https://www.ghacks.net/?p=178552
- CCleaner community, optimizer flags needed programs: https://community.ccleaner.com/t/performance-optimizer-reports-mandatory-programs-as-problems/80431
- BetaNews, Advanced SystemCare 19 (2025-10-14): https://betanews.com/2025/10/14/iobit-advanced-systemcare-19-improves-windows-performance-and-security-heres-whats-new/
- BetaNews, WinOptimizer 11 adds Game Booster (2014): https://betanews.com/2014/04/03/ashampoo-winoptimizer-11-adds-game-booster-user-rights-manager
- TheWindowsClub, Wise Game Booster: https://www.thewindowsclub.com/optimize-gameplay-wise-game-booster
- TechSpot, Wise Game Booster download: https://www.techspot.com/downloads/6156-wise-game-booster.html
- TrustRadius, AVG TuneUp pricing: https://www.trustradius.com/products/avg-tuneup-for-pc
- Slickdeals, Process Lasso Pro deal: https://slickdeals.net/f/17971464-process-lasso-pro-40-sale-23-97-lifetime-license
- The Register, tainted CCleaner downloads (2017-09-18): https://www.theregister.com/2017/09/18/tainted_ccleaner_downloads
- TechSpot, Defender flags CCleaner as PUA (2020): https://www.techspot.com/news/86216-windows-defender-flagging-ccleaner-potentially-unwanted-application.html

Repo notes: `research/gaming-tweak-catalogue.md` on `origin/claude/project-thread-5ggkan` (items 1, 5, 8, 10, 19, 20, 22) and `GLOSSARY.md`.
