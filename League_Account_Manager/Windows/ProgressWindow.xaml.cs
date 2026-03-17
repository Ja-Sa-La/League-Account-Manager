using System.Windows;

namespace League_Account_Manager.views;

public partial class ProgressWindow : Window
{
    private readonly int _total;

    public ProgressWindow(int total)
    {
        InitializeComponent();
        _total = total;
        ProgressBar.Maximum = total;
        UpdateProgress(0);
    }

    public void UpdateProgress(int current)
    {
        ProgressBar.Value = current;
        StatusText.Text = $"Updating ranks: {current} / {_total}";
    }

    public void FollowOwner()
    {
        if (Owner == null) return;

        Left = Owner.Left + Owner.Width - Width - 10;
        Top = Owner.Top + Owner.Height - Height - 70;
    }
}