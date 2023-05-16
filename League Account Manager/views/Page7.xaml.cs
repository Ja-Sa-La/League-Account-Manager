using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace League_Account_Manager.views
{
    /// <summary>
    /// Interaction logic for Page7.xaml
    /// </summary>
    public partial class Page7 : Page
    {
        public Page7()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var championsbought = "Files \n";
            var processesByName = Process.GetProcessesByName("RiotClientUx");
            var processesByName2 = Process.GetProcessesByName("LeagueClientUx");
            Page1.killleaguefunc(processesByName, processesByName2);
            DeleteFilesAndFolders(list, championsbought);
            
        }

        private string[] list = new string[]
        {
            "C:\\ProgramData\\Riot Games\\",
            "C:\\Riot Games\\Riot Client\\UX\\GPUCache",
            "C:\\Users\\"+ Environment.UserName + "\\AppData\\Local\\Riot Games\\",
            "C:\\Riot Games\\Riot Client\\UX\\databases-incognito",
            "C:\\Users\\"+ Environment.UserName + "\\AppData\\LocalLow\\Microsoft\\CryptnetUrlCache\\",
            "C:\\Riot Games\\Riot Client\\UX\\icudtl.dat",
            "C:\\Riot Games\\League of Legends\\databases-off-the-record",
            "C:\\Riot Games\\League of Legends\\debug.log",
            "C:\\Riot Games\\League of Legends\\Logs",
            "C:\\Riot Games\\League of Legends\\Config",
            "C:\\Riot Games\\League of Legends\\icudtl.dat",
            "C:\\Riot Games\\League of Legends\\system.yaml",
            "C:\\Riot Games\\League of Legends\\snapshot_blob.bin",
            "C:\\Riot Games\\League of Legends\\natives_blob.bin",
            "C:\\Riot Games\\Riot Client\\snapshot_blob.bin",
            "C:\\Riot Games\\Riot Client\\natives_blob.bin",
            "C:\\Riot Games\\Riot Client\\UX\\icudtl.dat",
            "C:\\Riot Games\\Riot Client\\UX\\v8_context_snapshot.bin",
            "C:\\Riot Games\\Riot Client\\UX\\snapshot_blob.bin",
            "C:\\Riot Games\\Riot Client\\UX\\natives_blob.bin",
            "C:\\Riot Games\\League of Legends\\DATA",
            "C:\\Riot Games\\League of Legends\\v8_context_snapshot.bin"
        };
        public void DeleteFilesAndFolders(string[] paths, string championsbought)
        {
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    championsbought = championsbought + "Deleted Item: " + path + "\n";
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    championsbought = championsbought + "Deleted Item: " + path + "\n";
                }
                else
                {
                    championsbought = championsbought + "Failed to delete item or item does not exist: " + path + "\n";
                }
                success.Text = championsbought;
            }
            championsbought = championsbought + "LOGS HAVE BEEN CLEANED!!!";
            success.Text = championsbought;
        }
    }
}
