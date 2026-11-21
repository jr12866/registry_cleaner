using RegistryCleaner.Core.Findings;

namespace RegistryCleaner.Core.Safety;

/// <summary>
/// The outcome of applying the safety guard to a candidate target.
/// </summary>
/// <param name="IsProtected">Whether the target is protected and must not be removed.</param>
/// <param name="Reason">Why it is protected, or <see langword="null"/> when it is allowed.</param>
/// <param name="EffectiveRisk">The risk tier after the guard has been applied.</param>
/// <param name="Selectable">Whether the user may select the target for removal.</param>
public sealed record GuardResult(
    bool IsProtected,
    string? Reason,
    RiskTier EffectiveRisk,
    bool Selectable)
{
    /// <summary>
    /// The result for a target that is allowed, at the proposed risk tier.
    /// </summary>
    public static GuardResult Allowed(RiskTier proposedRisk) =>
        new(false, null, proposedRisk, proposedRisk is not RiskTier.Critical);

    /// <summary>
    /// The result for a target that is protected. The risk is raised to
    /// <see cref="RiskTier.Critical"/> and selection is disabled.
    /// </summary>
    public static GuardResult Blocked(string reason) =>
        new(true, reason, RiskTier.Critical, false);
}
