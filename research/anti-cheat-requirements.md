# Find what each anti-cheat checks

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/6

Researched 2026-10-15, mostly against first-party support pages: Riot, Epic (EAC, plus BattlEye errors that Epic hosts), Ubisoft's BattlEye FAQ, FACEIT, Activision, EA, Microsoft Learn and NVD. Each claim has a link. Anything marked **(community)** comes from a secondary or community source. Anything marked **(inference)** is my own reading, and no source says it.

## Answer

- **Hard launch blocks come from boot and kernel trust settings, not from tweaks.** Disabling Secure Boot, TPM 2.0, Memory Integrity (HVCI)/VBS or IOMMU stops Riot Vanguard games from launching. FACEIT requires the same set, partly through a staged rollout. CoD (Ricochet) and Battlefield 6 (Javelin) require TPM 2.0 and Secure Boot. Fortnite (EAC) requires them only for higher-tier tournaments.
- **Biggest conflict with our own catalogue:** "Turn off Memory Integrity/VBS for FPS" (see `research/gaming-tweak-catalogue.md`) breaks Valorant/LoL outright (`VAN: RESTRICTION: 5`, `VAN 9005`). It can also break FACEIT. That tweak needs the strongest warning in the app, and it should name those games.
- **Test signing, disabled driver signature enforcement, kernel debugging and Driver Verifier** block BattlEye and EAC at launch. Riot's fix for Vanguard errors is to turn test signing and `nointegritychecks` back off. The app should never set any of these.
- **Vulnerable kernel drivers** are the main risk for the OC, monitoring and fan features:
  - WinRing0 (CVE-2020-14979) and old MSI RTCore64 (CVE-2019-16098) are the examples.
  - Vanguard stops known vulnerable drivers from loading at boot, and it can block the game when it finds incompatible software.
  - BattlEye blocks tools that use such drivers (SpeedFan is the named example).
  - Microsoft's vulnerable-driver blocklist is on by default on Windows 11, and Defender flags WinRing0.
  - **We must not ship WinRing0 or any other generic port/physical-memory driver.**
- **The driver installer can trigger blocks too.** Vanguard and FACEIT block launch over specific OEM storage and Wi-Fi drivers, because those drivers break DMA remapping. The named ones are Intel RST/VMD `iastorvd.sys`, `secnvme.sys`, `solidnvm.sys`, `mtinvme`, `asstahci64.sys`, `mvs91xx`, `amd_sata`, and MediaTek RZ616 `mtkwl6ex.sys` < 3.5.
- **Debloat can also block launch:**
  - Vanguard's `vgc` service must be set to Automatic unless the player uses Pre-Check.
  - Windows must stay updated: Vanguard sets a minimum build, and FACEIT sets an OS version deadline.
  - Modified or "untrusted" system files cause EAC and BattlEye launch failures.
  - BattlEye reports "kernel modification" when other software has turned off Windows security features.
- **No first-party source says a system setting or a legitimate OC/monitoring tool causes a ban.** Documented bans are for cheats: BattlEye bans for hack software left running even from other games, and Riot issues HWID bans for ToS violations. "Ban risk" in the matrix is therefore **low/undocumented** for every optimizer action. The real outcomes are launch blocks, matchmaking restrictions and kicks. Riot's October 2026 block on game-memory access by third-party apps is explicitly **not** a ban.
- **Gaps:** I found no first-party requirement pages for ESEA, nProtect GameGuard or Tencent ACE. I also found no official statement from any vendor on MSI Afterburner/RTSS or HWiNFO. These should be treated as unknown, not as safe.

## Severity scale used in the matrix

| Severity | Meaning |
|---|---|
| **Won't launch** | The anti-cheat refuses to start the game or service, or shows a blocking error. |
| **Restricted** | The game runs, but modes or matchmaking are limited (separate pool, no ranked, no tournaments). |
| **Kick** | The player is removed from a session, without an account penalty. |
| **Tool blocked** | The anti-cheat stops the *other* program or driver from loading, and the game still runs. |
| **Ban risk** | An account or hardware penalty is documented for this action. None of the optimizer actions below reaches this level in a first-party source. |

## Matrix: optimizer action → objecting anti-cheat → severity → source

