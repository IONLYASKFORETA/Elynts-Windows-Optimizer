# Review the app's look for improvements

Ticket: https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer/issues/100

Researched 2026-11-07. I looked at the screenshot tour of main (branch `screenshots/main`, commit `fdeb92b13ac53445f52ae393f9581d2c1acf63f1`, which shoots app commit `c1cb600`, the app code main has today). I also read the window's XAML and view models, measured the theme colours against WCAG, and did a few web reads: Microsoft's design guidance, W3C, and vendor pages for the NVIDIA App, AMD Software, Hone and Windows 11 Settings. No app was run, because this session has no Windows PC. Anything from a secondary or community source is marked **(community)**. My own judgement, which is most of this review, is marked **(inference)**. Screenshots are named by folder: `default/` is 1600×1000, `smallest/` is 900×520 and `wide/` is 2560×1400. A `.screen.png` file includes the title bar. Nothing here proposes changing the black background, the red accents, the accent wave or layout A.

## Answer

- **Give the app its own icon.** The title bar, taskbar and tray all show Windows' blank default icon (`default/01-home.screen.png`, and `TrayNotes.cs` uses `SystemIcons.Application`). The red "E" mark from the sidebar would fix all three. Small change, seen every day.
- **Use solid red for one thing per screen.** Startup shows five bright red "Delete…" buttons, louder than the switches beside them. Cleanup has two red buttons side by side, and Undo history has two as well. Fluent 2 says to use only one primary button per layout. Making Delete, Scan and Restore everything plain buttons lets the red that stays mean "next step" or "danger". Small.
- **Say clearly when the service isn't connected.** The code shows that while the service is down or not set up, every tweak page reads "Nothing here yet. This section arrives in a later build." The real reason sits in small grey text at the foot of the sidebar. The tour never shoots this state, so this comes from the code **(inference)**. A full-page message with a Retry or Set up button, like the existing "Administrator accounts only" page, would fix the worst first-run moment. Small.
- **Make the small text readable.** Section labels ("WHAT IT DOES", "WAITING") and table headings are 11 px at 3.3–3.8:1 contrast, and WCAG asks for 4.5:1. Pills are 11 px, but Windows' smallest regular text size is 12 px. White text on a primary button falls to 2.6:1 whenever the wave turns orange behind it. One colour token and two sizes fix most of this. The button text touches the wave, so that part is Aden's call.
- **Show what each tweak does on its row.** A row shows only a name and pills. To learn what a tweak does you must click it and read the detail pane. A one-line summary under the name, as Windows Settings cards have, would let people scan the 31 Debloat tweaks without clicking each one. Medium.
- **Group the sidebar.** It has 14 items in one list, and Microsoft suggests 5–10 top-level items, with headers to group them. Group them as Tweaks, Tools and Tuning, and put Per-game profiles next to Gaming. Small.
- **Add search where lists are long.** That means the catalogue of about 100 apps, the tweak lists, and an app-wide "find a tweak or tool" box. Windows Settings and AMD Software both have one. Medium.
- **Pick one width rule for wide screens.** Some pages stop at 760 px, one stops at 960 px, and the rest stretch to about 1,850 px. At 2560 a switch sits about 1,400 px from its name. Capping list pages at about 1,100–1,200 px and using two columns on Overclocking would fix both problems. Medium.
- **Show long jobs better.** A driver install shows only text. On Repair the Run button and its progress can sit below a long list of commands, off screen. Once you leave a page, nothing shows that a job is still running. Add progress bars, move Run to the top, mark the running row and its sidebar item, and show progress on the taskbar button. Small to medium.
- **Make Per-game profiles game-first.** You pick the game from a dropdown, and the "Installed games" list below it can't be clicked. A clickable list of games with a "has a profile" mark, like Hone's game tiles or the NVIDIA App's program list, fits layout A. Medium to large.
- **All six known follow-ups are confirmed** (see "Known follow-ups"). The quickest are: replace the protected services' empty dropdown with a lock note, put hint text in the services filter, and mark installed apps in the catalogue.

## Every finding

"Fits" means it fits the black-and-red look.

