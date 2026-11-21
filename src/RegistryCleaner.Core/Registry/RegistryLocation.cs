namespace RegistryCleaner.Core.Registry;

/// <summary>
/// Identifies exactly one registry key or one registry value.
/// </summary>
/// <remarks>
/// <para>
/// A location is the tuple <c>(Hive, KeyPath, ValueName, View)</c>. The view is part of
/// the identity because the same key path under different views is a different key with
/// different content (ADR-012).
/// </para>
/// <para>
/// When <paramref name="ValueName"/> is <see langword="null"/> the location refers to
/// <em>the key itself</em>. When it is non-null it refers to <em>that value inside the
/// key</em>. This distinction matters during remediation: deleting a value must not
/// delete the key, and vice versa.
/// </para>
/// </remarks>
public readonly record struct RegistryLocation(
    RegistryHive Hive,
    string KeyPath,
    string? ValueName,
    RegistryViewScope View)
{
    /// <summary>
    /// Creates a location that refers to a key rather than to a value inside it.
    /// </summary>
    public RegistryLocation(RegistryHive hive, string keyPath, RegistryViewScope view = RegistryViewScope.Default)
        : this(hive, keyPath, null, view)
    {
    }

    /// <summary>
    /// True when this location targets a single value rather than a whole key.
    /// </summary>
    public bool IsValueTarget => ValueName is not null;

    /// <summary>
    /// A human-readable rendering, suitable for a UI and for log output.
    /// </summary>
    public string DisplayPath
    {
        get
        {
            var path = $"{Hive}\\{KeyPath}";
            if (ValueName is not null)
            {
                path += $" :: {ValueName}";
            }

            if (View != RegistryViewScope.Default)
            {
                path += $" [{View}]";
            }

            return path;
        }
    }

    /// <summary>
    /// Returns the same location with the given value name, or with none when
    /// <paramref name="valueName"/> is <see langword="null"/>.
    /// </summary>
    public RegistryLocation WithValue(string? valueName) => this with { ValueName = valueName };

    /// <summary>
    /// Returns the location of the key itself, discarding any value name.
    /// </summary>
    public RegistryLocation AsKey() => this with { ValueName = null };

    /// <inheritdoc />
    public override string ToString() => DisplayPath;
}
