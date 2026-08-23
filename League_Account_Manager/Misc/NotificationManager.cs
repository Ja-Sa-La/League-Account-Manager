using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using League_Account_Manager;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;

namespace Notification.Wpf;

public enum NotificationType
{
    Notification,
    Information,
    Success,
    Warning,
    Error
}

public enum NotificationTextTrimType
{
    Trim,
    NoTrim
}

public sealed class NotificationContent
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.Notification;
}

public sealed class NotificationManager
{
    public void Show(NotificationContent content)
    {
        Show(content.Title, content.Message, content.Type);
    }

    public void Show(string title, string message, NotificationType type)
    {
        Show(title, message, type, "WindowArea", null);
    }

    public void Show(string title, string message, NotificationType type, string? areaName,
        TimeSpan? expirationTime = null, object? icon = null, object? onClose = null,
        Action? onClick = null, string? onClickText = null, Action? secondOnClick = null,
        string? secondOnClickText = null, NotificationTextTrimType trimType = NotificationTextTrimType.Trim,
        uint maxTextLength = 0, bool canClose = true, object? tag = null, object? data = null,
        bool showCloseButton = true)
    {
        var timeout = expirationTime ?? TimeSpan.FromSeconds(5);
        NotificationHost? host = null;

        RunOnUi(() =>
        {
            host = (Application.Current?.MainWindow as MainWindow)?.WindowArea;
            host?.ShowNotification(title, message, type, timeout, onClick, onClickText, secondOnClick,
                secondOnClickText, canClose && showCloseButton);
        });

        if (host == null)
            ShowNativeToast(title, message, type);
    }

    public void ShowNative(string title, string message, NotificationType type = NotificationType.Notification)
    {
        ShowNativeToast(title, message, type);
    }

    public MessageBoxResult ShowModal(string message, string title = "League Account Manager",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        return AppMessageBox.Show(message, title, buttons, icon);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    private static void ShowNativeToast(string title, string message, NotificationType type)
    {
        try
        {
            var xml = $"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text>" +
                      $"<text>{SecurityElement.Escape(message)}</text></binding></visual></toast>";
            var script = "$xml = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, " +
                         "ContentType = WindowsRuntime]::new(); " +
                         $"$xml.LoadXml('{EscapePowerShell(xml)}'); " +
                         "$toast = [Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime]::new($xml); " +
                         "$notifier = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]::CreateToastNotifier('League Account Manager'); " +
                         "$notifier.Show($toast)";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[Notifications] Native Windows notification unavailable: {ex.Message}",
                ConsoleColor.DarkYellow);
        }
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''").Replace("`", "``");
    }
}

public sealed class NotificationHost : Border
{
    private readonly StackPanel _panel = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        MaxWidth = 430
    };

    private readonly ConcurrentDictionary<Guid, Border> _active = new();

    public NotificationHost()
    {
        Padding = new Thickness(0, 12, 18, 0);
        Child = _panel;
    }

    public void ShowNotification(string title, string message, NotificationType type, TimeSpan timeout,
        Action? onClick, string? onClickText, Action? secondOnClick, string? secondOnClickText,
        bool canClose)
    {
        var id = Guid.NewGuid();
        var card = CreateCard(id, title, message, type, onClick, onClickText, secondOnClick, secondOnClickText,
            canClose);
        _active[id] = card;
        _panel.Children.Insert(0, card);

        _ = Task.Run(async () =>
        {
            await Task.Delay(timeout);
            await Dispatcher.InvokeAsync(() => Remove(id));
        });
    }

    private Border CreateCard(Guid id, string title, string message, NotificationType type, Action? onClick,
        string? onClickText, Action? secondOnClick, string? secondOnClickText, bool canClose)
    {
        var accent = type switch
        {
            NotificationType.Error => (Brush)Application.Current.FindResource("DangerBrush"),
            NotificationType.Warning => (Brush)Application.Current.FindResource("WarningBrush"),
            NotificationType.Success => (Brush)Application.Current.FindResource("SuccessBrush"),
            _ => (Brush)Application.Current.FindResource("AccentBrush")
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush")
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        AddAction(actions, onClick, onClickText);
        AddAction(actions, secondOnClick, secondOnClickText);
        if (actions.Children.Count > 0)
            content.Children.Add(actions);

        var close = new Button
        {
            Content = "×",
            FontSize = 16,
            Padding = new Thickness(5, 0, 5, 0),
            Background = Brushes.Transparent,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = canClose ? Visibility.Visible : Visibility.Collapsed
        };
        close.Click += (_, _) => Remove(id);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(content, 0);
        Grid.SetColumn(close, 1);
        grid.Children.Add(content);
        grid.Children.Add(close);

        return new Border
        {
            Child = grid,
            Background = (Brush)Application.Current.FindResource("SurfaceRaisedBrush"),
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 11, 8, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Width = 410,
            IsHitTestVisible = true
        };
    }

    private static void AddAction(Panel panel, Action? action, string? text)
    {
        if (action == null || string.IsNullOrWhiteSpace(text))
            return;

        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 3, 8, 3)
        };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private void Remove(Guid id)
    {
        if (_active.TryRemove(id, out var card))
            _panel.Children.Remove(card);
    }
}