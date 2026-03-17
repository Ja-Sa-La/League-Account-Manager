using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace League_Account_Manager.Windows;

public partial class AppMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    public AppMessageBox(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();

        Title = string.IsNullOrWhiteSpace(caption) ? "League Account Manager" : caption;
        MessageTextBlock.Text = messageBoxText;

        IconTextBlock.Text = GetIconGlyph(icon);
        IconTextBlock.Foreground = GetIconBrush(icon);
        IconTextBlock.Visibility = string.IsNullOrWhiteSpace(IconTextBlock.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        BuildButtons(button);
    }

    public static MessageBoxResult Show(string messageBoxText)
    {
        return Show(messageBoxText, "League Account Manager", MessageBoxButton.OK, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button,
        MessageBoxImage icon)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            return Application.Current.Dispatcher.Invoke(() => ShowCore(messageBoxText, caption, button, icon));

        return ShowCore(messageBoxText, caption, button, icon);
    }

    private static MessageBoxResult ShowCore(string messageBoxText, string caption, MessageBoxButton button,
        MessageBoxImage icon)
    {
        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

        var dialog = new AppMessageBox(messageBoxText, caption, button, icon)
        {
            WindowStartupLocation = owner != null && owner.IsVisible
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };

        if (owner != null && owner.IsVisible)
            dialog.Owner = owner;

        dialog.ShowDialog();
        return dialog._result;
    }

    private void BuildButtons(MessageBoxButton button)
    {
        foreach (var buttonConfig in GetButtonConfigs(button))
        {
            var buttonControl = new Button
            {
                Content = buttonConfig.Text,
                Style = (Style)FindResource("DialogButtonStyle"),
                IsDefault = buttonConfig.IsDefault,
                IsCancel = buttonConfig.IsCancel
            };

            buttonControl.Click += (_, _) =>
            {
                _result = buttonConfig.Result;
                DialogResult = true;
            };

            ButtonsPanel.Children.Add(buttonControl);
        }

        if (ButtonsPanel.Children.Count > 0 && ButtonsPanel.Children[^1] is Button lastButton)
            lastButton.Margin = new Thickness(0);
    }

    private static IEnumerable<(string Text, MessageBoxResult Result, bool IsDefault, bool IsCancel)>
        GetButtonConfigs(MessageBoxButton button)
    {
        return button switch
        {
            MessageBoxButton.OK => new[] { ("OK", MessageBoxResult.OK, true, true) },
            MessageBoxButton.OKCancel => new[]
            {
                ("OK", MessageBoxResult.OK, true, false),
                ("Cancel", MessageBoxResult.Cancel, false, true)
            },
            MessageBoxButton.YesNo => new[]
            {
                ("Yes", MessageBoxResult.Yes, true, false),
                ("No", MessageBoxResult.No, false, true)
            },
            MessageBoxButton.YesNoCancel => new[]
            {
                ("Yes", MessageBoxResult.Yes, true, false),
                ("No", MessageBoxResult.No, false, false),
                ("Cancel", MessageBoxResult.Cancel, false, true)
            },
            _ => new[] { ("OK", MessageBoxResult.OK, true, true) }
        };
    }

    private static string GetIconGlyph(MessageBoxImage icon)
    {
        return icon switch
        {
            MessageBoxImage.Error => "✖",
            MessageBoxImage.Warning => "⚠",
            MessageBoxImage.Question => "?",
            MessageBoxImage.Information => "ℹ",
            _ => string.Empty
        };
    }

    private static Brush GetIconBrush(MessageBoxImage icon)
    {
        return icon switch
        {
            MessageBoxImage.Error => Brushes.IndianRed,
            MessageBoxImage.Warning => Brushes.Gold,
            MessageBoxImage.Question => Brushes.LightSkyBlue,
            MessageBoxImage.Information => Brushes.DeepSkyBlue,
            _ => Brushes.Transparent
        };
    }
}
