using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

/// <summary>Cleanup's tweaks (spec user story 89). File cleaning itself is the cleanup scan, not tweaks.</summary>
public static class CleanupTweaks
{
    public static TweakDefinition HibernationOff { get; } = new(
        Id: "cleanup.hibernation-off",
        Section: Sections.Cleanup,
        Name: "Turn off hibernation and delete the hibernation file",
        WhatItDoes:
            "Turns hibernation off, so Windows deletes hiberfil.sys from the Windows drive after the next restart. That " +
            "file is usually a large share of your RAM's size. This also turns off Fast Startup, which uses the same " +
            "file: cold boots may take a few seconds longer, and Hibernate disappears from the power menu.",
        Evidence:
            "Microsoft documents that turning hibernation off removes hiberfil.sys and that Fast Startup depends on it. " +
            "A disk-space change, not a performance one.",
        Sources:
        [
            new("Microsoft: Disable and re-enable hibernation",
                "https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation"),
            new("Microsoft: Distinguishing Fast Startup from wake-from-hibernation",
                "https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/distinguishing-fast-startup-from-wake-from-hibernation"),
        ],
        Tier: EvidenceTier.Proven,
        Undo:
            "Puts back the previous hibernation setting; after a restart Windows recreates hiberfil.sys and Fast Startup " +
            "works again if it was on.",
        Steps:
        [
            new RegistryValueStep(
                new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"),
                RegistryValue.DWord(0)),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
    };

    public static IReadOnlyList<TweakDefinition> All { get; } = [HibernationOff];
}
