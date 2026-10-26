# CPU tuning goes only through the optional PawnIO driver, and only lowers

In-Windows CPU tuning goes only through PawnIO's signed modules, which are installed only when the user turns them on,
and it only lowers: negative-only Curve Optimizer and lower-only PPT, TDC and EDC on Ryzen, and PL1 and PL2 lowering on
Intel. Undervolting Intel and anything that raises limits are left to BIOS guides. No CPU vendor offers a public way to
write these from Windows, the drivers older tools used (WinRing0 and kin) are known-vulnerable and banned by the
never-touch rule, and the open-source SMU libraries are GPL, so the app speaks to PawnIO through its own thin layer.

## Consequences

- Lowering only keeps the worst case a slower CPU, and nothing set from Windows survives a reboot, so a crash always
  falls back to the BIOS settings.
- PawnIO's RyzenSMU module passes any message, so the Engine's SMU allowlist is the only guard.
- FACEIT blocks PawnIO, so it stays optional and its install warns, naming installed FACEIT games.
