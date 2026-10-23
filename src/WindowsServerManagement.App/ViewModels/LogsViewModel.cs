using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class LogsViewModel : ObservableObject
{
    private readonly IActivityLogService _activity;
    private readonly object _sync = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedStatus = "All";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filteredCountText = "0 entries";

    public LogsViewModel(IActivityLogService activity)
    {
        _activity = activity;
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;
        _activity.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<ActivityLogEntry> Entries { get; } = [];
    public ICollectionView EntriesView { get; }
    public IReadOnlyList<string> StatusOptions { get; } = ["All", "Info", "Success", "Warning", "Error"];
    public bool HasNoEntries => Entries.Count == 0;
    public bool HasVisibleEntries => EntriesView.Cast<ActivityLogEntry>().Any();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var entries = await _activity.GetRecentAsync(1000, cancellationToken).ConfigureAwait(true);
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            EntriesView.Refresh();
            UpdateCount();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatus = "All";
        EntriesView.Refresh();
        UpdateCount();
    }

    partial void OnSearchTextChanged(string value)
    {
        EntriesView.Refresh();
        UpdateCount();
    }

    partial void OnSelectedStatusChanged(string value)
    {
        EntriesView.Refresh();
        UpdateCount();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not ActivityLogEntry entry)
        {
            return false;
        }

        if (!string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry.Status.ToString(), SelectedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return entry.ServerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.Operation.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (entry.Details?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               entry.TimestampText.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void OnEntryAdded(object? sender, ActivityLogEntry entry)
    {
        RunOnUi(() =>
        {
            lock (_sync)
            {
                Entries.Insert(0, entry);
                while (Entries.Count > 2000)
                {
                    Entries.RemoveAt(Entries.Count - 1);
                }
            }

            EntriesView.Refresh();
            UpdateCount();
        });
    }

    private void UpdateCount()
    {
        var visible = EntriesView.Cast<ActivityLogEntry>().Count();
        FilteredCountText = visible == Entries.Count
            ? $"{visible} entries"
            : $"{visible} of {Entries.Count} entries";
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(HasVisibleEntries));
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