| # | Page | Problem or idea | Why it matters | Size | Fits |
|---|---|---|---|---|---|
| 1 | Whole app | Windows' blank default icon in the title bar, taskbar and tray | First thing anyone sees; looks unfinished | small | yes |
| 2 | Startup | Five solid-red "Delete…" buttons beside red switches | The action that can't be undone is the loudest thing on the page | small | yes |
| 3 | Cleanup | "Scan" and "Clean ticked" are both red | No clear next step | small | yes |
| 4 | Undo history | "Restore everything" and "Re-apply all" are both red | Undoing everything looks like the main job of the page | small | yes |
| 5 | Every page, while disconnected | Pages say "This section arrives in a later build"; the reason is only in the sidebar footer | First-run users think the app is empty or broken | small | yes |
| 6 | Whole app | Faint text (labels, table headings) at 3.3–3.8:1; 11 px labels and pills | Below WCAG AA and Windows' 12 px minimum | small | yes |
| 7 | Whole app | White button text falls to 2.6:1 when the wave is orange behind it | Button labels are hard to read part of the time | small | partly |
| 8 | Tweak pages | The off switch's outline is 1.8:1; a locked switch is a dull maroon (1.5:1 against its card) | Hard to tell on, off and locked apart | small | yes |
| 9 | Tweak pages | The "Destructive" and "Can't be undone" pills look like "Reversible" | The most important warning is the quietest | small | yes |
| 10 | Tweak pages | No one-line description on rows | You must click each row to learn what it does | medium | yes |
| 11 | Gaming, Windows Update | Pick-one tweaks shown as separate switches (High vs Ultimate power plan; updates 7 vs 30 days late) | Invites turning on two that fight | medium | yes |
| 12 | Debloat & Privacy | 31 tweaks in one list with no groups | Slow to scan | medium | yes |
| 13 | Sidebar | 14 items, no groups; Per-game profiles far from Gaming; at 900×520 four items scroll out of sight | Slower to find things; a gaming-first feature is buried | small | yes |
| 14 | Whole app | No app-wide search | Can't jump to a tweak or tool by name | medium | yes |
| 15 | Whole app, wide | Three width rules (760 px, 960 px, no limit); at 2560 switches sit 1,400+ px from their names | The eye has to travel far; half of some pages is empty | medium | yes |
| 16 | Home | Tool sections show "Tools" over an empty meter | An empty bar reads as "0 %" | small to medium | yes |
| 17 | Home | Restart shown twice; its row links to Undo history, with no "Restart now" | Extra steps to finish a change | small | yes |
| 18 | Home | Nothing suggests what to do next on a new PC | Home shows the PC's state but not the next step | medium | yes |
| 19 | Home | Re-reads every tweak and the hardware every time it is shown | Home is slower than it needs to be (known follow-up) | small | yes |
| 20 | Apps | The catalogue doesn't mark installed apps or apps with an update waiting | You may tick an app you already have (known follow-up) | small | yes |
| 21 | Apps | The catalogue of about 100 apps has no filter; three rows fit at 900×520 | Long scrolling | small | yes |
| 22 | Apps | Install buttons are red and enabled with nothing ticked, and don't say how many apps | Unclear what pressing them will do | small | yes |
| 23 | Apps | The winget result count appears at the very bottom, far from the search box | The answer is far from the question | small | yes |
| 24 | Services | Protected services show an empty, greyed dropdown plus greyed Set and Undo | Looks broken (known follow-up) | small | yes |
| 25 | Services | The filter box has no hint text and no accessible name | Looks like an empty bar (known follow-up) | small | yes |
| 26 | Services | Every row has a greyed Undo; the Advanced switch hides in the table header | Clutter; Advanced is hard to find | small | yes |
| 27 | Cleanup | The category list is a short scroll box (1.5 rows at 900×520) while the page has empty space | Hard to check what will be deleted | small | yes |
| 28 | Cleanup | The button doesn't say how much will be freed; no progress bar | "Clean 1.7 GB" says more than "Clean ticked" | small | yes |
| 29 | Repair | Run and its progress sit below the command list (off screen for game runtimes) | The main action is hidden | small | yes |
| 30 | Repair | Rows don't show running, done or failed, or how long a tool takes | You can't see a tool's status from the list | small | yes |
| 31 | Drivers | "Download and install" is red before anyone has checked for a newer driver; four install options always show | Encourages blind installs; busy cards | small | yes |
| 32 | Drivers | Install progress is text only | A job of several minutes has no bar | small | yes |
| 33 | Long jobs | No running mark on the sidebar or the taskbar button | Leave the page and you lose track of the job | small | yes |
| 34 | Overclocking | The column stops at 760 px; at 2560 about two-thirds of the page is empty | Wasted room and more scrolling (known follow-up) | medium | yes |
| 35 | Overclocking | Slider fill starts at the far left, so defaults look "set" or "maxed" | Misleading for offsets and caps | small | yes |
| 36 | Overclocking | No way to type an exact value; range text wraps onto three lines | Hard to hit 1 MHz steps by dragging | small | yes |
| 37 | Per-game profiles | The game is picked from a dropdown; the installed-games list can't be clicked | Not game-first | medium to large | yes (partly with cover art) |
| 38 | Per-game profiles | Background apps show as full file paths; Remove needs a selection first | Hard to read | small | yes |
| 39 | Undo history | The table isn't virtualised; every row shows a full timestamp; no grouping or filter | Slow and hard to scan after months of use (known follow-up) | small to medium | yes |
| 40 | Undo history | A whole warning paragraph is in bold orange | Hard to read | small | yes |
| 41 | Security | The Defender steps are a 14-line paragraph with "1) 2) 3)" written inline | Hard to follow while switching windows | small | yes |
| 42 | Confirm dialog | Yes/No buttons; the message repeats the title; square corners | Microsoft asks for specific verbs and no repeated title; Windows 11 rounds dialogs | small | yes |
| 43 | Settings | No About section (version, links, app log); tray-note switches live on three other pages | You can't find the version; settings are scattered | small | yes |

