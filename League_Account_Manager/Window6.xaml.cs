    using System.IO;
    using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CsvHelper;
using League_Account_Manager.views;
using static League_Account_Manager.views.Page1;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using CsvHelper.Configuration;
using System.Globalization;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for Window4.xaml
/// </summary>
public partial class Window6 : Window
{
    private Utils.AccountList dataholder;
    private readonly CsvConfiguration _config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };

    public Window6(Utils.AccountList Data)
    {
        InitializeComponent();
        Datathing.Clear();
        dataholder = Data;
        Datathing.AppendText(Data.note);
    }


    private void Window_MouseDownDatadisplay(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private async void Window_Deactivated(object sender, EventArgs e)
    {
        if (Datathing.Text != dataholder.note)
        {
            dataholder.note = Datathing.Text;
            Page1.ActualAccountlists.RemoveAll(r => r.username == dataholder.username && r.password == dataholder.password);
            Page1.ActualAccountlists.Add(dataholder);
            Utils.RemoveDoubleQuotesFromList(Page1.ActualAccountlists);
            FileStream? fileStream = null;
            while (fileStream == null)
                try
                {
                    fileStream =
                        File.Open(Path.Combine(Directory.GetCurrentDirectory(), $"{Settings.settingsloaded.filename}.csv"),
                            FileMode.Open, FileAccess.Read, FileShare.None);
                    fileStream.Close();
                }
                catch (IOException)
                {
                    // The file is in use by another process. Wait and try again.
                    await Task.Delay(1000);
                }

            using var writer =
                new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), $"{Settings.settingsloaded.filename}.csv"));
            using var csvWriter = new CsvWriter(writer, _config);
            csvWriter.WriteRecords(Page1.ActualAccountlists);

        }
        Close();
    }

}