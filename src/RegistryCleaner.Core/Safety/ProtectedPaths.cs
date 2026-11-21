using RegistryCleaner.Core.Registry;

namespace RegistryCleaner.Core.Safety;

/// <summary>
/// The default deny-list of registry paths.
/// </summary>
/// <remarks>
/// <para>
/// This list is enforced by <see cref="ProtectedPathGuard"/> in code. It is not a
/// convention that rule authors are expected to follow, and rules have no way to opt out
/// of it (ADR-008).
/// </para>
/// <para>
/// The entries here correspond one-to-one with <c>docs/SAFETY.md</c> §4.1. Changing one
/// without the other is a review failure.
/// </para>
/// </remarks>
public static class ProtectedPaths
{
    /// <summary>
    /// Value names that are never removed, whatever key they live in.
    /// </summary>
    /// <remarks>
    /// From <c>docs/SAFETY.md</c> §4.2. Two further names in that section —
    /// <c>UninstallString</c> and <c>InstallLocation</c> — are conditional on the state of
    /// the surrounding key, so they are enforced by the individual rules instead of here.
    /// </remarks>
    public static IReadOnlySet<string> ProtectedValueNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System",
            "BootId",
            "CurrentVersion",
            "PathName",
            "ServiceDll",
            "ImagePath",
            "Start",
            "Type",
            "ObjectName",
            "Security",
            "Parameters",
            "EventMessageFile",
            "CategoryMessageFile",
            "MessageFile",
            "ProviderPath",
            "LocationInformation",
            "ServiceMain",
        };

    /// <summary>
    /// The default list of protected registry paths.
    /// </summary>
    public static IReadOnlyList<ProtectedPath> Default { get; } = new[]
    {
        // --- Security-critical hives -------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SAM",
            "Local account database. Not recoverable."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SECURITY",
            "LSA secrets and security policies. Not recoverable."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"BOOT",
            "Boot loader configuration."),

        // --- SYSTEM ------------------------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SYSTEM\Select",
            "Duplicate view of the current control set; writing to it corrupts the live set."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SYSTEM\Setup",
            "Pending setup state."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SYSTEM\CurrentControlSet\Enum",
            "Enumerated device and driver instances."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SYSTEM\CurrentControlSet\Services",
            "Service and driver configuration."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SYSTEM\CurrentControlSet\Control\Class",
            "Device class configuration."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SYSTEM\CurrentControlSet\Control\DeviceClasses",
            "Plug and Play device classes."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE, @"SYSTEM\CurrentControlSet\Control\DeviceGuard",
            "Secure Boot and code integrity policy."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            "Paging and memory configuration."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SYSTEM\CurrentControlSet\Control\Session Manager\BootExecute",
            "Boot-time execution."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SYSTEM\CurrentControlSet\Control\Session Manager",
            "Session Manager configuration.",
            valueName: "PendingFileRenameOperations"),

        // --- Installer cache ----------------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Cache",
            "Cached MSI/MSP payloads required for repair and uninstall."),

        // --- Logon and process injection ----------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
            "Shell, Userinit, and notification handlers."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
            "Per-executable redirection and debugging."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows",
            "AppInit_DLLs and legacy loaders."),

        // --- User shell folders ---------------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_CURRENT_USER,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
            "Location of the user's folders."),
        new ProtectedPath(RegistryHive.HKEY_CURRENT_USER,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders",
            "Location of the user's folders, unexpanded."),

        // --- Group Policy ---------------------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies",
            "Group Policy."),
        new ProtectedPath(RegistryHive.HKEY_CURRENT_USER,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies",
            "Group Policy."),

        // --- Windows servicing and update ------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing",
            "Windows Update component store."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT\Channels",
            "Event log channel definitions."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate",
            "Windows Update agent state."),
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows Defender",
            "Security policy and exclusions."),

        // --- Shell extension container -----------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects",
            "BHO container key. Individual entries may be cleaned; the container may not."),

        // --- In-box COM -------------------------------------------------------------------
        new ProtectedPath(RegistryHive.HKEY_CLASSES_ROOT,
            @"CLSID\{6D809377-6AF0-444B-8957-A3773F02200E}",
            "Shell infrastructure used by Explorer itself."),
    };

    /// <summary>
    /// File system locations that must never be the target of a removal.
    /// </summary>
    /// <remarks>
    /// A registry value elsewhere can name one of these paths — for example a value that
    /// points at the SAM hive file. Protecting the key is not enough; the file path needs
    /// protecting too.
    /// </remarks>
    public static IReadOnlyList<string> ProtectedFilePaths { get; } = new[]
    {
        @"%SystemRoot%\System32\config",
        @"%SystemRoot%\System32\config\SAM",
        @"%SystemRoot%\System32\config\SECURITY",
        @"%SystemRoot%\System32\config\SYSTEM",
        @"%SystemRoot%\System32\config\SOFTWARE",
        @"%SystemRoot%\repair",
    };
}
