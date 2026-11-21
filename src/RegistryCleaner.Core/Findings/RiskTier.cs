namespace RegistryCleaner.Core.Findings;

/// <summary>
/// The consequence of a wrong call: how much damage removing the target would cause
/// if the finding turns out not to be stale.
/// </summary>
/// <remarks>
/// See <c>docs/SAFETY.md</c> §2. Risk is deliberately independent of
/// <see cref="Finding.Confidence"/> — "how bad if I am wrong" and "how sure am I" are
/// different questions (ADR-003).
/// </remarks>
public enum RiskTier
{
    /// <summary>S1 — removing it cannot plausibly affect running software.</summary>
    Safe = 1,

    /// <summary>S2 — administrative residue of a program that is no longer installed.</summary>
    Low = 2,

    /// <summary>S3 — could affect an application that is still installed.</summary>
    Moderate = 3,

    /// <summary>S4 — could affect system, driver, or shell behavior.</summary>
    High = 4,

    /// <summary>
    /// S5 — not offered for removal at all. Reported for awareness only.
    /// </summary>
    Critical = 5,
}
