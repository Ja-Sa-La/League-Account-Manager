using League_Account_Manager.views;
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
    /// Interaction logic for Window2.xaml
    /// </summary>
    public partial class Window2 : Window
    {
        public Window2()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string[] array = accountlogins.Text.Split(new string[1] { Environment.NewLine }, StringSplitOptions.None);
            Page2.bulkadd.Clear();
            string[] array2 = array;
            for (int i = 0; i < array2.Length; i++)
            {
                string[] array3 = array2[i].Split(":");
                Page2.bulkadd.Add(new Page2.usernamelist
                {
                    username = array3[0],
                    password = array3[1]
                });
            }
            Close();
        }
    }
}
