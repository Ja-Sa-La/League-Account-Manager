using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace League_Account_Manager.Windows;

/// <summary>
///     Interaction logic for DisplayDataWithSearch.xaml
/// </summary>
public partial class DisplayDataWithSearch : Window
{
    private readonly string dataholder = "";
    private readonly List<DisplayItem> items = new();
    private HoverPreviewWindow? _previewWindow;

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
            var name = line;
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

            items.Add(new DisplayItem
                { Name = name, IconUrl = string.IsNullOrWhiteSpace(url) ? null : url, Price = price });
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
        if (_previewWindow is { IsVisible: true })
            return;

        Close();
    }


    private void TextBox_TextChangeddatafilt(object sender, TextChangedEventArgs e)
    {
        var searchTerm = datafiltersearch.Text ?? string.Empty;
        var filtered = items.Where(it => it.Name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        ItemsList.ItemsSource = filtered;
    }

    private void OnItemHoverEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (element.DataContext is not DisplayItem item)
            return;

        if (item.IconSource is not ImageSource imageSource)
        {
            HidePreviewWindow();
            return;
        }

        EnsurePreviewWindow();
        UpdatePreviewWindowPlacement();
        _previewWindow!.SetImage(imageSource);
        if (!_previewWindow.IsVisible)
            _previewWindow.Show();
    }

    private void OnItemsListMouseLeave(object sender, MouseEventArgs e)
    {
        HidePreviewWindow();
    }

    private void Window_LocationOrSizeChanged(object sender, EventArgs e)
    {
        UpdatePreviewWindowPlacement();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (_previewWindow != null)
        {
            try
            {
                _previewWindow.Close();
            }
            catch
            {
            }

            _previewWindow = null;
        }
    }

    private void EnsurePreviewWindow()
    {
        if (_previewWindow != null)
            return;

        _previewWindow = new HoverPreviewWindow
        {
            Owner = this
        };
    }

    private void HidePreviewWindow()
    {
        if (_previewWindow == null)
            return;

        _previewWindow.Hide();
        _previewWindow.ClearImage();
    }

    private void UpdatePreviewWindowPlacement()
    {
        if (_previewWindow == null)
            return;

        const double previewWidth = 380;
        const double gap = 12;

        _previewWindow.Width = previewWidth;
        _previewWindow.Height = ActualHeight;
        _previewWindow.Top = Top;
        _previewWindow.Left = Left - previewWidth - gap;
    }

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
                    var url = IconUrl!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                              IconUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        ? IconUrl
                        : "https://" + IconUrl;

                    return new BitmapImage(new Uri(url));
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    private sealed class HoverPreviewWindow : Window
    {
        private readonly Image _previewImage;

        public HoverPreviewWindow()
        {
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Background = (Brush)Application.Current.FindResource("SurfaceBrush");
            BorderBrush = (Brush)Application.Current.FindResource("StrokeBrush");
            BorderThickness = new Thickness(1);

            _previewImage = new Image
            {
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(8)
            };

            Content = _previewImage;
        }

        public void SetImage(ImageSource source)
        {
            _previewImage.Source = source;
        }

        public void ClearImage()
        {
            _previewImage.Source = null;
        }
    }
}