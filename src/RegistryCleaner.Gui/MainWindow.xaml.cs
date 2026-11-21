using System.Windows;
using RegistryCleaner.Core.Safety;

namespace RegistryCleaner.Gui;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Creates the main window and reports the current elevation state.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        var elevated = ElevationGuard.IsElevated();
        StatusText.Text = elevated
            ? "Running elevated. The tool writes to HKLM, so it runs with administrator rights for its whole lifetime."
            : "Running without elevation. Scanning works, but applying changes will require administrator rights.";
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        // M1. See docs/UI.md for the scan behaviour.
    }
}
