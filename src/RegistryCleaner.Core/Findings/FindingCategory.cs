namespace RegistryCleaner.Core.Findings;

/// <summary>
/// The broad family a finding belongs to. Used for grouping in the GUI and for
/// <c>--category</c> filtering on the CLI.
/// </summary>
public enum FindingCategory
{
    Unknown = 0,

    /// <summary>Uninstall records, App Paths, and similar application registration.</summary>
    StaleApplicationRegistration,

    /// <summary>COM class, ProgID, TypeLib, and AppID registration.</summary>
    ComRegistration,

    /// <summary>Shell extensions, context menu handlers, and BHOs.</summary>
    ShellIntegration,

    /// <summary>Run, RunOnce, and StartupApproved entries.</summary>
    Startup,

    /// <summary>Windows Installer product and component records.</summary>
    InstallerDatabase,

    /// <summary>Fonts, printers, and other system-level registrations.</summary>
    SystemResidue,

    /// <summary>Data source names and other connection registrations.</summary>
    DataSource,

    /// <summary>Shell navigation history.</summary>
    ShellHistory,

    /// <summary>Cosmetic clutter with no functional impact.</summary>
    Clutter,
}