| # | Optimizer action | Anti-cheats that object | Severity | Source |
|---|---|---|---|---|
| 1 | Turn off **Memory Integrity (HVCI)** | Riot Vanguard (Valorant, LoL, 2XKO) | Won't launch (`VAN: RESTRICTION: 5`) | https://support.riotgames.com/en-us/riot/penalties/error-van-restriction-5 |
| | | FACEIT AC | Won't launch, for players who are required to have it ("Some players may be required to enable it") | https://support.faceit.com/hc/en-us/articles/22851956652956-Known-issues-with-Anti-Cheat-Requirements |
| 2 | Turn off **VBS / hypervisor** (e.g. `bcdedit /set hypervisorlaunchtype off`, which removes HVCI) | Vanguard | Won't launch (`VAN 9005`: VBS needs TPM 2.0 + UEFI) | https://support.riotgames.com/en-us/riot/client/error-van-9005 |
| | | FACEIT AC | Won't launch ("We require VBS to support IOMMU reliably") | https://support.faceit.com/hc/en-us/articles/23117181142556 |
| 3 | Turn off **CPU virtualization** (VT-x/SVM) in a BIOS guide | FACEIT AC (required "for most players with compatible hardware"); Vanguard indirectly, since HVCI and VBS need it | Won't launch | https://support.faceit.com/hc/en-us/articles/22851956652956-Known-issues-with-Anti-Cheat-Requirements ; https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity |
| 4 | Turn off **IOMMU / VT-d / AMD-Vi / DMA protection** | Vanguard | Won't launch (`VAN: RESTRICTION`). Vanguard checks the BIOS setting only, so Windows Kernel DMA Protection status does not matter | https://support.riotgames.com/en-us/riot/support-tools/enabling-iommu ; https://support.riotgames.com/en-us/riot/penalties/error-van-restriction |
| | | FACEIT AC | Won't launch. Enforced for players above 3,000 Elo since Aug 2025, and rolling out wider | https://support.faceit.com/hc/en-us/articles/22851956652956-Known-issues-with-Anti-Cheat-Requirements |
| 5 | Turn off **Secure Boot** (or use Legacy/CSM boot or an MBR disk) | Vanguard | Won't launch (`VAN: RESTRICTION 1` / `4`) | https://support.riotgames.com/en-us/riot/penalties/error-van-restriction-1 ; https://support.riotgames.com/en-us/riot/penalties/error-van-restriction-4 |
| | | FACEIT AC | Won't launch (mandatory since 25 Nov 2025) | https://support.faceit.com/hc/en-us/articles/22851956652956-Known-issues-with-Anti-Cheat-Requirements |
| | | Activision Ricochet (BO7, Warzone, MW4) | Restricted: separate pool, no Ranked; in MW4, no online play | https://support.activision.com/articles/trusted-platform-module-and-secure-boot ; https://www.callofduty.com/blog/2026/06/call-of-duty-black-ops-7-warzone-ricochet-anti-cheat-season-04 |
| | | EA Javelin (Battlefield 6) | Won't play: "a requirement to play", and the game shows a prompt | https://www.ea.com/games/battlefield/battlefield-6/news/secure-boot-information |
| | | EAC in Fortnite | Restricted: no tournaments that need Account Level 350+ | https://www.epicgames.com/help/fortnite-battle-royale-c-202300000001636/competitive-c-202300000001722/why-do-i-need-to-enable-tpm-and-secure-boot-on-a-windows-pc-a202300000014544 |
| | | Delta Force (anti-cheat not named on the page) | In-game pop-up requiring it; enforcement is unclear | https://deltaforce.garena.com/en/news/announcement/CNDZCB |
| 6 | Turn off or clear the **TPM 2.0**, or apply a "bypass TPM check" registry hack | Vanguard ("LoL x Vanguard comes with a TPM 2.0 requirement"; it warns users who "modified registry keys to bypass this requirement") | Won't launch | https://www.leagueoflegends.com/en-us/news/dev/dev-vanguard-x-lol/ ; https://support.riotgames.com/en-us/riot/penalties/error-van-restriction |
| | | FACEIT AC | Won't launch (mandatory since 25 Nov 2025) | https://support.faceit.com/hc/en-us/articles/22851956652956-Known-issues-with-Anti-Cheat-Requirements |
| | | Ricochet | Restricted (as in row 5) | https://support.activision.com/articles/trusted-platform-module-and-secure-boot |
| | | Javelin / EA | Prompt; the EA help page says "TPM 2.0 needs to be turned on" | https://help.ea.com/help/pc/secure-boot/ |
| | | EAC in Fortnite | Restricted (tournaments) | same Epic link as row 5 |
| 7 | Turn on **test signing** (`bcdedit /set testsigning on`) or **disable driver signature enforcement** (`nointegritychecks`), e.g. to load an unsigned OC or fan driver | BattlEye | Won't launch ("Windows Test-Signing Mode not supported") | https://www.epicgames.com/help/technical-support-c-202300000001619/third-party-support-c-202300000001668/failed-to-initialize-battleye-service-windows-test-signing-mode-not-supported-error-a202300000012901 |
| | | Vanguard | Won't launch. Riot's fix for `VAN 25`/`128`/`193` is to turn both settings off | https://support.riotgames.com/riot/security/restore-windows-security-to-normal-state ; https://support.riotgames.com/en-us/riot/performance/error-van-25/ |
| | | EAC | Won't launch **(community)**. I found no first-party EAC page; the easy.ac knowledge base failed to load | https://steamcommunity.com/app/252490/discussions/0/610574394237040993?ctp=1 |
| 8 | Turn on **kernel debugging** (`bcdedit /debug on`), **Driver Verifier**, or debug memory dumps | BattlEye (kernel debugging) | Won't launch | https://www.epicgames.com/help/en-US/c-Category_TechnicalSupport/c-ThirdPartySupport/failed-to-initialize-battleye-service-kernel-debugging-enabled-error-a000086624 |
| | | EAC (Driver Verifier) | Won't launch | https://www.epicgames.com/help/technical-support-c-202300000001619/pc-troubleshooting-c-202300000001670/why-do-i-receive-the-error-easy-anti-cheat-cannot-run-with-driver-verifier-enabled-a202300000016427 |
| | | Vanguard ("debugging settings"; the fix is to turn off debug memory dumps) | Won't launch (`VAN 128`) | https://support.riotgames.com/en-us/riot/performance/error-van-128 |
| 9 | **Install or load a known-vulnerable signed kernel driver** for OC, monitoring, fan or RGB control. Examples: WinRing0 (CVE-2020-14979), old RTCore64 from MSI Afterburner (CVE-2019-16098) | Vanguard: it prevents "known signed-but-vulnerable drivers from loading" at boot, and it denylisted an RGB keyboard driver until the vendor fixed it | Tool blocked. If it detects incompatible software it shows `VAN: Incompatible Software`, which means won't launch | https://www.leagueoflegends.com/en-us/news/dev/dev-vanguard-x-lol/ ; https://support.riotgames.com/en-us/riot/performance/error-van-incompatible-software ; https://support.riotgames.com/en-us/league-of-legends/performance/riot-vanguard-faq-league-of-legends |
| | | BattlEye ("blocking certain software (like Speedfan) that is using kernel drivers which contain known security issues") | Tool blocked | https://www.epicgames.com/help/technical-support-c-202300000001619/third-party-support-c-202300000001668/speedfan-or-potentially-other-hardware-related-software-stops-working-when-battleye-is-enabled-a202300000014076 |
| | | Windows itself: the vulnerable-driver blocklist and Defender (`HackTool:Win32/Winring0`) | Driver blocked or quarantined | https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules ; Defender name **(community)**: https://www.neowin.net/news/windows-1110-is-flagging-winring0-on-your-pc-monitoring-fan-control-apps-heres-why/ ; CVEs: https://nvd.nist.gov/vuln/detail/CVE-2020-14979 , https://nvd.nist.gov/vuln/detail/CVE-2019-16098 |
| 10 | Turn off the **Microsoft vulnerable driver blocklist** | No anti-cheat names this setting. It is still enforced whenever HVCI is on, so it becomes moot on Vanguard and FACEIT machines that must run HVCI (inference) | No documented severity | https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules |
| 11 | The **driver installer** installs OEM storage or Wi-Fi drivers that are named as incompatible: Intel RST/VMD `iastorvd.sys` (older versions), Samsung `secnvme`, Solidigm `solidnvm`, Micron `mtinvme`, Asmedia `asstahci64`, Marvell `mvs91xx`, AMD `amd_sata`, MediaTek RZ616 `mtkwl6ex.sys` < 3.5 | Vanguard (`VAN: Incompatible OEM Driver`) | Won't launch | https://support.riotgames.com/en-us/riot/performance/van-incompatible-oem-driver |
| | | FACEIT AC ("Incompatible driver detected": these drivers conflict with DMA remapping) | Won't launch | https://support.faceit.com/hc/en-us/articles/25296204531996 |
| 12 | **Run OC or monitoring apps** (MSI Afterburner/RTSS, HWiNFO, etc.) | No first-party page names these tools. BattlEye says it "will not block popular programs" (Discord, Fraps, OBS, XSplit). Riot only says tools that read game memory may break | Unknown. No documented ban | https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq ; https://www.leagueoflegends.com/en-us/news/dev/dev-vanguard-x-lol/ |
| 13 | Ship or recommend **overlays or tools that read game memory** (e.g. stat trackers) for Riot games | Vanguard: since 6 Oct 2026, memory access is blocked for unknown third-party apps; approved tools have until Apr 2027 | Tool blocked. Riot says this is not a ban | https://support.riotgames.com/ar-ae/riot/performance/game-memory-access-removed-for-third-party-apps |
| 14 | **Inject graphics mods** (ReShade, Special K) or drop `d3d9.dll`/`dsound.dll` into game folders | BattlEye | Won't launch / driver load error | https://support.rockstargames.com/articles/2qX3EkMbz2ahbzhgJXSZ29/failed-to-initialize-battleye-service-driver-load-error-1072 ; https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq |
| 15 | **Debloat: disable or remove anti-cheat services or startup entries** (Vanguard `vgc`/`vgk`, the FACEIT AC driver that loads at boot, BattlEye/EAC services) | Vanguard: `vgc` must be Automatic unless Pre-Check is used. Re-enabling it needs a reboot, and "interacting with Vanguard in an unsanctioned way will prevent you from playing" | Won't launch (`VAN 57`) | https://support.riotgames.com/en-us/riot/performance/error-van-57 ; https://support.riotgames.com/en-us/league-of-legends/performance/riot-vanguard-faq-league-of-legends |
| | | FACEIT AC (kernel driver "loaded upon PC boot"; a restart re-enables it) | Won't launch until reboot | https://support.faceit.com/hc/en-us/articles/9394666828188 |
| 16 | **Debloat: block or defer Windows Update** | Vanguard: minimum build 19045, and a PC with outdated Windows "may be restricted from playing" | Won't launch | https://support.riotgames.com/en-us/riot/penalties/error-van-restriction ; https://support.riotgames.com/en-us/league-of-legends/performance/riot-vanguard-faq-league-of-legends |
| | | FACEIT AC (from 14 Oct 2026, Windows 10 needs 22H2 + ESU; Windows 11 is required after Oct 2027) | Won't launch | https://support.faceit.com/hc/en-us/articles/23117181142556 |
| | | Ricochet (a Windows update fixes TCG event log attestation failures) | Restricted | https://support.activision.com/articles/trusted-platform-module-and-secure-boot |
| 17 | **Debloat: modify or replace system files**, or remove Windows components (custom ISOs, patched DLLs, "tweaked" DirectX) | EAC ("Untrusted system file") | Won't launch | https://www.epicgames.com/help/c-202300000001639/c-202300000001736/easy-anti-cheat-eac-error-untrusted-system-file-a202300000013813 |
| | | BattlEye ("Windows Kernel modification detected": software "that disables certain important security features of Windows"; "don't use any tweaked/custom versions of system files (including DirectX)") | Won't launch. The fix can be a full Windows reinstall | https://www.epicgames.com/help/en-US/c-Category_TechnicalSupport/c-ThirdPartySupport/windows-kernel-modification-detected-error-in-battleye-a000086629 ; https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq |
| | | Vanguard ("Some Windows security settings can prevent Vanguard from verifying your PC") | Won't launch (`VAN 25`/`128`/`193`) | https://support.riotgames.com/en-us/riot/performance/error-van-193 |
| 18 | **Suppress UAC prompts or auto-deny elevation** | Ricochet (declining the CODBrokerInstaller.exe / enrollaik.exe UAC prompt means "not being able to play") | Won't launch | https://support.activision.com/articles/trusted-platform-module-and-secure-boot |
| 19 | **Turn on Kernel-mode Hardware-enforced Stack Protection** (the reverse case: a hardening toggle) | EAC (driver error 1275). The page may be dated, so re-check before using it | Won't launch | https://www.epicgames.com/help/c-202300000001639/c-202300000001736/eac-driver-error-1275-driver-load-error-in-windows-11-a202300000010435 |
| 20 | **Unstable RAM or CPU overclock** | BattlEye "Corrupted Memory #X". Its own suggested fix is to test RAM with Memtest86. Linking this to OC is my inference | Kick | https://www.epicgames.com/help/c-202300000001636/c-202300000001719/i-am-getting-kicked-for-the-following-violation-s-i-don-t-use-hacks-so-what-can-i-do-to-resolve-this-a202300000011200 |
| 21 | **Run any cheat or hack tool, even for another game** (not an optimizer action; listed to show where bans come from) | BattlEye ("actively ban for it"); Vanguard HWID ban `VAN 152` for ToS violations | **Ban** | https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq ; https://support.riotgames.com/en-us/riot/performance/error-van-152 |

