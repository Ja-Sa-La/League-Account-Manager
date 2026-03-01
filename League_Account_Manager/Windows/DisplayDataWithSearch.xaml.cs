using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using System.Collections.Generic;
using System;

namespace League_Account_Manager.Windows;

/// <summary>
///     Interaction logic for DisplayDataWithSearch.xaml
/// </summary>
public partial class DisplayDataWithSearch : Window
{
    private readonly string dataholder = "";
    private List<DisplayItem> items = new();

    private sealed class DisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public string? IconUrl { get; set; }
        public string? Price { get; set; }
        public object? IconSource
        {
            get
            {
                if (string.IsNullOrWhiteSpace(IconUrl)) return null;
                try
                {
                    // If stored without scheme, prepend https:// to form a valid absolute URI
                    var url = IconUrl!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || IconUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        ? IconUrl
                        : "https://" + IconUrl;

                    return new System.Windows.Media.Imaging.BitmapImage(new Uri(url));
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    public DisplayDataWithSearch(string? Data)
    {
        InitializeComponent();

        if (string.IsNullOrWhiteSpace(Data))
            return;

        // Items in the input are separated by ':' as before. However icon URLs contain '://', which
        // would create extra ':' tokens if we simply split on ':'. To handle this we tokenize by
        // splitting on ':' only when we've already collected the expected number of '|' separators
        // (we generate items as "name|iconUrl|price" so there are two '|' per item). If no '|' is
        // present in the input we fall back to simple ':' splitting for compatibility.
        dataholder = Data.Replace("\r", "").Trim();

        // Split by ':' first and then recombine tokens until we have a complete item. A complete
        // item is considered to contain two '|' separators (name|url|price). This allows ':' to
        // appear inside URLs without breaking parsing.
        var rawTokens = dataholder.Split(new[] { ':' }, StringSplitOptions.None);
        var lines = new List<string>();
        for (var i = 0; i < rawTokens.Length; i++)
        {
            var current = rawTokens[i];
            var pipeCount = current.Count(c => c == '|');
            while (pipeCount < 2 && i + 1 < rawTokens.Length)
            {
                i++;
                current = current + ":" + rawTokens[i];
                pipeCount = current.Count(c => c == '|');
            }

            current = current.Trim();
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
        }

        foreach (var line in lines)
        {
            // Support multiple formats: pipe-delimited (name|url|price) or hyphen-delimited (name-url-price)
            string name = line;
            string? url = null;
            string? price = null;

            if (line.Contains("|"))
            {
                var parts = line.Split('|');
                if (parts.Length >= 1) name = parts[0].Trim();
                if (parts.Length >= 2) url = parts[1].Trim();
                if (parts.Length >= 3) price = parts[2].Trim();
            }
            else if (line.Contains("-"))
            {
                // split by last '-' to get url, and second last for price if present
                var last = line.LastIndexOf('-');
                if (last > 0)
                {
                    url = line.Substring(last + 1).Trim();
                    var rest = line.Substring(0, last).Trim();
                    var secondLast = rest.LastIndexOf('-');
                    if (secondLast > 0)
                    {
                        price = rest.Substring(secondLast + 1).Trim();
                        name = rest.Substring(0, secondLast).Trim();
                    }
                    else
                    {
                        name = rest;
                    }
                }
            }

            items.Add(new DisplayItem { Name = name, IconUrl = string.IsNullOrWhiteSpace(url) ? null : url, Price = price });
        }

        ItemsList.ItemsSource = items;
    }


    private void Window_MouseDownDatadisplay(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Close();
    }


    private void TextBox_TextChangeddatafilt(object sender, TextChangedEventArgs e)
    {
        var searchTerm = datafiltersearch.Text ?? string.Empty;
        var filtered = items.Where(it => it.Name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        ItemsList.ItemsSource = filtered;
    }
}