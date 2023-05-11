using Newtonsoft.Json.Linq;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace League_Account_Manager.views
{
    /// <summary>
    /// Interaction logic for Page6.xaml
    /// </summary>
    public partial class Page6 : Page
    {
        public Page6()
        {
            InitializeComponent();
        }

        class Data
        {

        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {


            var resp = await lcu.Connector("league", "get", "/lol-summoner/v1/summoners?name=" + playername.Text, "");
            var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var rankedinfo = JObject.Parse(responseBody);
            var resp2 = await lcu.Connector("league", "get",
                "/lol-match-history/v1/products/lol/" + rankedinfo["puuid"].ToString() + "/matches?begIndex=0&endIndex=0", "");
            var responseBody2 = await resp2.Content.ReadAsStringAsync().ConfigureAwait(false);
            var rankedinfo2 = JObject.Parse(responseBody2);
            //Console.WriteLine(rankedinfo2);
            foreach (var item in rankedinfo2["games"]["games"])
            {
                string reportstring = "{\"gameId\":" + item["gameId"].ToString() + ",\"categories\":[\"NEGATIVE_ATTITUDE\",\"VERBAL_ABUSE\",\"HATE_SPEECH\"],\"offenderSummonerId\":" + rankedinfo["accountId"].ToString() + ",\"offenderPuuid\":\"" + rankedinfo["puuid"].ToString() + "\"}";
                 resp = await lcu.Connector("league", "post", "/lol-end-of-game/v2/player-reports", reportstring);
                var responseBody3 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            reportstatus.Content = "Reported player " + playername.Text;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                throw;
            }
        }

        private void playername_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
