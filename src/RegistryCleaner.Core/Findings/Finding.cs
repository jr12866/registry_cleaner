using RegistryCleaner.Core.Registry;

namespace RegistryCleaner.Core.Findings;

/// <summary>
/// One thing the scan found that might be removable.
/// </summary>
/// <remarks>
/// <para>
/// A finding is immutable, and it always carries evidence. There is no such thing as a
/// finding without a stated reason — a rule that cannot produce evidence for a candidate
/// may not produce a finding (ADR-015).
/// </para>
/// <para>
/// The three descriptive strings are deliberately distinct:
/// <list type="bullet">
///   <item><description><see cref="Summary"/> — the one-line label.</description></item>
///   <item><description><see cref="Explanation"/> — plain English, for the user.</description></item>
///   <item><description><see cref="Evidence"/> — the raw fact, so a skeptical reader can verify it.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed record Finding(
    string Id,
    string RuleId,
    RegistryLocation Location,
    string Summary,
    string Explanation,
    string Evidence,
    RiskTier Risk,
    int Confidence,
    FindingCategory Category,
    string? SoftwareName = null,
    long EstimatedBytes = 0)
{
    /// <summary>Whether the target is a key or a value inside a key.</summary>
    public FindingKind Kind => Location.IsValueTarget ? FindingKind.Value : FindingKind.Key;

    /// <summary>
    /// Whether the user is allowed to select this finding for removal.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> by <c>ProtectedPathGuard</c> for anything under a
    /// protected path, and for <see cref="RiskTier.Critical"/> findings.
    /// </remarks>
    public bool Selectable { get; init; } = Risk is not RiskTier.Critical;

    /// <summary>
    /// The friendly software name when one is known, otherwise the registry path.
    /// </summary>
    public string SoftwareNameOrLocation => SoftwareName ?? Location.DisplayPath;

    /// <summary>
    /// Returns a copy with the risk raised and selection disabled. Used by the guard,
    /// which may only ever raise a tier, never lower one.
    /// </summary>
    public Finding WithRisk(RiskTier risk, bool selectable) =>
        this with { Risk = risk, Selectable = selectable };
}
