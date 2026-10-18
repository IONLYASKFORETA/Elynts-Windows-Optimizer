using Elynt.Core.Tweaks;

namespace Elynt.Core.Services;

/// <summary>Who may change a service.</summary>
public enum ServiceAccess
{
    /// <summary>On the reviewed safe list: editable in the normal view (Automatic or Manual).</summary>
    Listed,
    /// <summary>Not reviewed: editable only with Advanced on.</summary>
    AdvancedOnly,
    /// <summary>Never changed, even in Advanced: anti-cheat, Windows Update core or a boot driver.</summary>
    Protected,
}

/// <summary>A suggested start type for this PC, and why.</summary>
public sealed record ServiceRecommendation(ServiceStart Start, string Reason);

/// <summary>A service as the Services page shows it.</summary>
/// <param name="Explanation">The reviewed explanation for a listed service, otherwise Windows' own description.</param>
/// <param name="LockReason">Why a protected service can never change; null otherwise.</param>
/// <param name="NormalChoices">Start types the user may pick with Advanced off. Empty when it can't be changed there.</param>
/// <param name="AdvancedChoices">Start types the user may pick with Advanced on. Empty for protected services.</param>
/// <param name="CanUndo">The app changed this service and can put its previous configuration back.</param>
public sealed record ServiceView(
    string Name,
    string DisplayName,
    string Explanation,
    ServiceKind Kind,
    ServiceStart Start,
    bool DelayedAutoStart,
    bool Running,
    ServiceAccess Access,
    string? LockReason,
    ServiceRecommendation? Recommendation,
    IReadOnlyList<ServiceStart> NormalChoices,
    IReadOnlyList<ServiceStart> AdvancedChoices,
    bool CanUndo);

/// <summary>The outcome of changing or undoing one service.</summary>
public sealed record ServiceResult(bool Succeeded, string Message, ServiceView? Service);
