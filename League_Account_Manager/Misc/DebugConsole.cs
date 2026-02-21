using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace League_Account_Manager.Misc;

public static class DebugConsole
{
    private static DebugConsoleWindow? _window;
    private static DebugConsoleWriter? _writer;

    public static void WriteLine(string message, ConsoleColor color = ConsoleColor.White)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = previous;
    }

    public static void Initialize(Window owner)
    {
        if (_window != null)
            return;

        _window = new DebugConsoleWindow
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        _writer = new DebugConsoleWriter((text, color) => _window?.AppendLine(text, color));
        Console.SetOut(_writer);
        Console.SetError(_writer);
    }

    public static void ToggleVisibility()
    {
        if (_window == null)
            return;

        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
            _window.Activate();
        }
    }
}

internal sealed class DebugConsoleWriter : TextWriter
{
    private readonly Action<string, ConsoleColor> _append;
    private readonly StringBuilder _buffer = new();

    public DebugConsoleWriter(Action<string, ConsoleColor> append)
    {
        _append = append;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        _buffer.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        foreach (var c in value)
            Write(c);
    }

    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _buffer.Append(value);

        _buffer.Append(Environment.NewLine);
        FlushBuffer();
    }

    public override void Flush()
    {
        FlushBuffer();
    }

    private void FlushBuffer()
    {
        if (_buffer.Length == 0)
            return;

        var text = _buffer.ToString();
        _buffer.Clear();
        _append(text, Console.ForegroundColor);
    }
}

internal class DebugConsoleWindow : Window
{
    private const int MaxLength = 500;
    private readonly TextBox _commandBox;
    private readonly KeyGesture _keyGesture;
    private readonly StackPanel _outputPanel;

    private ConsoleEntry? _lastEntry;

    public DebugConsoleWindow()
    {
        Title = "LAM Console";
        Width = 900;
        Height = 450;
        Background = Brushes.Black;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResize;
        _keyGesture = new KeyGesture(Key.F12);

        _outputPanel = new StackPanel
        {
            Background = Brushes.Black
        };

        var scrollViewer = new ScrollViewer
        {
            Content = _outputPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Black
        };

        _commandBox = new TextBox
        {
            Margin = new Thickness(6, 4, 6, 4),
            Background = Brushes.Black,
            Foreground = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        };

        var sendButton = new Button
        {
            Content = "Send",
            Margin = new Thickness(0, 4, 6, 4),
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        sendButton.Click += async (_, _) => await ExecuteCommandAsync(_commandBox.Text);
        _commandBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await ExecuteCommandAsync(_commandBox.Text);
                e.Handled = true;
            }
        };

        var commandPanel = new DockPanel
        {
            LastChildFill = true,
            Background = Brushes.Black
        };

        DockPanel.SetDock(sendButton, Dock.Right);
        commandPanel.Children.Add(sendButton);
        commandPanel.Children.Add(_commandBox);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(scrollViewer, 0);
        Grid.SetRow(commandPanel, 1);

        layout.Children.Add(scrollViewer);
        layout.Children.Add(commandPanel);

