﻿using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CsvHelper;
using CsvHelper.Configuration;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page2.xaml
/// </summary>
public partial class Page2 : Page
{
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };

    public class usernamelist
    {
        public string username { get; set; }

        public string password { get; set; }
    }

    public Page2()
    {
        InitializeComponent();
    }

    public List<Champs> jotain { get; private set; }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        if (Password.Password == "" || Username.Text == "")
        {
            MissingPass.Visibility = Visibility.Visible;
        }
        else
        {
            MissingPass.Visibility = Visibility.Hidden;
            using (var reader = new StreamReader(Directory.GetCurrentDirectory() + "/List.csv"))
            using (var csv = new CsvReader(reader, config))
            {
                var records = csv.GetRecords<Champs>();
                jotain = records.ToList();
            }

            jotain.Add(new Champs { username = Username.Text, password = Password.Password });
            using (var writer = new StreamWriter(Directory.GetCurrentDirectory() + "/List.csv"))
            using (var csv2 = new CsvWriter(writer, config))
            {
                csv2.WriteRecords(jotain);
            }
        }
    }

    public class Champs
    {
        public string username { get; set; }
        public string password { get; set; }
        public string level { get; set; }
        public string server { get; set; }
        public string be { get; set; }
        public string rp { get; set; }
        public string rank { get; set; }
        public string champions { get; set; }
        public string skins { get; set; }
        public string Loot { get; set; }
    }

    public static List<usernamelist> bulkadd = new List<usernamelist>();

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        new Window2().ShowDialog();
        using (StreamReader reader = new StreamReader(Directory.GetCurrentDirectory() + "/List.csv"))
        {
            using CsvReader csvReader = new CsvReader(reader, config);
            IEnumerable<Champs> records = csvReader.GetRecords<Champs>();
            jotain = records.ToList();
        }
        foreach (usernamelist item in bulkadd)
        {
            jotain.Add(new Champs
            {
                username = item.username,
                password = item.password
            });
        }
        using StreamWriter writer = new StreamWriter(Directory.GetCurrentDirectory() + "/List.csv");
        using CsvWriter csvWriter = new CsvWriter(writer, config);
        csvWriter.WriteRecords(jotain);
    }
}