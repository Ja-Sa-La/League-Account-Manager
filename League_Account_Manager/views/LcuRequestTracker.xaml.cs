using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using League_Account_Manager.Misc;
using Microsoft.Win32;

namespace League_Account_Manager.views;

public partial class LcuRequestTracker : Page
{
    private readonly ObservableCollection<TrafficRow> _rows = [];
    private readonly ICollectionView _view;
    private bool _allSelected;

    public LcuRequestTracker()
    {
        InitializeComponent();
        foreach (var record in LcuRequestLog.Snapshot())
            _rows.Add(new TrafficRow(record));

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterTraffic;
        TrafficGrid.ItemsSource = _view;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateStatus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LcuRequestLog.RequestCompleted += OnRequestCompleted;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LcuRequestLog.RequestCompleted -= OnRequestCompleted;
    }

    private void OnRequestCompleted(object? sender, LcuRequestRecord record)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _rows.Add(new TrafficRow(record));
            if (_rows.Count > 1000)
                _rows.RemoveAt(0);
            UpdateStatus();
        });
    }

    private bool FilterTraffic(object item)
    {
        if (item is not TrafficRow row)
            return false;

        var selectedType = (TrafficTypeFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var matchesType = selectedType == "HTTP"
            ? row.Record.TrafficType is "HTTP" or "REST"
            : selectedType == "All" || string.Equals(row.Record.TrafficType, selectedType,
                StringComparison.OrdinalIgnoreCase);
        if (!matchesType)
            return false;

        var search = SearchText.Text.Trim();
        return string.IsNullOrEmpty(search) ||
               row.Record.Endpoint.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Record.Method.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Record.RequestBody.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Record.ResponseBody.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchText_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateStatus();
    }

    private void TrafficTypeFilter_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateStatus();
    }

    private void TrafficGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TrafficGrid.SelectedItem is not TrafficRow row)
        {
            RequestDetails.Clear();
            ResponseDetails.Clear();
            return;
        }

        RequestDetails.Text = string.IsNullOrWhiteSpace(row.Record.RequestBody)
            ? FormatRequestDetails(row.Record)
            : $"{FormatRequestDetails(row.Record)}{Environment.NewLine}{Environment.NewLine}{row.Record.RequestBody}";
        ResponseDetails.Text = string.IsNullOrWhiteSpace(row.Record.Error)
            ? row.Record.ResponseBody
            : $"{row.Record.Error}{Environment.NewLine}{Environment.NewLine}{row.Record.ResponseBody}";
    }

    private static string FormatRequestDetails(LcuRequestRecord record)
    {
        var firstLine = record.TrafficType == "WebSocket"
            ? $"WebSocket {record.Direction} {record.EventType} {record.Endpoint}"
            : $"{record.Direction} {record.Method} {record.Endpoint}";

        return string.IsNullOrWhiteSpace(record.RequestHeaders)
            ? firstLine
            : $"{firstLine}{Environment.NewLine}{record.RequestHeaders}";
    }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        _allSelected = !_allSelected;
        foreach (var row in _view.Cast<TrafficRow>())
            row.IsSelected = _allSelected;
    }

    private async void ExportSelected_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(row => row.IsSelected).Select(row => row.Record).ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "Select at least one traffic item to export.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"lcu-traffic-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog() != true)
            return;

        var json = JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(dialog.FileName, json);
        StatusText.Text = $"Exported {selected.Length} selected item(s).";
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        LcuRequestLog.Clear();
        _rows.Clear();
        RequestDetails.Clear();
        ResponseDetails.Clear();
        _allSelected = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (StatusText == null || _view == null)
            return;

        StatusText.Text = $"{_view.Cast<object>().Count()} visible / {_rows.Count} captured";
    }

    private sealed class TrafficRow(LcuRequestRecord record) : INotifyPropertyChanged
    {
        private bool _isSelected;

        public LcuRequestRecord Record { get; } = record;
        public string Time => Record.Timestamp.ToString("HH:mm:ss.fff");
        public string Status => Record.StatusCode.HasValue ? $"{Record.StatusCode} {Record.Status}" : Record.Status;
        public string Duration => Record.DurationMilliseconds > 0 ? $"{Record.DurationMilliseconds} ms" : string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}