        Content = layout;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e != null && _keyGesture.Matches(this, e))
        {
            Hide();
            e.Handled = true;
        }
    }

    public void AppendLine(string text, ConsoleColor color)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var normalized = text?.TrimEnd('\r', '\n') ?? string.Empty;

            if (_lastEntry is { } last && last.FullText == normalized && last.Color == color)
            {
                last.Count++;
                UpdateEntryVisual(last);
            }
            else
            {
                var entry = CreateEntry(normalized, color);
                _lastEntry = entry;
                _outputPanel.Children.Add(entry.Container);
            }

            if (_outputPanel.Parent is ScrollViewer sv) sv.ScrollToEnd();
        });
    }

    private ConsoleEntry CreateEntry(string text, ConsoleColor color)
    {
        var entry = new ConsoleEntry
        {
            FullText = text,
            Color = color,
            Count = 1,
            IsExpanded = false,
            IsTruncated = text?.Length > MaxLength
        };

        var border = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = ColorToBrush(color),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                AcceptsReturn = true
            }
        };

        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                ToggleExpand(entry);
                e.Handled = true;
            }
        };
        border.MouseRightButtonUp += (_, _) => Clipboard.SetText(entry.FullText);

        if (border.Child is TextBox tb)
            tb.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    ToggleExpand(entry);
                    e.Handled = true;
                }
            };

        entry.Container = border;
        UpdateEntryVisual(entry);
        return entry;
    }

    private void ToggleExpand(ConsoleEntry entry)
    {
        if (!entry.IsTruncated && entry.Count <= 1)
            return;

        entry.IsExpanded = !entry.IsExpanded;
        UpdateEntryVisual(entry);
    }

    private void UpdateEntryVisual(ConsoleEntry entry)
    {
        if (entry.Container.Child is not TextBox textBlock)
            return;

        var baseText = entry.IsExpanded || !entry.IsTruncated
            ? entry.FullText
            : $"{entry.FullText[..Math.Min(entry.FullText.Length, MaxLength)]}… (click to expand)";

        if (entry.IsTruncated && entry.IsExpanded)
            baseText += " (click to collapse)";

        var countSuffix = entry.Count > 1 ? $" (x{entry.Count})" : string.Empty;
        textBlock.Text = baseText + countSuffix;
        textBlock.Foreground = ColorToBrush(entry.Color);
    }

    private async Task ExecuteCommandAsync(string? command)
    {
        var input = (command ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
            return;

        _commandBox.Clear();

        try
        {
            // Expected format: target method endpoint [data]
            var parts = input.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                AppendLine("[Console] Usage: <target> <METHOD> <endpoint> [data]", ConsoleColor.Yellow);
                return;
            }

            var target = parts[0].Trim();
            var method = parts[1].Trim();
            var endpoint = parts[2].Trim();
            var data = parts.Length == 4 ? parts[3] : string.Empty;

            AppendLine($"[Console] -> {target} {method.ToUpperInvariant()} {endpoint} {data}", ConsoleColor.Cyan);

            if (!string.IsNullOrWhiteSpace(data))
            {
                var formatted = TryFormatJson(data);
                AppendLine($"[Console] payload:\n{formatted}", ConsoleColor.DarkCyan);
            }

            var result = await Lcu.Connector(target, method, endpoint, data);

            if (result is HttpResponseMessage resp)
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                AppendLine($"[Console] <- {(int)resp.StatusCode} {resp.ReasonPhrase}", ConsoleColor.Green);
                AppendLine(body, ConsoleColor.Gray);
            }
            else
            {
                AppendLine($"[Console] <- {result}", ConsoleColor.Gray);
            }
        }
        catch (Exception ex)
        {
            AppendLine($"[Console] Error: {ex.Message}", ConsoleColor.Red);
        }
    }

    private static string TryFormatJson(string input)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return input;
        }
    }

    private static Brush ColorToBrush(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Black => Brushes.Black,
            ConsoleColor.DarkBlue => Brushes.DarkBlue,
            ConsoleColor.DarkGreen => Brushes.DarkGreen,
            ConsoleColor.DarkCyan => Brushes.DarkCyan,
            ConsoleColor.DarkRed => Brushes.DarkRed,
            ConsoleColor.DarkMagenta => Brushes.DarkMagenta,
            ConsoleColor.DarkYellow => Brushes.Olive,
            ConsoleColor.Gray => Brushes.Gray,
            ConsoleColor.DarkGray => Brushes.DarkGray,
            ConsoleColor.Blue => Brushes.Blue,
            ConsoleColor.Green => Brushes.Green,
            ConsoleColor.Cyan => Brushes.Cyan,
            ConsoleColor.Red => Brushes.Red,
            ConsoleColor.Magenta => Brushes.Magenta,
            ConsoleColor.Yellow => Brushes.Yellow,
            ConsoleColor.White => Brushes.White,
            _ => Brushes.White
        };
    }

    private sealed class ConsoleEntry
    {
        public string FullText { get; set; } = string.Empty;
        public ConsoleColor Color { get; set; }
        public int Count { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsTruncated { get; set; }
        public Border Container { get; set; } = null!;
    }
}