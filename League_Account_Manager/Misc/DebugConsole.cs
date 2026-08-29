using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace League_Account_Manager.Misc;

public static class 
    DebugConsole
{
    private static DebugConsoleWindow? _window;
    private static DebugConsoleWriter? _writer;
    private static readonly TextWriter _originalOut = Console.Out;
    private static readonly object _sync = new();
    private static readonly Queue<(string Message, ConsoleColor Color)> _pending = new();
    private const int MaxPendingEntries = 1000;

    public static void WriteLine(string message, ConsoleColor color = ConsoleColor.White)
    {
        Debug.WriteLine(message);

        DebugConsoleWindow? window;

        lock (_sync)
        {
            if (_window == null)
            {
                _pending.Enqueue((message, color));
                while (_pending.Count > MaxPendingEntries)
                    _pending.Dequeue();

                var previous = Console.ForegroundColor;
                Console.ForegroundColor = color;
                _originalOut.WriteLine(message);
                Console.ForegroundColor = previous;
                return;
            }

            window = _window;
        }

        window.AppendLine(message, color);
    }

    public static void Initialize(Window owner)
    {
        lock (_sync)
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

            while (_pending.Count > 0)
            {
                var (message, color) = _pending.Dequeue();
                _window.AppendLine(message, color);
            }
        }
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
    private const int MaxEntries = 2000;
    private readonly TextBox _commandBox;
    private readonly Button _clearButton;
    private readonly Button _copyAllButton;
    private readonly Button _exportButton;
    private readonly KeyGesture _keyGesture;
    private readonly StackPanel _outputPanel;
    private readonly ScrollViewer _scrollViewer;

    private ConsoleEntry? _lastEntry;

    public DebugConsoleWindow()
    {
        Title = "LAM Console";
        Width = 1000;
        Height = 600;
        Background = new SolidColorBrush(Color.FromRgb(16, 19, 21));
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResize;
        _keyGesture = new KeyGesture(Key.F12);

        _outputPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 28, 31))
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _outputPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(24, 28, 31)),
            Padding = new Thickness(8)
        };

        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(16, 19, 21)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 59, 64)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        var titleText = new TextBlock
        {
            Text = "LAM Console",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 248)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var subtitleText = new TextBlock
        {
            Text = " • Press F12 to toggle",
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 184, 190)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        titleStack.Children.Add(titleText);
        titleStack.Children.Add(subtitleText);
        titleBar.Child = titleStack;

        _commandBox = new TextBox
        {
            Margin = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(32, 37, 41)),
            Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 59, 64)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var sendButton = new Button
        {
            Content = "Send",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(77, 163, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(77, 163, 255)),
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            MinWidth = 80
        };

        _clearButton = new Button
        {
            Content = "Clear",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(32, 37, 41)),
            Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 59, 64)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            MinWidth = 80
        };

        _copyAllButton = new Button
        {
            Content = "Copy All",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(32, 37, 41)),
            Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 59, 64)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            MinWidth = 80
        };

        _exportButton = new Button
        {
            Content = "Export",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(32, 37, 41)),
            Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 59, 64)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            MinWidth = 80
        };

        sendButton.Click += async (_, _) => await ExecuteCommandAsync(_commandBox.Text);
        _clearButton.Click += (_, _) => ClearConsole();
        _copyAllButton.Click += (_, _) => CopyAllLogs();
        _exportButton.Click += (_, _) => ExportLogs();

        _commandBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await ExecuteCommandAsync(_commandBox.Text);
                e.Handled = true;
            }
        };

        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonStack.Children.Add(_clearButton);
        buttonStack.Children.Add(_copyAllButton);
        buttonStack.Children.Add(_exportButton);
        buttonStack.Children.Add(sendButton);

        var commandPanel = new DockPanel
        {
            LastChildFill = true,
            Background = new SolidColorBrush(Color.FromRgb(16, 19, 21)),
            Margin = new Thickness(12)
        };

        DockPanel.SetDock(buttonStack, Dock.Right);
        commandPanel.Children.Add(buttonStack);
        commandPanel.Children.Add(_commandBox);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(titleBar, 0);
        Grid.SetRow(_scrollViewer, 1);
        Grid.SetRow(commandPanel, 2);

        layout.Children.Add(titleBar);
        layout.Children.Add(_scrollViewer);
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
                while (_outputPanel.Children.Count > MaxEntries)
                    _outputPanel.Children.RemoveAt(0);
            }

            _scrollViewer.ScrollToEnd();
        });
    }

    private void ClearConsole()
    {
        _outputPanel.Children.Clear();
        _lastEntry = null;
    }

    private void CopyAllLogs()
    {
        var text = GetAllLogsText();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void ExportLogs()
    {
        var text = GetAllLogsText();
        if (string.IsNullOrEmpty(text))
            return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"LAM-Console-{DateTime.Now:yyyy-MM-dd_HHmmss}.log",
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".log"
        };

        if (dialog.ShowDialog(this) == true)
            File.WriteAllText(dialog.FileName, text);
    }

    private string GetAllLogsText()
    {
        var builder = new StringBuilder();
        foreach (var child in _outputPanel.Children)
            if (child is Border { Tag: ConsoleEntry entry })
            {
                builder.Append(entry.FullText);
                if (entry.Count > 1)
                    builder.Append($" (x{entry.Count})");
                builder.Append(Environment.NewLine);
            }

        return builder.ToString();
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

        var textBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ColorToBrush(color),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsReadOnly = true,
            AcceptsReturn = true,
            Cursor = Cursors.Arrow
        };

        var border = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 1, 0, 1),
            Tag = entry,
            Child = textBox
        };

        border.MouseEnter += (_, _) => border.Background = new SolidColorBrush(Color.FromRgb(41, 47, 52));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;

        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                ToggleExpand(entry);
                e.Handled = true;
            }
        };
        border.MouseRightButtonUp += (_, _) => Clipboard.SetText(entry.FullText);

        textBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                ToggleExpand(entry);
                e.Handled = true;
            }
        };
        textBox.MouseRightButtonUp += (_, e) =>
        {
            Clipboard.SetText(entry.FullText);
            e.Handled = true;
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