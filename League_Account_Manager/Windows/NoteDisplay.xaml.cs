using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CsvHelper.Configuration;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Windows;

/// <summary>
///     Interaction logic for DisplayDataWithSearch.xaml
/// </summary>
public partial class NoteDisplay : Window
{
    private readonly CsvConfiguration _config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };
    private readonly Utils.AccountList dataholder;
    private bool _isClosing;

    public NoteDisplay(Utils.AccountList Data)
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

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (Datathing.Text == dataholder.note) return;

        dataholder.note = Datathing.Text;
        var filePath = AccountFileStore.GetAccountsFilePath();
        var records = await AccountFileStore.LoadAsync(filePath, _config);

        var updated = false;
        foreach (var record in records)
        {
            if (!IsSameAccount(record, dataholder)) continue;
            record.note = dataholder.note;
            updated = true;
        }

        if (!updated)
            records.Add(dataholder);

        Utils.RemoveDoubleQuotesFromList(records);
        await AccountFileStore.SaveAsync(filePath, records, _config);
    }

    private static bool IsSameAccount(Utils.AccountList left, Utils.AccountList right)
    {
        return string.Equals(left.username, right.username, StringComparison.Ordinal) &&
               string.Equals(left.password, right.password, StringComparison.Ordinal);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }
}