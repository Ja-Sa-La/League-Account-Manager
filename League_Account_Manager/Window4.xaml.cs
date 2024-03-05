using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace League_Account_Manager
{
    /// <summary>
    /// Interaction logic for Window4.xaml
    /// </summary>
    public partial class Window4 : Window
    {
        string dataholder = "";
        public Window4(string Data)
        {
            InitializeComponent();
            Datathing.Clear();
            dataholder = Data.Replace(":", Environment.NewLine).Trim();
            Datathing.AppendText(Data.Replace(":", Environment.NewLine).Trim());
        }


        private void Window_MouseDownDatadisplay(object sender, MouseButtonEventArgs e)
        {

                if (e.ChangedButton == MouseButton.Left)
                    this.DragMove();
            
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Close();
        }



        private void TextBox_TextChangeddatafilt(object sender, TextChangedEventArgs e)
        {

            var lines = dataholder.Split(Environment.NewLine);
            string searchTerm = datafiltersearch.Text;
            var filteredLines = lines.Where(line => line.ToLower().Contains(searchTerm.ToLower()));
            Datathing.Text = string.Join("\n", filteredLines);
        }
    }
}
