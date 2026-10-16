using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

public static class GamingTweaks
{
    private const string MouseKey = @"Control Panel\Mouse";

    public static TweakDefinition EnhancePointerPrecisionOff { get; } = new(
        Id: "gaming.enhance-pointer-precision-off",
        Section: Sections.Gaming,
        Name: "Turn off Enhance pointer precision",
        WhatItDoes:
            "Turns off Windows mouse acceleration, so the same hand movement always moves the cursor the same distance, " +
            "however fast you move. Games that use the Windows cursor get 1:1 aim. Games that read raw mouse input are " +
            "not affected either way. The desktop cursor may feel slower at first.",
        Evidence:
            "Microsoft documents that cursor messages (WM_MOUSEMOVE) have pointer acceleration applied while raw input " +
            "(WM_INPUT) has none. Acceleration depends on speed, so the same movement lands in different places, which " +
            "works against muscle memory. The benefit is consistent aim, not more FPS.",
        Sources:
        [
            new("Microsoft: Taking advantage of high-definition mouse movement",
                "https://learn.microsoft.com/en-us/windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement"),
            new("How-To Geek: What is Enhance Pointer Precision in Windows?",
                "https://www.howtogeek.com/321763/what-is-enhance-pointer-precision-in-windows/"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back exactly the values that were there before. Windows' own defaults are 1, 6 and 10.",
        Steps:
        [
            new RegistryValueStep(new(RegistryRoot.CurrentUser, MouseKey, "MouseSpeed"), RegistryValue.String("0")),
            new RegistryValueStep(new(RegistryRoot.CurrentUser, MouseKey, "MouseThreshold1"), RegistryValue.String("0")),
            new RegistryValueStep(new(RegistryRoot.CurrentUser, MouseKey, "MouseThreshold2"), RegistryValue.String("0")),
        ],
        Refresh: SessionRefresh.MouseSettings)
    {
        Reversibility = Reversibility.Reversible,
        Risks = RiskFlags.None,
        Applicability = Applicability.Always,
        // Instant, but the signed-in session must re-read the mouse settings, which a profile running in the
        // service can't do.
        ProfileEligible = false,
    };

    public static IReadOnlyList<TweakDefinition> All { get; } = [EnhancePointerPrecisionOff];
}
