# Find what actually helps online play on a home PC

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/92

Researched 2026-10-30 against Microsoft Learn, RFCs and IETF drafts, Valve, Epic, Riot and Blizzard pages, NIC vendor guides, the bufferbloat project and measured tests. Each claim has a link. Anything marked **(community)** comes from a secondary or community source. Anything marked **(inference)** is my own reasoning, and no source says it. A **†** after a link means the page could not be opened this session (the web-fetch budget shared by all agents ran out), so the claim rests on that page's known content and must be re-checked before anything ships on it.

## Answer

- **A free app on the PC can only fix the PC's own end.** It can pick the link (cable or Wi-Fi), stop the PC's own big transfers, and set the adapter's power options. It cannot change the router's queue, the ISP's route or where the server is **(inference)**. Paid route-changing services are out of scope.
- **Two things measurably help, and both are proven:**
  - **Play on Ethernet, not Wi-Fi.** Wi-Fi adds delay and, worse, uneven delay (jitter) and retries. Game publishers tell players to use a cable first.
  - **Stop or limit big downloads and uploads while playing.** A busy line makes the router's buffer fill up, and every game packet waits behind it ("bufferbloat"). The real cure is Smart Queue Management (SQM) on the router. The PC can only stop or cap its own traffic.
- **Measure, don't guess.** The app can show ping, jitter and loss to the router, to the internet and to the game's server, side by side. That split tells the player whether the problem is their Wi-Fi, their line or the route.
- **Finding the game's server is possible but not with the usual API.** `GetExtendedTcpTable` lists remote addresses for TCP, but `GetExtendedUdpTable` lists only the local side, and most game traffic is UDP. The kernel's own network events (ETW) carry the process id and the remote address of every UDP packet, so the elevated service can find the busiest remote address of the game process.
- **Ping (ICMP) to that address often won't work.** Valve games go through Steam Datagram Relay, which hides the server's address, and many servers ignore ping. When ping gets no reply at all, the app must say "this server doesn't answer ping", never "100% loss". Fall-backs are: ping the last router on the path that answers; watch the timing of the game's own incoming packets (jitter without sending anything); read the game's own network overlay.
- **Packet loss shown at middle hops of a traceroute is usually fake.** Routers slow down their replies to ping on purpose. Only loss that carries on to the last hop is real.
- **Situational:** turning Wi-Fi power saving to "Maximum Performance" while a game runs (instant, reversible, fits a per-game profile); capping Windows Update's Delivery Optimization and OneDrive bandwidth; a Windows QoS **throttle** policy that caps a named app's uploads.
- **Possible placebo:** Energy Efficient Ethernet off (it saves microseconds, ping is in milliseconds); Windows QoS **DSCP** marking for games (most home routers ignore it and many ISPs wipe it); changing DNS (it only changes name lookups, not the ping of a running match).
- **Anti-cheat:** no first-party anti-cheat page names any of these network settings. None of them reads game memory. The app should not install packet-capture drivers, send probes to game ports, or block relay servers **(inference)**.
- **Gaps:** whether DSCP marking works on a home (non-domain) Windows 11 PC without a restart; whether Wi-Fi drivers turn DSCP into Wi-Fi priority; whether DO and QoS throttle changes reach downloads already running. Each needs a test on a real PC.

## What the PC can and can't reach

A game packet goes: game → Windows network stack → network adapter → (cable or Wi-Fi) → home router → ISP line → internet route → relay or game server, and back.

| Part of the path | Can a free app on the PC change it? |
|---|---|
| What else the PC sends and receives | **Yes.** Pause, cap or close background transfers. |
| Adapter type and settings | **Partly.** It can't plug in a cable, but it can say Wi-Fi is in use and change power settings. |
| Packet markings (DSCP) | **Yes**, but only the routers on the way decide whether to honour them. |
| Home router queue (bufferbloat) | **No.** It can measure it and point to the router's SQM setting. |
| Other devices at home | **No.** It can only show that the line is busy. |
| ISP line, internet route, server location | **No** (in-game region choice aside). Paid route services are out of scope. |

All rows are **(inference)** from how IP networks work; the sections below give the evidence for each.

## Measuring ping, jitter and packet loss

### What the numbers mean

- **Ping (round-trip time, RTT):** the time from sending a packet to getting the answer back. RFC 2681 defines it as a metric. https://www.rfc-editor.org/rfc/rfc2681 †
- **Jitter:** how much that delay changes from packet to packet. RFC 5481 sets out two ways to state it: the difference between one packet and the next (IPDV), and each packet's delay above the lowest seen (PDV). https://www.rfc-editor.org/rfc/rfc5481 †
- RTP (used for voice and video) keeps a running jitter estimate that moves 1/16 of the way to each new difference. https://www.rfc-editor.org/rfc/rfc3550#section-6.4.1 †
- **Packet loss:** the share of packets that never arrive in a set time. RFC 7680 defines it one way. https://www.rfc-editor.org/rfc/rfc7680 † A ping can't tell whether the request or the reply was lost **(inference)**.
- **Suggested display (inference):** median ping, jitter as the 95th-percentile ping minus the lowest ping (the PDV form, which players read as "spikes"), and loss as a percentage over the last 100 probes. State which form is used, because RFC 5481 warns that the two forms answer different questions.

