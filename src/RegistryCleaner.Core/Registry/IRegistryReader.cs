namespace RegistryCleaner.Core.Registry;

/// <summary>
/// Read-only access to the registry.
/// </summary>
/// <remarks>
/// Every rule reads through this interface, never through <c>Microsoft.Win32.Registry</c>
/// directly. That is what allows the rule engine to be tested against an in-memory fake
/// (ADR-005, ADR-017).
/// </remarks>
public interface IRegistryReader
{
    /// <summary>
    /// Returns whether the key exists.
    /// </summary>
    bool KeyExists(RegistryLocation location);

    /// <summary>
    /// Returns the names of the key's immediate subkeys. Empty if the key does not exist.
    /// </summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryLocation key);

    /// <summary>
    /// Returns the names of the key's values. The empty string denotes the default value.
    /// </summary>
    IReadOnlyList<string> GetValueNames(RegistryLocation key);

    /// <summary>
    /// Returns the value, or <see langword="null"/> if it does not exist.
    /// </summary>
    /// <param name="location">
    /// A location whose <see cref="RegistryLocation.ValueName"/> is non-null.
    /// </param>
    RegistryValue? GetValue(RegistryLocation location);

    /// <summary>
    /// Returns <em>all descendant keys</em> of <paramref name="root"/>, at any depth,
    /// excluding the root itself.
    /// </summary>
    /// <remarks>
    /// This is deliberately recursive: the reference index builder needs the entire
    /// subtree in one pass, and rules that only need direct children can use
    /// <see cref="GetSubKeyNames"/> instead.
    /// </remarks>
    IReadOnlyList<RegistryLocation> EnumerateSubKeys(RegistryLocation root);
}