## Per anti-cheat summary

| Anti-cheat (games) | TPM 2.0 | Secure Boot | HVCI / VBS | IOMMU / DMA | Test signing / DSE off | Vulnerable drivers | Services / debloat |
|---|---|---|---|---|---|---|---|
| **Riot Vanguard** (Valorant, LoL, TFT, 2XKO) | Required | Required | HVCI required (`RESTRICTION: 5`) | Required (BIOS setting) | Must be off | Blocked at boot; may block the game | `vgc` Automatic; Windows build ≥ 19045 |
| **FACEIT AC** (CS2 on FACEIT) | Required (25 Nov 2025) | Required (25 Nov 2025) | VBS required; HVCI for some players | Required, staged rollout; incompatible storage drivers | Not documented | Storage/Wi-Fi driver list only | Win10 22H2 + ESU from Oct 2026 |
| **Easy Anti-Cheat** (Fortnite, Apex, etc.) | Per game (Fortnite: tournaments only) | Per game (as for TPM) | Not documented. Kernel stack protection broke it | Not documented | Community only | Not documented | Untrusted system files → won't launch; Driver Verifier off |
| **BattlEye** (R6 Siege, PUBG, DayZ, Arma, Tarkov) | Not documented | Not documented | Not documented | Not documented | Won't launch; kernel debugging also blocked | Blocks the tools that use them | Custom system files / security features off → won't launch |
| **Activision Ricochet** (BO7, Warzone, MW4) | Required (restricted pool) | Required (restricted pool) | Not documented | Not documented | Not documented | Not documented | Accept UAC; keep Windows updated |
| **EA Javelin** (Battlefield 6) | Listed on the EA help page | Required | Not documented | Not documented | Not documented | Not documented | — |
| **ESEA, nProtect GameGuard, Tencent ACE** | No first-party page found | No first-party page found (Delta Force asks for SB + TPM) | — | — | — | — | — |

## Notes for the app

- **Warning, not block:** this follows the owner's decision. Each warning should name the games affected, using the matrix. For example: "Disabling Memory Integrity will stop Valorant/LoL (Vanguard) and may stop FACEIT from launching."
- **Hard rules for our own code (judgment):**
  - Never set `testsigning`, `nointegritychecks` or `debug`.
  - Never ship WinRing0, InpOut or similar generic physical-memory/port drivers. Use vendor SDKs or user-mode APIs for monitoring.
  - Debloat lists must exclude `vgc`, `vgk`, FACEIT, BattlEye and EAC services and drivers.
  - The driver installer should prefer the Microsoft inbox NVMe/AHCI drivers over the OEM storage drivers listed in row 11.
- **Re-check cadence:** these pages change often. For example, Riot's restriction codes and FACEIT's rollout both changed during 2025–2026. Re-verify before each release.
- **Unverified:**
  - Whether WinRing0 and RTCore64 hashes are in Microsoft's blocklist XML. The download (`aka.ms/VulnerableDriverBlockList`) was blocked from this environment, and the docs page lists no driver names.
  - Any official anti-cheat statement on MSI Afterburner/RTSS or HWiNFO.