### Where to measure: three targets, not one

The app should ping three places at once **(inference)**:

1. **The router (default gateway).** Over a cable this should be about 1 ms and flat. Spikes here mean the problem is inside the home: Wi-Fi, a bad cable, a busy router.
2. **A stable internet target near the ISP** (the first ISP hop, or a large public anycast address). Spikes here, with a flat router, point at the line or bufferbloat.
3. **The game's server or relay**, found as below. Spikes only here point at the route or the server.

The default gateway comes from the IP routing table (`GetIpForwardTable2`). https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getipforwardtable2 †

### Finding which server a running game talks to

| Method | What it gives | Good for games? |
|---|---|---|
| `GetExtendedTcpTable` (owner PID table) | Local and **remote** address and port of every TCP connection, and the owning process id. https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable † , https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcprow_owner_pid † | Only for games whose live traffic is TCP. Most action games send live state over UDP, and use TCP for log-in, store and chat **(inference)**. |
| `GetExtendedUdpTable` (`MIB_UDPROW_OWNER_PID`) | Only `dwLocalAddr`, `dwLocalPort` and `dwOwningPid`. **No remote address or port.** https://learn.microsoft.com/en-us/windows/win32/api/udpmib/ns-udpmib-mib_udprow_owner_pid | Shows that the game has a UDP socket open, not where it sends. |
| Kernel network events (ETW, NT Kernel Logger, `EVENT_TRACE_FLAG_NETWORK_TCPIP`) | One event per UDP send and receive, with `PID`, `size`, `daddr`, `saddr`, `dport`, `sport`. https://learn.microsoft.com/en-us/windows/win32/etw/udpip , https://learn.microsoft.com/en-us/windows/win32/etw/udpip-typegroup2 | **Best fit.** Count packets per (game PID, remote address) for a few seconds. The busiest remote address during a match is the server or relay **(inference)**. The same feed gives bytes per process for the "what is using the line" view. |
| Packet Monitor (`pktmon`, built into Windows) | Packet capture and counters from the command line. https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/pktmon † | Good for checking the app's own work (for example, whether DSCP marks are set). Not needed for live use **(inference)**. |

Notes:

- Starting a kernel trace session needs administrator rights (or the Performance Log Users group). The app's elevated service has them. https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracea †
- Kernel network events are the feed Windows' own Resource Monitor network view is built on **(inference)**. Using them reads nothing inside the game.
- A busy game sends 20 to 128 packets a second, so the right address stands out within seconds **(inference)**.

### Relays and servers that hide or ignore ping

- **Valve games (CS2, Dota 2, Deadlock) and other Steam games that use Steam Datagram Relay (SDR)** send game traffic over Valve's backbone through relays. "IP addresses are never revealed", so the address the PC sees is a relay, not the server. https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay?language=english
- Valve says the relayed route often "actually improves player ping times", but for servers outside Valve's data centres the relayed route "might be slower than the default IP route". Same link.
- Steam gives games their ping to Valve's data centres through `ISteamNetworkingUtils`. Same link. A third-party app can't use that API in a supported way, because it needs a Steamworks app **(inference)**.
- So for SDR games, "ping to the address the game talks to" means ping to the nearest relay. That is still the part of the path the player's own line affects **(inference)**.

### How to probe

| Probe | How | Works when | Limits |
|---|---|---|---|
| ICMP echo (ping) | `IcmpSendEcho2` / `Icmp6SendEcho2`; can run asynchronously; reply holds `RoundTripTime` in milliseconds. https://learn.microsoft.com/es-es/windows/win32/api/icmpapi/nf-icmpapi-icmpsendecho2 | The target answers ping. | Whole-millisecond results; time the call yourself for finer jitter **(inference)**. Many servers and relays drop ping. |
| .NET `Ping` class | Managed wrapper for the same thing. https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.ping † | Same. | Same. |
| Ping the last router before the server | Run a traceroute (`tracert`, `pathping`) to the server and ping the last hop that answers. https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/pathping † | The server drops ping but its data-centre router doesn't. | That router may slow its own replies (see below). Good for trends, not exact numbers **(inference)**. |
| TCP round-trip stats | `GetPerTcpConnectionEStats` with `TcpConnectionEstatsPath` gives `SmoothedRtt`, `RttVar`, `MinRtt`, `MaxRtt` (ms) for a live TCP connection; collection must first be switched on with `SetPerTcpConnectionEStats`. https://learn.microsoft.com/en-us/windows/win32/api/tcpestats/ns-tcpestats-tcp_estats_path_rod_v0 , https://learn.microsoft.com/en-us/windows/win32/api/tcpestats/ne-tcpestats-tcp_estats_type | The game's live traffic is TCP. | Needs no extra packets; useless for UDP games. |
| Watch the game's own packets | From the ETW feed, time the gaps between packets arriving from the server. | Always, for UDP games. | Shows **downstream jitter and stalls** without sending anything. It can't count loss, because only the game knows its own sequence numbers **(inference)**. |
| The game's own overlay | See the table below. | The game has one. | The most honest number, because it measures the real game traffic **(inference)**. |

