using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
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
using Microsoft.CSharp.RuntimeBinder;

namespace League_Account_Manager.views
{
    /// <summary>
    /// Interaction logic for Page5.xaml
    /// </summary>
    public partial class Page5 : Page
    {

        class champs
        {
            public int Column1 { get; set; }
            public int Column2 { get; set; }
            public string Column3 { get; set; }
        }

        public Page5()
        {
            InitializeComponent();
            items.Clear();
            items.Add(new champs
            {
                Column1 = 350,
                Column2 = 450,
                Column3 = "yuumi"
            });
            items.Add(new champs
            {
                Column1 = 19,
                Column2 = 450,
                Column3 = "Warwick"
            });
            items.Add(new champs
            {
                Column1 = 17,
                Column2 = 450,
                Column3 = "Teemo"
            });
            items.Add(new champs
            {
                Column1 = 16,
                Column2 = 450,
                Column3 = "Soraka"
            });
            items.Add(new champs
            {
                Column1 = 37,
                Column2 = 450,
                Column3 = "Sona"
            });
            items.Add(new champs
            {
                Column1 = 113,
                Column2 = 450,
                Column3 = "Sejuani"
            });
            items.Add(new champs
            {
                Column1 = 15,
                Column2 = 450,
                Column3 = "Sivir"
            });
            items.Add(new champs
            {
                Column1 = 78,
                Column2 = 450,
                Column3 = "Poppy"
            });
            items.Add(new champs
            {
                Column1 = 20,
                Column2 = 450,
                Column3 = "Nunu & Willump"
            });
            items.Add(new champs
            {
                Column1 = 21,
                Column2 = 450,
                Column3 = "Miss Fortune"
            });
            items.Add(new champs
            {
                Column1 = 11,
                Column2 = 450,
                Column3 = "Master Yi"
            });
            items.Add(new champs
            {
                Column1 = 99,
                Column2 = 450,
                Column3 = "Lux"
            });
            items.Add(new champs
            {
                Column1 = 63,
                Column2 = 450,
                Column3 = "Brand"
            });
            items.Add(new champs
            {
                Column1 = 22,
                Column2 = 450,
                Column3 = "Ashe"
            });
            items.Add(new champs
            {
                Column1 = 54,
                Column2 = 450,
                Column3 = "Malphite"
            });
            items.Add(new champs
            {
                Column1 = 89,
                Column2 = 450,
                Column3 = "Leona"
            });
            items.Add(new champs
            {
                Column1 = 32,
                Column2 = 450,
                Column3 = "Amumu"
            });
            items.Add(new champs
            {
                Column1 = 26,
                Column2 = 1350,
                Column3 = "Zilean"
            });
            items.Add(new champs
            {
                Column1 = 44,
                Column2 = 1350,
                Column3 = "Taric"
            });
            items.Add(new champs
            {
                Column1 = 117,
                Column2 = 4800,
                Column3 = "Lulu"
            });

        }

       
        static List<champs> items = new List<champs>();

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            int totalcost = 0;
            int count = 1;
            string championsbought = " Bought champions \n";
            string championsfailed = "Failed to Buy \n";
            
            foreach (var item in items)
            {
                dynamic val = await lcu.Connector("league", "post", "/lol-purchase-widget/v2/purchaseItems", "{\"items\":[{\"itemKey\":{\"inventoryType\":\"CHAMPION\",\"itemId\":" + item.Column1 + "},\"purchaseCurrencyInfo\":{\"currencyType\":\"IP\",\"price\":" + item.Column2 + ",\"purchasable\":true},\"source\":\"cdp\",\"quantity\":1}]}");
                dynamic val2 = JsonArray.Parse( await val.Content.ReadAsStringAsync().ConfigureAwait(false));
                try
                {
                    totalcost += Convert.ToInt32(val2["items"][0]["purchaseCurrencyInfo"]["price"].ToString());
                    statusmessage.Content = "Bought: " + item.Column3 + "! progress: " + count + "/20 Total BE used: " + totalcost;
                    championsbought = championsbought + item.Column3 + "\n";
                    success.Text = championsbought;
                }
                catch (Exception value)
                {
                    Console.WriteLine(value);
                    Console.WriteLine(val2);
                    statusmessage.Content = "Bought: " + item.Column3 + "! progress: " + count + "/20 Total BE used: " + totalcost;
                    championsfailed = championsfailed + item.Column3 + " " + val2["message"] + "\n";
                    failure.Text = championsfailed;
                }
                Thread.Sleep(2000);
                count++;
            }
            statusmessage.Content = "All champions bought! Total BE used: " + totalcost;
        }
    }
}
