namespace RegistryCleaner.Core.Registry;

/// <summary>
/// A single registry value: its name, type, payload, and approximate size.
/// </summary>
/// <param name="Name">
/// The value name. The empty string denotes the key's <em>default</em> value.
/// </param>
/// <param name="Kind">The value type.</param>
/// <param name="Data">
/// The payload, boxed as <see cref="string"/>, <see cref="string"/>[],
/// <see cref="byte"/>[], <see cref="uint"/>, or <see cref="ulong"/> to match
/// <paramref name="Kind"/>.
/// </param>
/// <param name="ByteSize">Approximate size in bytes, used to estimate reclaimable space.</param>
public readonly record struct RegistryValue(
    string Name,
    RegistryValueKind Kind,
    object Data,
    long ByteSize)
{
    /// <summary>True when this value is the key's default value.</summary>
    public bool IsDefault => Name.Length == 0;

    /// <summary>
    /// Returns the value's text, for the kinds that carry text. Returns an empty
    /// string for other kinds.
    /// </summary>
    public string AsString() => Kind switch
    {
        RegistryValueKind.String or RegistryValueKind.ExpandString => Data as string ?? string.Empty,
        _ => string.Empty,
    };

    /// <summary>
    /// Returns the value's entries, for <see cref="RegistryValueKind.MultiString"/>.
    /// </summary>
    public IReadOnlyList<string> AsMultiString() =>
        Kind == RegistryValueKind.MultiString && Data is string[] entries
            ? entries
            : Array.Empty<string>();
}