The app should **not** send its own UDP packets to a game server's port. The protocol is the game's own, an unexpected packet could be treated as an attack, and it proves nothing the methods above can't **(inference)**.

When a target never answers ping from the very first probe, the app should label it "doesn't answer ping" and switch to a fall-back. Showing "100% packet loss" would be false **(inference)**.

### Traceroute hops that "lose" packets

- Routers forward traffic in hardware but answer ping and traceroute with a slower control processor, and they limit how fast they answer. So a middle hop can show loss or high ping while the path through it is fine. Loss only matters if it carries on to the last hop. https://archive.nanog.org/meetings/nanog47/presentations/Sunday/RAS_Traceroute_N47_Sun.pdf † **(community: NANOG operator talk)**
- ICMPv6 requires error-message rate limiting in every node. https://www.rfc-editor.org/rfc/rfc4443#section-2.4 †
- `pathping` computes loss per hop over time, so it shows the same pattern. https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/pathping †
- The app should show per-hop numbers only with this warning, or show only the first hop, the ISP and the last hop **(inference)**.

### The game's own network overlay

| Game | How to show it | Source |
|---|---|---|
| Fortnite | Epic's help page on checking packet loss in a match (Settings → net debug stats). | https://www.epicgames.com/help/fortnite-battle-royale-c-202300000001636/technical-support-c-202300000001719/fortnite-a202300000012783 † |
| Counter-Strike 2 | Settings → Game → Telemetry: ping, packet loss and jitter on the HUD. | **(community)** https://gameriv.com/counter-strike-2s-new-telemetry-settings-will-give-you-all-the-benefits-from-csgos-net_graph-1/ |
| Overwatch 2 | `Ctrl+Shift+N` network graph. | **(community)** https://cyberpost.co/what-is-ctrl-shift-n-in-overwatch-2/ |
| Valorant | Settings → Video → Stats: network round-trip time and packet loss. | **(community)** https://prosettings.net/blog/how-to-show-fps-and-ping-in-valorant |

The app can't read these numbers without reading the game, which it must not do **(inference)**. It can tell the player where to switch them on, so they can compare.

## Bufferbloat

### What it is

- Bufferbloat is "high latency (or lag) that occurs when there's other traffic on your network". https://www.bufferbloat.net/projects/bloat/wiki/What_can_I_do_about_Bufferbloat/
- Cause: the router or modem holds a large queue. When a download or upload fills the line, every game packet waits at the back of that queue **(inference, standard explanation)**. The IETF recommends active queue management against it. https://www.rfc-editor.org/rfc/rfc7567 †

### How to show it from the PC

- The bufferbloat project's own manual test: ping something, then start a speed test, and watch whether ping jumps during upload or download. If it does, the router is "probably bloated". https://www.bufferbloat.net/projects/bloat/wiki/Tests_for_Bufferbloat/
- Thresholds from the project:
  - Extra latency under load below about 15–25 ms means bufferbloat is under control. https://www.bufferbloat.net/projects/bloat/wiki/What_can_I_do_about_Bufferbloat/
  - Above about 50 ms, or a grade below "B" on the web tests, suggests bufferbloat. https://www.bufferbloat.net/projects/bloat/wiki/Tests_for_Bufferbloat/
- Free web tests that show latency under load: Waveform Bufferbloat Test, Speedtest.net, Cloudflare Speed Test, LibreQoS test. The project calls Flent's RRUL test the best. Same link.
- The IETF is standardising a "Responsiveness Test" that reports round-trips per minute (RPM) while the line is loaded. It is still a draft (version 09, July 2026), not an RFC. https://datatracker.ietf.org/doc/draft-ietf-ippm-responsiveness/ For RPM, about 100 is poor and a few thousand is good. https://www.bufferbloat.net/projects/bloat/wiki/Tests_for_Bufferbloat/
- **How the app could do it (inference):**
  1. Ping the router and an internet target for 10 s while the line is idle.
  2. Keep pinging while the player runs a free web test (or while a big Steam or Windows download is already running), and plot both.
  3. Report "idle ping X ms, busy ping Y ms" and the increase. Router ping that stays flat while internet ping climbs is the bufferbloat signature.
  - Having the app generate the load itself needs a free download and upload server. Cloudflare's speed test is one option, but its terms for third-party use were not checked (gap).

