using Microsoft.Extensions.Logging.Abstractions;

namespace MauiApp1;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        IpEntry.Text = Preferences.Get("IP", "192.168.6.6");
        PortEntry.Text = Preferences.Get("Port", "5050");
        PrefixEntry.Text = Preferences.Get("Prefix", "QWER");
        SepEntry.Text = Preferences.Get("Sep", "/");
        LenEntry.Text = Preferences.Get("Len", "");
        EndCharEntry.Text = Preferences.Get("EndChar", "ZXCV");
        FuncPicker.SelectedItem = Preferences.Get("Func", "Ô­µãÆ«ÒÆ");
    }

    private void SaveSettings(object sender, EventArgs e)
    {
        Preferences.Set("IP", IpEntry.Text);
        Preferences.Set("Port", PortEntry.Text);
        Preferences.Set("Prefix", PrefixEntry.Text);
        Preferences.Set("Sep", SepEntry.Text);
        Preferences.Set("Len", LenEntry.Text);
        Preferences.Set("EndChar", EndCharEntry.Text);
        if (FuncPicker.SelectedItem != null)
            Preferences.Set("Func", FuncPicker.SelectedItem.ToString());
    }
}