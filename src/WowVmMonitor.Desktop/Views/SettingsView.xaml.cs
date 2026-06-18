using System.Runtime.InteropServices;
using WowVmMonitor.App.Settings;

namespace WowVmMonitor.Desktop.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnPasswordChanged(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        if (sender is not System.Windows.Controls.PasswordBox passwordBox ||
            passwordBox.DataContext is not MachineSettingsDraft draft)
        {
            return;
        }

        var pointer = Marshal.SecureStringToBSTR(passwordBox.SecurePassword);
        try
        {
            var length = Marshal.ReadInt32(pointer, -4) / sizeof(char);
            var characters = new char[length];
            if (length > 0)
            {
                Marshal.Copy(pointer, characters, 0, length);
            }

            draft.SetReplacementPassword(characters);
        }
        finally
        {
            Marshal.ZeroFreeBSTR(pointer);
        }
    }
}
