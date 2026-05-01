using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using BaseLine.ViewModels;

namespace BaseLine.Views;

public partial class MainWindow : Window
{
    private readonly DoubleAnimation _pageFadeAnimation = new()
    {
        From = 0,
        To = 1,
        Duration = TimeSpan.FromMilliseconds(140),
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
    };

    private ShellViewModel? _shellViewModel;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shellViewModel)
        {
            AttachShellViewModel(shellViewModel);
            await shellViewModel.InitializeAsync();
            AnimateCurrentPage();
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e) => ToggleWindowState();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void AttachShellViewModel(ShellViewModel shellViewModel)
    {
        if (ReferenceEquals(_shellViewModel, shellViewModel))
        {
            return;
        }

        if (_shellViewModel is not null)
        {
            _shellViewModel.PropertyChanged -= ShellViewModelOnPropertyChanged;
        }

        _shellViewModel = shellViewModel;
        _shellViewModel.PropertyChanged += ShellViewModelOnPropertyChanged;
    }

    private void ShellViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
        {
            AnimateCurrentPage();
        }
    }

    private void AnimateCurrentPage()
    {
        PageHost.BeginAnimation(OpacityProperty, null);
        PageHost.Opacity = 0;
        PageHost.BeginAnimation(OpacityProperty, _pageFadeAnimation);
    }
}
