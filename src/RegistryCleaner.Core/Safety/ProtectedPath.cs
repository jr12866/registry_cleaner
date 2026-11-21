using RegistryCleaner.Core.Registry;

namespace RegistryCleaner.Core.Safety;

/// <summary>
/// A registry key (or a single value within a key) that must never be removed.
/// </summary>
/// <remarks>
/// See <c>docs/SAFETY.md</c> §4 for the full list and the reasoning behind each entry.
/// </remarks>
public sealed class ProtectedPath
{
    /// <summary>
    /// Creates a protection entry.
    /// </summary>
    /// <param name="hive">The hive containing the protected key.</param>
    /// <param name="keyPath">The key path, relative to the hive.</param>
    /// <param name="reason">Why this path is protected. Shown to the user.</param>
    /// <param name="valueName">
    /// When non-null, only this value inside the key is protected. When null, the whole
    /// subtree is protected.
    /// </param>
    public ProtectedPath(
        RegistryHive hive,
        string keyPath,
        string reason,
        string? valueName = null)
    {
        Hive = hive;
        KeyPath = keyPath;
        Reason = reason;
        ValueName = valueName;
    }

    /// <summary>The hive containing the protected key.</summary>
    public RegistryHive Hive { get; }

    /// <summary>The key path, relative to the hive.</summary>
    public string KeyPath { get; }

    /// <summary>
    /// The protected value name, or <see langword="null"/> when the whole subtree is
    /// protected.
    /// </summary>
    public string? ValueName { get; }

    /// <summary>Why this path is protected.</summary>
    public string Reason { get; }

    /// <summary>True when only a single value is protected rather than the whole subtree.</summary>
    public bool IsValueScoped => ValueName is not null;

    /// <inheritdoc />
    public override string ToString()
    {
        var path = $"{Hive}\\{KeyPath}";
        return ValueName is null ? path : $"{path} :: {ValueName}";
    }
}
