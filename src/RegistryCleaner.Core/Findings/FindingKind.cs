namespace RegistryCleaner.Core.Findings;

/// <summary>
/// Whether a finding targets a whole key or a single value inside a key.
/// </summary>
public enum FindingKind
{
    /// <summary>The key itself is the target.</summary>
    Key,

    /// <summary>A single value inside the key is the target.</summary>
    Value,
}
