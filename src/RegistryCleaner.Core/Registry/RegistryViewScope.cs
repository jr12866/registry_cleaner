namespace RegistryCleaner.Core.Registry;

/// <summary>
/// Which registry view a location belongs to. On 64-bit Windows, <c>HKLM\SOFTWARE</c>
/// exists twice: the native 64-bit view and the 32-bit view (<c>WOW6432Node</c>).
/// A key path alone does not identify a key without also stating the view.
/// </summary>
/// <remarks>See ADR-012 in <c>docs/DECISIONS.md</c>.</remarks>
public enum RegistryViewScope
{
    /// <summary>The view the current process would naturally see.</summary>
    Default,

    /// <summary>The native 64-bit view.</summary>
    Registry64,

    /// <summary>The 32-bit (WOW6432Node) view.</summary>
    Registry32,
}
