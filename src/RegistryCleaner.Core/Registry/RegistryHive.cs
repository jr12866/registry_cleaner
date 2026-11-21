namespace RegistryCleaner.Core.Registry;

/// <summary>
/// The top-level registry trees. <see cref="HKEY_CLASSES_ROOT"/> is special: it is a
/// merged view over <c>HKLM\SOFTWARE\Classes</c> and <c>HKCU\Software\Classes</c>, so a
/// location under it can refer to either. See <see cref="RegistryLocation"/>.
/// </summary>
public enum RegistryHive
{
    HKEY_LOCAL_MACHINE,
    HKEY_CURRENT_USER,
    HKEY_CLASSES_ROOT,
    HKEY_USERS,
    HKEY_CURRENT_CONFIG,
}
