namespace RegistryCleaner.Core.Safety.PathNormalization;

/// <summary>
/// Resolves path aliases, chiefly the 8.3 short-name form of a Windows path
/// (<c>C:\PROGRA~1\...</c>) into its long form (<c>C:\Program Files\...</c>).
/// </summary>
/// <remarks>
/// <para>
/// Registry <em>key</em> paths have no 8.3 aliases, but the <em>file paths</em> stored
/// inside registry values do. A rule that guards against touching
/// <c>%SystemRoot%\System32\config</c> would otherwise miss a value that names the same
/// directory in short form.
/// </para>
/// <para>
/// This is an interface so that the guard can be tested against a deterministic fake
/// (ADR-017).
/// </para>
/// </remarks>
public interface IPathAliasResolver
{
    /// <summary>
    /// Returns the canonical form of <paramref name="path"/>, or the input unchanged when
    /// no alias could be resolved.
    /// </summary>
    string Resolve(string path);
}

/// <summary>
/// An alias resolver that performs no resolution. The default for the guard.
/// </summary>
public sealed class NullPathAliasResolver : IPathAliasResolver
{
    /// <summary>The shared instance.</summary>
    public static NullPathAliasResolver Instance { get; } = new();

    /// <inheritdoc />
    public string Resolve(string path) => path;
}
