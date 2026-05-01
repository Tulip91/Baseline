using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.ViewModels;

public abstract class PageViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    protected PageViewModelBase(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }
    public string Description { get; }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public virtual Task InitializeAsync() => Task.CompletedTask;
}

public sealed class NavigationItemViewModel : ObservableObject
{
    private bool _isSelected;

    public NavigationItemViewModel(string label, string caption, PageViewModelBase page, Action<NavigationItemViewModel> selectAction)
    {
        Label = label;
        Caption = caption;
        Page = page;
        SelectCommand = new RelayCommand(() => selectAction(this));
    }

    public string Label { get; }
    public string Caption { get; }
    public PageViewModelBase Page { get; }
    public RelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class CategorySelectionItemViewModel : ObservableObject
{
    private bool _isSelected;

    public CategorySelectionItemViewModel(ProfileCategory category, string displayName, string description, bool isSelected = true)
    {
        Category = category;
        DisplayName = displayName;
        Description = description;
        _isSelected = isSelected;
    }

    public ProfileCategory Category { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class RegistryTemplateSelectionViewModel : ObservableObject
{
    private bool _isSelected;

    public RegistryTemplateSelectionViewModel(StructuredRegistryTemplate template)
    {
        Template = template;
        _isSelected = template.IsDefaultSelected || template.IsCustom;
    }

    public StructuredRegistryTemplate Template { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class CompareRowViewModel : ObservableObject
{
    private bool _isSelected;

    public CompareRowViewModel(CompareItem item, bool selected = false)
    {
        Item = item;
        _isSelected = selected;
    }

    public CompareItem Item { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class SimpleRecordViewModel : ObservableObject
{
    public SimpleRecordViewModel(string title, string subtitle, string detail)
    {
        Title = title;
        Subtitle = subtitle;
        Detail = detail;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Detail { get; }
}

public sealed class CompareSummaryViewModel : ObservableObject
{
    private int _readyCount;
    private int _matchedCount;
    private int _warningCount;
    private int _unsupportedCount;

    public int ReadyCount
    {
        get => _readyCount;
        set => SetProperty(ref _readyCount, value);
    }

    public int MatchedCount
    {
        get => _matchedCount;
        set => SetProperty(ref _matchedCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        set => SetProperty(ref _warningCount, value);
    }

    public int UnsupportedCount
    {
        get => _unsupportedCount;
        set => SetProperty(ref _unsupportedCount, value);
    }
}