### What fixes it

- The cure is on the router: Smart Queue Management (SQM) with cake, fq_codel or PIE. Measure the line, then set the SQM limits to about those speeds. https://www.bufferbloat.net/projects/bloat/wiki/What_can_I_do_about_Bufferbloat/ ; FQ-CoDel is RFC 8290. https://www.rfc-editor.org/rfc/rfc8290 †
- Firmware that has it: OpenWrt (luci-app-sqm), Asuswrt-Merlin with CakeQOS-Merlin, pfSense, OPNsense. Same bufferbloat.net link.
- **What the PC can add (inference):** stop or cap its own big transfers (next section but one), and cap a named app's uploads below the line speed with a QoS throttle policy (see "Windows QoS"). That keeps the upstream queue short only for the PC's own traffic. Other devices at home still fill it.
- **Tier:** stopping the PC's own transfers during play is **proven** whenever a transfer is running (the mechanism above). Router SQM is outside the app; the app should explain it, not set it.

## Wi-Fi versus Ethernet

### Evidence

- Riot's connection troubleshooting puts a wired connection among the first steps. https://support.riotgames.com/en-us/riot/performance/troubleshoot-pc-connection †
- RTINGS measured Wi-Fi latency in their router tests and found that Wi-Fi adds latency and variation that a "gaming router" doesn't remove. https://www.rtings.com/router/learn/research/wireless-latency † , https://www.rtings.com/router/learn/research/router-latency † **(measured test, not peer-reviewed)**
- Mechanism: Wi-Fi is shared air. Devices take turns, frames get lost to interference and are sent again, and the adapter scans and sleeps. Each of these adds uneven delay; a cable has none of them **(inference)**. RFC 8325 describes Wi-Fi's contention-based access and its priority queues. https://www.rfc-editor.org/rfc/rfc8325 †
- **Tier: proven.** It is the single most useful thing the app can tell a Wi-Fi player.

### Detect and measure

- Link type: `GetIfTable2`/`MIB_IF_ROW2` gives each adapter's `Type` (IEEE 802.11 for Wi-Fi) and its live byte counters. https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2 †
- Wi-Fi details: `WlanQueryInterface` gives the current connection, including signal quality and the link's PHY rate. https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanqueryinterface †
- The proof for the player: router ping on Wi-Fi with spikes, next to a flat router ping on a cable **(inference)**.

### Wi-Fi power saving

