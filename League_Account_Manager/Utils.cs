using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static League_Account_Manager.views.Page1;

namespace League_Account_Manager
{
    public class Utils
    {
        public static void RemoveDoubleQuotesFromList(List<AccountList> accountList)
        {
            foreach (var account in accountList)
            {
                account.username = RemoveDoubleQuotes(account.username);
                account.password = RemoveDoubleQuotes(account.password);
                account.riotID = RemoveDoubleQuotes(account.riotID);
                account.server = RemoveDoubleQuotes(account.server);
                account.rank = RemoveDoubleQuotes(account.rank);
                account.champions = RemoveDoubleQuotes(account.champions);
                account.skins = RemoveDoubleQuotes(account.skins);
                account.Loot = RemoveDoubleQuotes(account.Loot);
                account.rank2 = RemoveDoubleQuotes(account.rank2);
                account.note = RemoveDoubleQuotes(account.note);
            }
        }
        public class AccountList
        {
            public string? username { get; set; }
            public string? password { get; set; }
            public string? riotID { get; set; }
            public int? level { get; set; }
            public string? server { get; set; }
            public int? be { get; set; }
            public int? rp { get; set; }
            public string? rank { get; set; }
            public string? champions { get; set; }
            public string? skins { get; set; }
            public int Champions { get; set; }
            public int Skins { get; set; }
            public string? Loot { get; set; }
            public int Loots { get; set; }
            public string? rank2 { get; set; }
            public string? note { get; set; }
        }

        public class Wallet
        {
            public int? be { get; set; }
            public int? rp { get; set; }
     
       }
        public static void killleaguefunc()
        {
            try
            {
                var source = new[]
                {
                "RiotClientUxRender", "RiotClientUx", "RiotClientServices", "RiotClientCrashHandler",
                "LeagueCrashHandler",
                "LeagueClientUxRender", "LeagueClientUx", "LeagueClient"
            };

                var allProcessesKilled = false;

                while (!allProcessesKilled)
                {
                    allProcessesKilled = true;

                    foreach (var processName in source)
                    {
                        var processes = Process.GetProcessesByName(processName);

                        foreach (var process in processes)
                        {
                            process.Kill();
                            allProcessesKilled = false;
                        }
                    }

                    if (!allProcessesKilled)
                        // Wait for a moment before checking again
                        Thread.Sleep(1000); // You can adjust the time interval if needed
                }
            }
            catch (Exception exception)
            {
                LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
            }
        }
    }
}