## What already works (keep it)

- The evidence pills, the "Possible placebo" fold and the detail pane's "Exact change" and "Undo" sections are clear and honest (`default/02-gaming-fullest-tweak.png`) **(inference)**.
- Anti-cheat and "can't be undone" boxes in the detail pane use the orange and red tints well (`default/06-windows-update.png`, `default/03-debloat-privacy-locked-can-t-be-undone.png`) **(inference)**.
- Compact mode works: at 900×520 every switch and button stays visible with no sideways scrolling (`smallest/02-gaming.png`, `smallest/07-startup-services-services.png`).
- The confirm dialog makes the safe choice the default and answers Escape with it (`ConfirmDialog.xaml.cs`). Microsoft also asks for a safe button in every dialog ([Dialogs](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)).

## Across the whole app

**App icon (finding 1).** In `default/01-home.screen.png` the title bar shows Windows' generic window icon, and the tray icon is `System.Drawing.SystemIcons.Application`. The project sets no `ApplicationIcon`. An .ico of the red "E" mark would cover the window, taskbar, tray and Start menu **(inference)**.

**Red as the one strong colour (findings 2–4).** Fluent 2: "Only use one primary button in a layout for the most important action." It also says that when many actions are equally minor, use subtle or outline buttons "to avoid a busy layout" ([Fluent 2 button](https://fluent2.microsoft.design/components/web/react/core/button/usage)). The app has three cases:
- Startup (`default/07-startup-services.png`): the five red "Delete…" buttons are `Button.Destructive`. Make them plain buttons, or move Delete into a "…" menu. The confirmation dialog already guards the action **(inference)**.
- Cleanup (`default/08-cleanup.png`): "Scan" and "Clean ticked" are both `Button.Primary`. Make Scan plain ("Scan again" after a scan) **(inference)**.
- Undo history (`default/13-undo-history.png`): "Restore everything" is solid red at the top right, and "Re-apply all" is red below it. Restoring everything is rare, so make it plain and keep its confirmation **(inference)**.

**Disconnected and first-run state (finding 5).** `MainViewModel.IsEmpty` shows "Nothing here yet. This section arrives in a later build." on any tweak page with no tweaks. Tweaks only load once the service connects, so this text shows while the service is down, still being set up, or after someone says No to the UAC prompt. The real message ("The background service isn't set up… press Retry and choose Yes") is 11.5 px dim text in the sidebar footer. The "later build" text is also out of date, since v1 ships every section. Show a page-sized message instead, like the refused page (`default/refused-standard-account.png`). It should give the status in plain words plus a Retry or "Set up the service" button. The tour should shoot it **(inference)**.

**Readability (findings 6–8).** WCAG 1.4.3 asks for 4.5:1 for normal text and 3:1 for large text ([W3C 1.4.3](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum)). WCAG 1.4.11 asks for 3:1 for the parts of a control that show its state ([W3C 1.4.11](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html)). Disabled controls are exempt from both. Windows' type ramp puts the smallest text at "12px Regular" and body text at 14 px ([Typography](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)). I computed these ratios from the `Theme.xaml` colour values:

| What | Colours | Ratio | Needs |
|---|---|---|---|
| `Text.Label` headings, table headings, "ELYNT'S" (11 px) | `TextFaint` #6B6B75 on Bg / Surface / Surface2 | 3.78 / 3.58 / 3.32 | 4.5 |
| Proposed `TextFaint` #85858F | same backgrounds | 5.45 / 5.16 / 4.79 | 4.5 |
| White label on a primary button, wave at red | white on #E5162D | 4.68 | 4.5 |
| Same, halfway through the wave / at orange | white on #F24824 / #FF7A1A | 3.67 / 2.61 | 4.5 |
| Off switch outline | `StrokeStrong` #3F3F46 on Surface | 1.80 | 3 |
| Off switch knob | #8A8A93 on its track | 5.12 | 3 |
| Locked "on" switch (Opacity 0.4) | its fill on Surface | 1.51 | (exempt, but it still shows a real state) |
| Orange pill text ("Needs reboot") | orange on its tint | 6.33 | 4.5 |

- Raise `Theme.TextFaint` to about #85858F, and set `Text.Label` and pill text to 12 px. Pills may wrap a little more at 900 px, so check `smallest/` afterwards **(inference)**.
- Off switch: give the track edge #6B6B75 (3.6:1). The knob already carries the state **(inference)**.
- Locked switches ("Already set on this PC", `default/02-gaming-locked-already-set-on-this-pc.png`): drop the 0.4 opacity and show a small lock glyph instead. The state then stays readable, and the lock says why it can't be changed **(inference)**.
- Text on the wave: the wave is Aden's choice, so here are the options without changing it. (a) Keep the wave and add a soft dark shadow under button labels. (b) Keep the wave on the button's border or underline, with a still red face behind the text. (c) Give the button brush a deeper orange stop: #CC4A0A gives 4.6:1. (d) Accept it. My lean is (a) or (b), because both keep the wave exactly as it is **(inference)**.

**Sidebar (finding 13).** For a left pane, Microsoft recommends "5-10 equally important top-level navigation categories". It provides item headers "for labeling groups of items" and separators for grouping, and puts footer items before Settings ([NavigationView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview)). The app has 14 items. At 900×520 the folded sidebar scrolls, and Overclocking, Per-game profiles, Undo history and Settings drop out of view (`smallest/01-home.png`). A possible grouping, all under small `Text.Label` headers **(inference)**:
- Home.
- PLAY: Gaming, Per-game profiles, Overclocking, Drivers.
- WINDOWS: Debloat & Privacy, Security, Windows Update, Startup & Services.
- TOOLS: Apps, Cleanup, Repair.
- Footer: Undo history, Settings.

**Search (finding 14).** Windows Settings has a search bar and a Home of "interactive cards" ([Exploring Windows Settings](https://support.microsoft.com/en-us/windows/experience/exploring-windows-settings)). AMD's own help says to "search for 'Gaming'" inside AMD Software ([AMD profiles FAQ](https://www.amd.com/en/resources/support-articles/faqs/DH3-029.html)). The NVIDIA App sorts and filters its program list ([NVIDIA App article](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)). Hone filters its tweaks by System, Graphic, Internet and so on ([Hone getting started](https://support.hone.gg/en/articles/8833584-getting-started-with-hone)). A box at the top of the sidebar would search tweak names, repair tools and apps, and open the page with the row selected **(inference)**. It needs one shared search style in `Theme.xaml` (hint text and a search glyph), which the services filter and an Apps filter can reuse.

**Widths on big screens (finding 15).** Drivers, Overclocking, Settings and the refused page cap at 760 px. Per-game profiles caps at 960 px. Tweak lists, Apps, Startup, Services, Undo history and Home have no cap. At 2560 a tweak's switch is about 1,400 px from its name (`wide/02-gaming.png`), and Startup's Delete button sits about 2,100 px from the app name (`wide/07-startup-services.png`). Microsoft's line-length advice is "50–60 letters per line" ([Typography](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)). Suggested rule: rows and tables cap at about 1,100–1,200 px. Reading text stays near 760 px. Wide card pages (Home, Overclocking) use the extra room for a second column, not empty space **(inference)**.

**Long jobs (findings 29, 30, 32, 33).** Microsoft: use a determinate bar "during an operation whose duration is known", and an indeterminate one when it isn't known. Microsoft also says that "a line of text is still necessary" to explain what is under way ([Progress controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls)). Today:
- Repair has a bar.
- Apps show "Installing… 40%" on each row.
- Drivers show text only: `DriverInstallProgress` has a stage but no percent.
- Cleanup shows text only, although `CleanupProgress` has Index and Count, so a real bar is possible.

Add bars where the data exists, and stage text ("Step 2 of 4: checking the signature") where it doesn't. Add a small dot on the sidebar item of a page with a job running. Windows can also show progress on the taskbar button ([Taskbar extensions](https://learn.microsoft.com/en-ca/windows/win32/shell/taskbar-extensions)), and WPF exposes that through `Window.TaskbarItemInfo`. That helps a gamer who minimises the app during a 10–30 minute repair **(inference)**.

**Confirm dialog (finding 42).** `default/confirmation-destructive-tweak.png` has the title "This can't be undone", and the message starts with the same sentence. The buttons are "Yes" and "No". Microsoft: "Use specific responses to the main instruction or content as button text", and in the content "Don't repeat the title with slightly different wording" ([Dialogs](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)). Suggested: the title "Remove these apps for new accounts?", the message without the repeat, and the buttons "Remove apps" and "Cancel". The dialog is a borderless window with square corners. Windows 11 rounds standard WPF windows, but a custom frame may need `DwmSetWindowAttribute` with `DWMWCP_ROUND` ([Rounded corners](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners)). Whether that works for this window must be tried on Windows **(inference)**.

## Home

Screens: `default/01-home.png`, `smallest/01-home.png`, `wide/01-home.png`.

- **Tool sections (finding 16).** Apps, Startup & Services, Repair, Drivers and Overclocking show "Tools" over an empty meter (`HomeViewModel`: `Total == 0 ? "Tools"`). Hide the meter when there is no count. Better still, give each tool card a useful line: "2 app updates", "5 apps start at sign-in", "Drivers not checked yet", "GPU on default settings" **(inference)**.
- **Restart (finding 17).** The "1 change waiting for a restart" number and the "1 change takes effect after a restart" row say the same thing, and the row only links to Undo history. The app already has a restart-now flow after driver installs (`MainWindow.AskToReboot`). Offer the same "Restart now…" button on the row **(inference)**.
- **What next (finding 18).** Windows Settings' Home has a "Recommended Settings" card ([Exploring Windows Settings](https://support.microsoft.com/en-us/windows/experience/exploring-windows-settings)). AMD's HYPR-RX first offers to "detect and apply optimal game settings", and its bell icon lists recommendations ([AMD profiles FAQ](https://www.amd.com/en/resources/support-articles/faqs/DH3-029.html)). Hone suggests sets for "General Performance, Gaming, and Privacy" ([Hone](https://support.hone.gg/en/articles/8833584-getting-started-with-hone)). An honest version for Elynt is a "Worth a look" card. It would list the Proven tweaks that fit this PC and are off, plus checks not yet run (drivers, cleanup), each opening its page. It would show a list, not a score, which keeps the earlier "show no score" decision (`research/gaming-2/paid-gaming-boosters.md`) **(inference)**.
- **The dash.** "–" for "drivers not checked yet" is fine, but the tile could hold the "Check drivers" button itself, rather than a separate button row below **(inference)**.

## Tweak pages (Gaming, Debloat & Privacy, Security, Windows Update, Cleanup's tweak)

- **Descriptions on rows (finding 10).** Windows 11's own settings rows have a Header and a Description, with the control on the right ([SettingsCard](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/settingscontrols/settingscard)). The NVIDIA App shows a description behind an "i" icon ([NVIDIA App article](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)). Here, the row shows the name, pills and "On"/"Off" (`default/03-debloat-privacy.png`). Add one short line of dim text taken from the start of "What it does", or from a new short-summary field. Rows grow by about 18 px, so check `smallest/` **(inference)**. The "On"/"Off" word next to the pills repeats the switch and could go to make room **(inference)**.
- **Warnings that look calm (finding 9).** "Destructive" uses the plain pill with dim text, which looks just like "Reversible". "Can't be undone" is plain too (`default/03-debloat-privacy-locked-can-t-be-undone.png`). Add a `Pill.Danger` to `Theme.xaml` (RedTint fill, RedEdge border) for those two **(inference)**.
- **Pick-one choices (finding 11).** "Use the High performance power plan" and "Use the Ultimate Performance power plan" are separate switches. So are "Get monthly updates 7 days late" and "30 days late", and both write the same `DeferQualityUpdatesPeriodInDays` value (`WindowsUpdateTweaks.cs`). Microsoft: "Use radio buttons to let users select from two or more mutually exclusive options." For a binary choice, use a single toggle switch or check box ([Radio buttons](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/radio-button)). Show each pair as one row with a choice: "Power plan: Windows default / High performance / Ultimate". This needs an engine notion of a choice group, so it is medium **(inference)**. Per-game profiles has the same pair as check boxes (`default/12-per-game-profiles.png`).
- **Long lists (finding 12).** Debloat & Privacy lists 31 tweaks with no headers (`default/03-debloat-privacy.png`). Group them under `Text.Label` headers such as TELEMETRY, ADS & SUGGESTIONS, BUILT-IN APPS and AI FEATURES. The list already supports groups, as the placebo fold shows **(inference)**.
- **Security steps (finding 41).** The "Before you can turn this on" box for Defender is one 14-line paragraph with "1) … 2) … 3) …" written inline (`default/04-security-fullest-tweak.png`). Show it as a numbered list, with "Repair the Windows Security app" as a link **(inference)**.
- **Selected row off screen.** In `default/03-debloat-privacy-fullest-tweak.png` the detail pane shows a tweak whose row is scrolled out of view. This is probably the tour selecting without scrolling, so it is listed in Gaps rather than as a finding.

## Apps

Screens: `default/05-apps.png`, `default/05-apps-installed.png`, `default/05-apps-search-winget.png`, `smallest/05-apps.png`.

- **Installed apps (finding 20).** Firefox and Steam are on the Installed tab with versions, but in the catalogue they are plain, unticked rows. `AppItemViewModel` has no installed state. Match them by winget id and show an "Installed" pill or an orange "Update available" pill **(inference)**.
- **Filter (finding 21).** There are about 100 apps in 7 groups, and only three rows fit at 900×520. Add the shared search box above the catalogue, and optionally group chips (Browsers, Game launchers…) to jump between groups **(inference)**.
- **Buttons (finding 22).** "Install ticked apps" and "Install ticked results" are enabled whenever the service is connected (`CanInstall => IsConnected && !IsInstalling`), even with nothing ticked. Show the count ("Install 3 apps") and disable the button at zero **(inference)**. Microsoft asks for button text that "clearly describes the action that the button performs" ([Buttons](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons)).
- **Search results (finding 23).** "3 found. 2 aren't in the reviewed catalogue…" sits at the very bottom of the tab, under the install button. Move it under the search box **(inference)**.
- **Optional.** On the Installed tab, "Uninstall" has the same weight as "Update". A "…" menu would keep the row calm. App icons would help scanning, but they need icon files and add colour, so they are a low priority **(inference)**.

## Startup & Services

Screens: `default/07-startup-services.png`, `default/07-startup-services-services.png`, `smallest/07-startup-services-services.png`.

- **Startup.** Make Delete plain (see above). Show "high impact" as a pill, and sort high-impact items first, so the useful ones are on top **(inference)**.
- **Protected services (finding 24).** The vgc and wuauserv rows show an empty, greyed dropdown plus greyed Set and Undo, while the lock line below already explains why. Hide the three controls on protected rows, and put a "Protected" label with the lock glyph in their place **(inference)**.
- **Filter (finding 25).** The text box under the summary is a blank bar. It only has a tooltip ("Search every service by name") and no `AutomationProperties.Name`. Add hint text and a search glyph through the shared style, plus a name for screen readers **(inference)**.
- **Clutter (finding 26).** Every row carries a greyed "Undo". Show Undo only on rows that changed. Move the Advanced switch out of the table header to the right of the filter, labelled "Show advanced services" **(inference)**.

## Cleanup

Screens: `default/08-cleanup.png`, `smallest/08-cleanup.png`.

- **Squeezed list (finding 27).** The categories sit in their own short scroll box, with the hibernation tweak docked below it. At 1600×1000 about four rows show while 250 px of page stays empty. At 900×520 only about one and a half rows show. Let the whole page scroll as one, or put the hibernation tweak in a fold above the list **(inference)**.
- **Size on the button (finding 28).** "11 ticked: about 1.7 GB" is small text. Put the size on the button ("Clean 1.7 GB"), show a bar from `CleanupProgress`, and make "Scan again" plain **(inference)**.

## Repair

Screens: `default/09-repair.png`, `default/09-repair-fullest-repair-tool.png`.

- **Run first (finding 29).** In `DetailPane.xaml`, Run comes after "Exactly what it runs". For "Reinstall game runtimes", that list runs past the bottom of the pane, so Run and its progress are off screen. Put Run (and its progress) under the restart note at the top, and fold the command list into "Show the exact commands" **(inference)**.
- **Status on rows (finding 30).** Rows show only the restart pill. Add a short status ("Running… step 2 of 3", "Done", "Failed"), the way Apps rows do. Also add how long a tool takes ("10–30 min"), which today is only in the pane's text **(inference)**.

## Drivers

Screen: `default/10-drivers.png`.

- **Check first (finding 31).** Before any check, each card shows the installed version and a red "Download and install". The NVIDIA App leads with what's new and what's fixed in the newest driver, then a Download button. It switches between Game Ready and Studio with a dropdown ([NVIDIA App article](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)). Suggested order: "Installed 591.74 · Latest 59x.xx (newer)". "Check for a newer driver" stays primary until the newest version is known, and "Download and install" becomes primary after that. Fold the four install options into "Install options" **(inference)**.
- **Progress (finding 32).** See "Long jobs" above.

## Overclocking

Screens: `default/11-overclocking.png`, `smallest/11-overclocking.png`, `wide/11-overclocking.png`.

- **Width (finding 34).** The 760 px cap leaves about two-thirds of a 2560 window empty. At wide sizes, GPU tuning could sit in one column, with Sensors and CPU tuning in a second **(inference)**.
- **Sliders (finding 35).** The red fill runs from the left end. For "Core clock offset" (−500 to 1000, default 0) a third of the track is red at the default. "Max GPU clock" and the CPU limits are fully red at their defaults (`wide/11-overclocking.png`). Fill from the default point instead, or add a small tick at the default and leave the track grey there **(inference)**.
- **Exact values (finding 36).** The value is text only. A small number box (or − and + buttons) next to each slider would allow exact entry. Give the name column more width, so "210–3105 MHz, default 3105" doesn't wrap onto three lines **(inference)**. I did not check whether the NVIDIA App or AMD Software have number boxes, so this is not claimed from them.

## Per-game profiles

Screens: `default/12-per-game-profiles.png`, `wide/12-per-game-profiles.png`.

- **Games first (finding 37).** The game is a dropdown ("Fortnite (has a profile)"). The "Installed games" list further down is a plain `ItemsControl` that can't be clicked. Hone shows detected games as tiles to click ([Hone](https://support.hone.gg/en/articles/8833584-getting-started-with-hone)). The NVIDIA App's Graphics page lists programs, applies settings per program by default, and has a "Global Settings" tab ([NVIDIA App article](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)). AMD describes its Gaming area as "Configure, launch or view your installed games from one location" ([AMD Software](https://www.amd.com/en/products/software/adrenalin.html)). Within layout A this becomes: the games as the list (name, launcher, anti-cheat, a "Profile" pill), with the profile editor in the detail pane. Cover art would look good, but it brings colour and needs images, so it is optional **(inference)**.
- **Background apps (finding 38).** Show "Discord" with the path in dim text, and give each row its own remove "×" **(inference)**.

## Undo history

Screens: `default/13-undo-history.png`, `wide/13-undo-history.png`.

- **Virtualise and group (finding 39).** See "Known follow-ups". Group rows under "Today", "Yesterday" and dates, and show only the time on each row. Add a filter for "Can be undone", "Undone" and "Changed outside the app" **(inference)**.
- **Warning text (finding 40).** The "Didn't undo this…" box sets the whole paragraph in bold orange. Use the standard `WarningBox` with body text and only a bold lead ("Didn't undo this."), as the detail pane does **(inference)**.
- Make "Restore everything" plain (see above).

## Settings

Screen: `default/14-settings.png`.

- **About (finding 43).** Microsoft recommends "an About section at the bottom of your settings page" showing "your app name, icon, and version number", with links to the website or bug reports ([App settings](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings)). The app shows no version anywhere. "Copy app log" sits on the Repair page, and the update channel is here. An About card (version, update channel state, "Copy app log", repo link) fits **(inference)**.
- **Scattered tray settings.** The tray-note switches are on Startup ("Tell me in the tray…"), Per-game profiles ("Quiet tray notes…") and Undo history ("Show a tray note…"). Keep each where it is, since it is in context, and also list all three under a NOTIFICATIONS group here **(inference)**. Microsoft: "Group similar or related settings under one section header" ([App settings](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings)).
- Small: the summary line sits about 14 px lower than on other pages, because Settings draws its own line.

## Known follow-ups

| Follow-up | Verdict | Evidence | Suggested fix |
|---|---|---|---|
| Protected services' empty dropdown | Confirmed | `default/07-startup-services-services.png` (vgc, wuauserv); `ComboBox IsEnabled="{Binding CanChange}"` with no choices | Hide the controls; show "Protected" and the lock |
| Services filter has no placeholder | Confirmed | Blank bar in the same screenshot; only a `ToolTip` in `StartupAndServicesPage.xaml` | Shared search-box style with hint text, plus `AutomationProperties.Name` |
| Overclocking column about 800 px at 2560 | Confirmed (760 px) | `MaxWidth="760"` in `OverclockingPage.xaml` (also Drivers, Settings); `wide/11-overclocking.png` | Two columns at wide sizes; one width rule for the app |
| Catalogue doesn't mark installed apps | Confirmed | Firefox and Steam are installed (`default/05-apps-installed.png`) but plain in `default/05-apps.png`; no installed field in `AppItemViewModel` | "Installed" or "Update available" pill, matched by winget id |
| Home re-reads tweaks on every show | Confirmed | `HomeViewModel.Active` calls `RefreshAsync`, which calls `GetPcOverview`, which calls `ListTweaks()` (every tweak's live state) and `ReadPcHardware()` | Read the hardware once per session; recount tweaks only after a change |
| Undo history table isn't virtualised | Confirmed | `HistoryPage.xaml` puts an `ItemsControl` inside a `ScrollViewer`; `docs/ui.md` says long tables should use `Table.List` | Move the rows to `Table.List` + `Table.ListRow`, with the drift card above the list |

## How the reference apps handle the same jobs

| Job | What the others do | Elynt today | Idea for Elynt |
|---|---|---|---|
| Navigation | Windows Settings: labelled categories with icons and a search bar [W]. NVIDIA App: Home, Drivers, Graphics, System, plus Settings and About [N]. Hone: an Optimizations page with filters, Backups, Game Settings [H] | 14 sidebar items, no groups | Group with headers; put Per-game profiles next to Gaming |
| Home | Windows: cards incl. "Recommended Settings" [W]. NVIDIA: news, recent games Library, other NVIDIA apps [N]. AMD: one-click HYPR-RX profiles on the home tab [A2] | PC facts, counts, waiting items, section cards | Useful lines on tool cards; a "Worth a look" list; Restart now |
| Toggles with descriptions | Windows: Header + Description on each row [S]. NVIDIA: an "i" icon shows the description [N]. Hone: filters, goal sets, labels such as "Feature Breaking" [H] | Name and pills only; text only in the pane | One-line description; group long lists |
| Warnings | Hone: labels and inline "(do at your own risk)" [H]. AMD: a bell with recommendations [A1] | Strong boxes in the pane; "Destructive" pill looks calm | Red danger pill; keep the boxes |
| Empty states | Not checked for the reference apps | "Arrives in a later build" while disconnected | Page-sized status with a Retry or Set up button |
| Long jobs | NVIDIA: Automatic Tuning takes 10–20 minutes, and the article says to let the PC idle [N] | Bars on Repair and Apps only | Bars everywhere data allows; sidebar and taskbar marks |
| Search | Windows: search bar [W]. AMD: in-app search [A1]. NVIDIA: sort and filter of programs [N] | Services filter; winget search | App-wide search; catalogue filter |
| First run | NVIDIA: opens on Home, sign-in optional [N]. Hone: sign-in, then Optimizations, and suggests a restore point [H]. AMD: HYPR-RX pop-up the first time [A1] | UAC prompt, then the Home page; failures only in the footer | Clear set-up status page; "Worth a look" on Home |
| Per game | Hone: game tiles and ACTIVATE [H]. NVIDIA: program list, per-program by default, plus a Global tab [N] | Dropdown plus a non-clickable list | Game list with the profile in the detail pane |

[W] [Exploring Windows Settings](https://support.microsoft.com/en-us/windows/experience/exploring-windows-settings) · [S] [SettingsCard](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/settingscontrols/settingscard) · [N] [NVIDIA App article](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/) · [A1] [AMD profiles FAQ](https://www.amd.com/en/resources/support-articles/faqs/DH3-029.html) · [A2] [AMD Software page](https://www.amd.com/en/products/software/adrenalin.html) · [H] [Hone getting started](https://support.hone.gg/en/articles/8833584-getting-started-with-hone)

## Gaps

- **Nothing was run on Windows.** I couldn't check keyboard focus, Narrator, hover and pressed states, the moving wave, or real display scaling. The tour shoots at 100 % scale. At 150 % a 2560 screen behaves like about 1700 px wide, so the "wide" findings apply less there.
- **States the tour doesn't shoot, read from code only:** service down or not set up, the first run, a repair, install, driver install or cleanup mid-run, and Home while loading. Findings 5, 32 and 33 rest on the code.
- **The selected row off screen** in `default/03-debloat-privacy-fullest-tweak.png` may be the tour selecting without scrolling. I didn't check whether keyboard or programmatic selection scrolls in the real app.
- **The reference apps were read about, not run.** I read vendor articles and help pages, not the apps or fresh screenshots. The NVIDIA article dates from the app's November 2024 launch, and layouts may have changed since. Empty states and slider number boxes in the reference apps were not checked.
- **Web reading was kept small** (about 12 reads) because of the earlier usage limit. The AMD product page and help pages described little of Adrenalin's layout.
- **Contrast comes from the theme's colour values,** not from screenshot pixels. Anti-aliasing and the wave's position shift the real values a little.
- **Sizes are rough guesses** from reading the XAML, not an implementer's estimate. Finding 11 (pick-one choices) and finding 37 (games list) also touch view models, or the Engine for 11.

## Sources

Repo (read 2026-11-07): `docs/ui.md`, `CODING_STANDARDS.md`, `GLOSSARY.md`, `src/Elynt.App/MainWindow.xaml`, `src/Elynt.App/Theme/Theme.xaml`, `src/Elynt.App/Theme/AccentWave.cs`, `src/Elynt.App/Pages/*.xaml`, `src/Elynt.App/ViewModels/MainViewModel.cs`, `src/Elynt.App/ViewModels/HomeViewModel.cs`, `src/Elynt.App/ConfirmDialog.xaml.cs`, `src/Elynt.App/TrayNotes.cs`, `src/Elynt.Core/Engine.Overview.cs`, `src/Elynt.Core/Tweaks/WindowsUpdateTweaks.cs`, `tests/Elynt.App.Screenshots/Tour.cs`, and the screenshot tour at `fdeb92b13ac53445f52ae393f9581d2c1acf63f1`.

Microsoft and W3C:
- [Fluent 2: Button usage](https://fluent2.microsoft.design/components/web/react/core/button/usage)
- [Microsoft Learn: Buttons](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons)
- [Microsoft Learn: Dialog controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)
- [Microsoft Learn: Radio buttons](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/radio-button)
- [Microsoft Learn: NavigationView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview)
- [Microsoft Learn: Typography in Windows](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)
- [Microsoft Learn: Apply rounded corners in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners)
- [Microsoft Learn: Guidelines for app settings](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings)
- [Microsoft Learn: SettingsCard (Windows Community Toolkit)](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/settingscontrols/settingscard)
- [Microsoft Learn: Guidelines for progress controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls)
- [Microsoft Learn: Taskbar extensions](https://learn.microsoft.com/en-ca/windows/win32/shell/taskbar-extensions)
- [Microsoft Support: Exploring Windows Settings](https://support.microsoft.com/en-us/windows/experience/exploring-windows-settings)
- [W3C: Understanding SC 1.4.3 Contrast (Minimum)](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum)
- [W3C: Understanding SC 1.4.11 Non-text Contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html)

Vendors (first-party for what each vendor says about its own app):
- [NVIDIA: NVIDIA App officially released](https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/)
- [AMD: AMD Software: Adrenalin Edition](https://www.amd.com/en/products/software/adrenalin.html)
- [AMD: Information about Adrenalin graphics profiles](https://www.amd.com/en/resources/support-articles/faqs/DH3-029.html)
- [Hone: Getting started with Hone](https://support.hone.gg/en/articles/8833584-getting-started-with-hone)