- Windows has a power plan setting, Wireless Adapter Settings → Power Saving Mode, with Maximum Performance, Low, Medium and Maximum Power Saving. Intel documents how its adapters use it. https://www.intel.com/content/www/us/en/support/articles/000005879.html †
- In 802.11 power save, the access point holds frames for a sleeping client until it wakes, which adds delay to incoming packets **(inference from the standard's design)**.
- **Tier: situational.** It matters most on laptops on battery, where Windows picks the power-saving values **(inference)**. Setting "Maximum Performance" for the plugged-in and battery values is a power-plan value, the same kind of change v1's `PowerSettingStep` already makes. It is instant and reversible, so it fits a per-game profile **(inference)**.

### Wi-Fi background scans

- Windows' native Wi-Fi model has a "media streaming" mode meant for low-latency traffic; apps turn it on with `WlanSetInterface` and `wlan_intf_opcode_media_streaming_mode`. https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetinterface † ; driver side: https://learn.microsoft.com/en-us/previous-versions/windows/hardware/wireless/native-802-11-media-streaming †
- What modern (WDI) Wi-Fi drivers do with it today is not documented in what I could read (gap). The popular trick of turning off WLAN AutoConfig to stop scans **(community)** also stops roaming and reconnects, so it should not ship **(inference)**.

## Network adapter power saving and Energy Efficient Ethernet

### "Allow the computer to turn off this device to save power"

- Microsoft explains this Power Management tab setting for network adapters. https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/power-management-on-network-adapter † It is about letting Windows power the adapter down when the PC sleeps or the link is idle, not about how fast packets move while a game is running **(inference)**.
- **Tier: possible placebo** for ping. It can matter for "network drops after sleep" problems, which are a different fix **(inference)**.

### Energy Efficient Ethernet (EEE, IEEE 802.3az)

- EEE lets a wired link drop into low-power idle between packets and wake when a packet comes. Intel lists it among its adapters' power options. https://edc.intel.com/content/www/us/en/design/products/ethernet/adapters-and-devices-user-guide/other-power-options †
- Waking the link takes microseconds (the 802.3az wake times are in the tens of microseconds) **(inference from the standard)**. Internet ping is measured in milliseconds, so the effect on ping can't be seen. https://grouper.ieee.org/groups/802/3/time_adhoc/public/apr09/bennett_01_0509.pdf †
- Dell's support article covers EEE / "Green Ethernet" causing link problems with some switches, fixed by turning it off. https://www.dell.com/support/kbdoc/en-us/000134483/ongelmien-ratkaiseminen-energiatehokas-ethernet-eee-or-green-ethernet?lang=en †
- **Tier: possible placebo** for ping; **situational** as a fix for a link that drops or renegotiates.
- **Not instant:** changing an adapter's advanced property restarts the adapter unless the caller asks it not to, so the link drops for a few seconds. https://learn.microsoft.com/en-us/powershell/module/netadapter/set-netadapteradvancedproperty † So it must not go in a per-game profile, which applies as the game starts **(inference)**. It is reversible: the app saves the old value, as the existing interrupt-moderation tweak does.

## Windows QoS: DSCP marking and throttling

### What Windows offers

- Policy-based QoS lets Windows "prioritize or manage the sending rate for outbound network traffic", matched by application, address, port or protocol. Priority is a DSCP value; sending rate is a throttle rate. https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/dd759093(v=ws.11)
- Throttling limits "outgoing network traffic to a specified rate". https://learn.microsoft.com/ru-ru/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/hh831689(v=ws.11)
- `New-NetQosPolicy` makes such a policy locally: match by `-AppPathNameMatchCondition` (an .exe), protocol, port or destination; act with `-DSCPAction` (0–63), `-ThrottleRateActionBitsPerSecond` or `-PriorityValue8021Action`. `-NetworkProfile` defaults to all networks. https://learn.microsoft.com/en-us/powershell/module/netqos/new-netqospolicy
- **Useful detail:** a policy put in `-PolicyStore ActiveStore` "does not persist after restart". Same link. That suits a temporary change from a per-game profile: even after a crash, a reboot clears it **(inference)**.
- Games can't easily mark their own packets. In the QoS2 API, setting the outgoing DSCP value needs administrator rights. https://learn.microsoft.com/en-us/windows/win32/api/qos2/ne-qos2-qos_set_flow † The plain `IP_TOS` socket option is not honoured on Windows. https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options †

### The home-PC catch

- Windows can apply QoS policies only on domain networks. Microsoft's event 16512 says a local setting can turn that off: "Selective application of QoS policies based on domain or non-domain network category has been disabled on this machine. QoS policies will be applied to all network interfaces." https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc727805(v=ws.10)
- That setting is the DWORD `Do not use NLA` = 1 under `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\QoS`, followed by a restart. **(community: Cisco's guide, not Microsoft)** https://www.cisco.com/c/nl_nl/support/docs/quality-of-service-qos/qos-configuration-monitoring/221868-enable-dscp-qos-tagging-on-windows-machi.html
- Whether a policy made with `New-NetQosPolicy` marks packets on a Windows 11 home PC without that value is not settled in any source I read (gap). It needs a test with `pktmon` on a real PC.

### Does anything honour DSCP?

- **Most home routers:** no, unless SQM with DiffServ classes is on (for example cake's `diffserv4` mode) **(inference; OpenWrt's SQM docs not checked)**.
- **The internet:** ISPs often clear or rewrite DSCP. A measurement study of real paths found DSCP values rewritten along many paths, often at the edge of networks. https://dl.ifip.org/db/conf/tma/tma2017/mnm2017_paper13.pdf † **(peer-reviewed, 2017)**
- **Wi-Fi:** Wi-Fi has four priority queues (WMM access categories). RFC 8325 recommends how access points and clients should map DSCP to them, which could give game packets a better turn on the air. https://www.rfc-editor.org/rfc/rfc8325 † Whether Windows' Wi-Fi drivers map the PC's DSCP marks this way is not documented (gap).
- **Tier: possible placebo** for DSCP on most home setups. **Situational** where the player's router runs SQM with DiffServ classes.
- **Throttling is different:** capping a background app's uploads works on the PC itself and needs no router help. **Situational** (it helps only when that app is uploading). The open question is whether a new policy affects connections already open (gap).
- **Reversible:** yes, `Remove-NetQosPolicy`, or a restart for ActiveStore policies. **Instant:** the policy is; the `Do not use NLA` value needs a restart.

## DNS

- DNS turns a name into an address before a connection starts. https://www.rfc-editor.org/rfc/rfc1034 † Once a match is running, the game sends packets to an address; DNS is not involved in each packet, so changing DNS can't change in-match ping **(inference)**.
- What DNS can change:
  - **How fast a name lookup is.** That costs milliseconds once per name, and Windows caches answers **(inference)**.
  - **Which server a content network picks for downloads.** Some resolvers pass part of the player's address to content networks (EDNS Client Subnet, RFC 7871), and some don't, which can lead to a farther download server. https://www.rfc-editor.org/rfc/rfc7871 † Cloudflare's 1.1.1.1 doesn't send it, for privacy. https://conference.apnic.net/46/assets/files/APNC402/DNS-resolver-1.1.1.1-from-Cloudflare.pdf †
  - **Encryption.** Windows 11 can use DNS over HTTPS. That is privacy, not speed. https://learn.microsoft.com/en-us/windows-server/networking/dns/dns-encryption-dns-over-https †
- Match servers are usually chosen by measured ping to data centres (as SDR does), not by DNS location. https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay?language=english **(inference that this is typical beyond Steam)**
- **Tier: possible placebo** for online play. If the app ever offers a DNS change, it belongs under privacy, not gaming. Reversible and instant: save the adapter's DNS server list and put it back **(inference)**. v1's repair tool already flushes the DNS cache.

## Background downloads and uploads during play

### Why it matters

A big transfer on the same line both takes bandwidth and fills the router's queue (see Bufferbloat). Uploads matter as much as downloads: home lines have far less upload than download, so a cloud backup can fill the upstream queue on its own **(inference)**.

### Detect

- The ETW feed that finds the game server also gives bytes sent and received per process. The app can show "using your connection right now: OneDrive 4 MB/s up" during play **(inference)**. Adapter-wide counters come from `MIB_IF_ROW2` (`InOctets`, `OutOctets`). https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2 †

### Each source, and how to pause or limit it reversibly

| Source | Built-in control | What the app could do | Instant? |
|---|---|---|---|
| **Windows Update / Delivery Optimization (DO)** | Absolute caps `DOMaxBackgroundDownloadBandwidth` / `DOMaxForegroundDownloadBandwidth` (KB/s); percentage caps `DOPercentageMaxBackgroundBandwidth` / `...Foreground...`; business-hours versions. Default 0 means "dynamically adjusts". LAN peers are not limited by these. Upload to internet peers is capped at 20 GB a month by default. https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference | Set a background cap as a reversible tweak; v1 already has the 35-day pause and peering off (download mode 0), which stops DO uploads to other PCs. | Not documented whether a cap reaches a running download (gap). |
| **OneDrive** | Policies: `UploadBandwidthLimit` and `DownloadBandwidthLimit` in KB/s (HKCU, 50–100000), or `AutomaticUploadBandwidthPercentage` (HKLM, 10–99%). OneDrive.exe must restart to apply the fixed limits. OneDrive pauses itself on metered networks and in battery saver unless a policy says not to. https://learn.microsoft.com/en-us/sharepoint/use-group-policy Users can also pause sync for 2, 8 or 24 hours from the tray. https://support.microsoft.com/en-au/onedrive/how-to-pause-and-resume-onedrive-sync † | Close OneDrive in a per-game profile and start it again after (already a v1 feature), or set an upload cap as a reversible tweak. | Closing: yes. Caps: need a OneDrive restart. |
| **Metered connection** | Set per network in Settings → Network & internet → Wi-Fi or Ethernet. Then "Windows Update will only download priority updates", and Microsoft Store downloads "might be paused". https://support.microsoft.com/en-us/windows/metered-connections-in-windows-7b33928f-a144-b265-97b6-f2e95a87c408 OneDrive pauses itself too (OneDrive link above). | Possible, but it is broad, it is per network, and other apps read it too. Better offered as advice than as a per-game switch **(inference)**. | Yes (per Microsoft, it is a toggle). |
| **Steam** | Settings → Downloads: "Allow downloads during gameplay" (off means Steam pauses downloads while a game runs), a bandwidth limit, and an auto-update schedule. https://help.steampowered.com/en/faqs/view/71AB-698D-57EB-178C † ; download limit: https://help.steampowered.com/faqs/view/163C-7C89-406E-2F63 † ; the gameplay option arrived as a client update **(community)** https://pcgamer.com/steam-update-allows-background-downloading-while-playing-a-game | Read-only check and advice. Editing Steam's own config files is fragile and Steam may overwrite them **(inference)**. | — |
| **Epic Games Launcher** | Settings → throttle downloads (a download speed limit). https://www.epicgames.com/help/epic-games-store-c-202300000001639/a202300000015320 † | Advice, or close the launcher in a per-game profile when the game doesn't need it. | Closing: yes. |
| **Battle.net** | App settings → Downloads → "Limit download bandwidth". https://support.blizzard.com/en/article/43662 † | Advice. Battle.net games need the app running, so don't close it **(inference)**. | — |
| **Other launchers and cloud backup** (EA app, Ubisoft Connect, Xbox app, Google Drive, Dropbox, backup tools) | Each has its own settings; not checked in this pass (gap). | Close in a per-game profile, or cap uploads with a QoS throttle policy per .exe (see above). | Closing: yes. QoS policy: yes for new traffic (gap for running transfers). |

### Recommended approach (inference)

1. **Show** what is using the line during play (ETW per-process bytes). Showing it is read-only and safe.
2. **Pause by closing** in the per-game profile, which v1 already does. It is instant, and it is undone when the game closes.
3. **Cap** what can't be closed with reversible tweaks: a DO background cap, a OneDrive upload cap, or a QoS throttle policy on a named .exe in `ActiveStore` for the length of the game.
4. **Advise** for launchers whose settings the app can't safely write (Steam, Battle.net, Epic).

## Anti-cheat

- The anti-cheat research found no first-party page that names network adapter settings, QoS policies, DNS, ping tools or update pausing as a problem. The documented problems are boot and kernel trust settings, vulnerable drivers, modified system files and tools that touch game memory. See `research/anti-cheat-requirements.md` on branch `claude/project-thread-5ggkan`.
- Riot blocks game-memory access by unknown third-party apps since 6 October 2026, and says this is not a ban. https://support.riotgames.com/ar-ae/riot/performance/game-memory-access-removed-for-third-party-apps Nothing in this file reads game memory **(inference)**.
- **Safe by design (inference):** ICMP pings, ETW kernel network events (OS telemetry about all processes), reading the TCP/UDP tables, QoS policies, power-plan values, launcher advice.
- **Avoid (inference):**
  - Installing a packet-capture driver (such as Npcap). Anti-cheats block some third-party kernel drivers, and Windows already has ETW and `pktmon`.
  - Sending UDP probes to game servers.
  - Firewall rules that block relays or servers to "choose" a server (a community trick for SDR games). It can break matchmaking, and no publisher documents it.
  - Anything that injects into or reads the game process, including its overlay numbers.
- Under the standing rule, any conflict found later warns, never blocks.

## Summary table

| Item | Tier | Detect / measure | Reversible? | Instant? (per-game profile) | Anti-cheat |
|---|---|---|---|---|---|
| Ping, jitter, loss to router, internet, game server | Measurement | ICMP + ETW server discovery; fall-backs when ICMP is dropped | Read-only | Yes | None found |
| Ethernet instead of Wi-Fi | **Proven** | Adapter type; router ping spikes on Wi-Fi | Player's action | — | None |
| Stop the PC's own big transfers during play | **Proven** (when one runs) | ETW bytes per process | Yes (close, then restart) | Yes | None found |
| Bufferbloat check (idle vs busy ping) | Measurement | Ping during a load | Read-only | — | None |
| Router SQM | **Proven**, but outside the app | Bufferbloat check | — | — | None |
| Wi-Fi power saving → Maximum Performance | Situational (laptops, battery) | Power plan value | Yes (old values saved) | Yes | None found |
| DO bandwidth cap | Situational | DO settings; ETW bytes | Yes | Unclear for a running download | None found |
| OneDrive upload/download cap | Situational | Policy values; ETW bytes | Yes | Needs OneDrive restart | None found |
| QoS throttle on a named app | Situational | Policy list; ETW bytes | Yes (ActiveStore clears at restart) | Yes for the policy | None found |
| QoS DSCP marking for the game | **Possible placebo** | `pktmon` capture to see the mark | Yes | Policy yes; `Do not use NLA` needs restart | None found |
| Energy Efficient Ethernet off | **Possible placebo** (situational for link drops) | Adapter advanced property | Yes (old value saved) | **No** (adapter restarts) | None found |
| "Allow the computer to turn off this device" off | **Possible placebo** for ping | Power Management tab value | Yes | Unclear | None found |
| Changing DNS server | **Possible placebo** | Lookup time only | Yes | Yes | None found |

## Gaps

- Whether `New-NetQosPolicy` DSCP marks appear on the wire on a Windows 11 home PC with and without `Do not use NLA`. Test with `pktmon`.
- Whether Windows Wi-Fi drivers turn DSCP into WMM priority (RFC 8325), and what WDI drivers do with media streaming mode.
- Whether DO caps and QoS throttle policies reach transfers already running.
- First-party pages for the CS2, Valorant and Overwatch network overlays (only community pages found), and current Steam, Epic and Battle.net help text (the Steam help pages render only in a browser).
- A free, allowed server for the app to create its own test load for a bufferbloat check.
- A peer-reviewed, recent measurement of Wi-Fi versus Ethernet jitter for game traffic. RTINGS' tests are the best found.

## Sources

Every link cited above, in order of first use. This list was compiled from the body after the research session was cut off; **†** marks in the body still apply.

- https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/92
- https://www.rfc-editor.org/rfc/rfc2681
- https://www.rfc-editor.org/rfc/rfc5481
- https://www.rfc-editor.org/rfc/rfc3550#section-6.4.1
- https://www.rfc-editor.org/rfc/rfc7680
- https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getipforwardtable2
- https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable
- https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcprow_owner_pid
- https://learn.microsoft.com/en-us/windows/win32/api/udpmib/ns-udpmib-mib_udprow_owner_pid
- https://learn.microsoft.com/en-us/windows/win32/etw/udpip
- https://learn.microsoft.com/en-us/windows/win32/etw/udpip-typegroup2
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/pktmon
- https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracea
- https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay?language=english
- https://learn.microsoft.com/es-es/windows/win32/api/icmpapi/nf-icmpapi-icmpsendecho2
- https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.ping
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/pathping
- https://learn.microsoft.com/en-us/windows/win32/api/tcpestats/ns-tcpestats-tcp_estats_path_rod_v0
- https://learn.microsoft.com/en-us/windows/win32/api/tcpestats/ne-tcpestats-tcp_estats_type
- https://archive.nanog.org/meetings/nanog47/presentations/Sunday/RAS_Traceroute_N47_Sun.pdf
- https://www.rfc-editor.org/rfc/rfc4443#section-2.4
- https://www.epicgames.com/help/fortnite-battle-royale-c-202300000001636/technical-support-c-202300000001719/fortnite-a202300000012783
- https://gameriv.com/counter-strike-2s-new-telemetry-settings-will-give-you-all-the-benefits-from-csgos-net_graph-1/
- https://cyberpost.co/what-is-ctrl-shift-n-in-overwatch-2/
- https://prosettings.net/blog/how-to-show-fps-and-ping-in-valorant
- https://www.bufferbloat.net/projects/bloat/wiki/What_can_I_do_about_Bufferbloat/
- https://www.rfc-editor.org/rfc/rfc7567
- https://www.bufferbloat.net/projects/bloat/wiki/Tests_for_Bufferbloat/
- https://datatracker.ietf.org/doc/draft-ietf-ippm-responsiveness/
- https://www.rfc-editor.org/rfc/rfc8290
- https://support.riotgames.com/en-us/riot/performance/troubleshoot-pc-connection
- https://www.rtings.com/router/learn/research/wireless-latency
- https://www.rtings.com/router/learn/research/router-latency
- https://www.rfc-editor.org/rfc/rfc8325
- https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2
- https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanqueryinterface
- https://www.intel.com/content/www/us/en/support/articles/000005879.html
- https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetinterface
- https://learn.microsoft.com/en-us/previous-versions/windows/hardware/wireless/native-802-11-media-streaming
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/power-management-on-network-adapter
- https://edc.intel.com/content/www/us/en/design/products/ethernet/adapters-and-devices-user-guide/other-power-options
- https://grouper.ieee.org/groups/802/3/time_adhoc/public/apr09/bennett_01_0509.pdf
- https://www.dell.com/support/kbdoc/en-us/000134483/ongelmien-ratkaiseminen-energiatehokas-ethernet-eee-or-green-ethernet?lang=en
- https://learn.microsoft.com/en-us/powershell/module/netadapter/set-netadapteradvancedproperty
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/dd759093(v=ws.11
- https://learn.microsoft.com/ru-ru/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/hh831689(v=ws.11
- https://learn.microsoft.com/en-us/powershell/module/netqos/new-netqospolicy
- https://learn.microsoft.com/en-us/windows/win32/api/qos2/ne-qos2-qos_set_flow
- https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options
- https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc727805(v=ws.10
- https://www.cisco.com/c/nl_nl/support/docs/quality-of-service-qos/qos-configuration-monitoring/221868-enable-dscp-qos-tagging-on-windows-machi.html
- https://dl.ifip.org/db/conf/tma/tma2017/mnm2017_paper13.pdf
- https://www.rfc-editor.org/rfc/rfc1034
- https://www.rfc-editor.org/rfc/rfc7871
- https://conference.apnic.net/46/assets/files/APNC402/DNS-resolver-1.1.1.1-from-Cloudflare.pdf
- https://learn.microsoft.com/en-us/windows-server/networking/dns/dns-encryption-dns-over-https
- https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference
- https://learn.microsoft.com/en-us/sharepoint/use-group-policy
- https://support.microsoft.com/en-au/onedrive/how-to-pause-and-resume-onedrive-sync
- https://support.microsoft.com/en-us/windows/metered-connections-in-windows-7b33928f-a144-b265-97b6-f2e95a87c408
- https://help.steampowered.com/en/faqs/view/71AB-698D-57EB-178C
- https://help.steampowered.com/faqs/view/163C-7C89-406E-2F63
- https://pcgamer.com/steam-update-allows-background-downloading-while-playing-a-game
- https://www.epicgames.com/help/epic-games-store-c-202300000001639/a202300000015320
- https://support.blizzard.com/en/article/43662
- https://support.riotgames.com/ar-ae/riot/performance/game-memory-access-removed-for-third-party-apps
