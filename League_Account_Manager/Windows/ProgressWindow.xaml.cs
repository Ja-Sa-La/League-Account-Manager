using System.Buffers;
using System.Windows;
using System.Windows.Forms;

namespace League_Account_Manager.views
{
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

            this.Left = Owner.Left + Owner.Width -this.Width - 10; 
            this.Top = Owner.Top + Owner.Height - this.Height - 10; 
        }
    }
}