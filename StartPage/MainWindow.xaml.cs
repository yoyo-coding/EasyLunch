using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using StartPage.Models;
using StartPage.Services;
using StartPage.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;

namespace StartPage;

public sealed partial class MainWindow : Window
{
    private const double MinBrandWidth = 720;
    private const double SearchBoxDefaultWidth = 480;
    private const double SearchBoxMinWidth = 200;

    private readonly AppLauncherService _launcherService = new();
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartPage", "settings.json");
    private readonly Dictionary<FrameworkElement, PanelAnimationState> _panelAnimationStates = new();

    private AppWindow? _appWindow;
    private OverlappedPresenter? _overlappedPresenter;
    private CancellationTokenSource? _loadCancellationTokenSource;
    private bool _hasLoaded;
    private AppBackdropMode _backdropMode = AppBackdropMode.Mica;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadWindowSettings();
        ConfigureWindow();
        ApplyBackdrop();

        if (Content is FrameworkElement root)
        {
            root.DataContext = ViewModel;
            root.Loaded += Root_Loaded;
        }

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.AppsChanged += ViewModel_AppsChanged;
        Closed += MainWindow_Closed;
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;

        if (sender is FrameworkElement root)
        {
            root.ActualThemeChanged += Root_ActualThemeChanged;
            UpdateTitleBarDragRegion(root.ActualWidth);
        }

        ApplyBackdrop();
        UpdateWindowCaptionButtons();
        await ReloadAppsAsync();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        ViewModel.AppsChanged -= ViewModel_AppsChanged;

        if (_appWindow is not null)
        {
            _appWindow.Changed -= AppWindow_Changed;
        }
    }

    private void ConfigureWindow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow = appWindow;
            _overlappedPresenter = appWindow.Presenter as OverlappedPresenter;
            _appWindow.Changed += AppWindow_Changed;

            appWindow.Title = "StartPage";
            _overlappedPresenter?.SetBorderAndTitleBar(true, false);
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            appWindow.Resize(new SizeInt32(1100, 760));
            UpdateWindowCaptionButtons();
        }
        catch
        {
            // Cosmetic only.
        }
    }

    private void LoadWindowSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppWindowSettings>(json);
            if (settings is null)
            {
                return;
            }

            if (Enum.TryParse<AppBackdropMode>(settings.BackdropMode, true, out var mode))
            {
                _backdropMode = mode;
            }
        }
        catch
        {
        }
    }

    private void SaveWindowSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new AppWindowSettings
            {
                BackdropMode = _backdropMode.ToString()
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }

    private void ApplyBackdrop()
    {
        try
        {
            Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            switch (_backdropMode)
            {
                case AppBackdropMode.GaussianBlur:
                    // WinUI 3's window-level Acrylic backdrop supplies the glass blur without
                    // using an AcrylicBrush as a root fallback color.
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                    break;

                case AppBackdropMode.Acrylic:
                    // Combine the window-level Acrylic backdrop with an in-app AcrylicBrush.
                    // The latter is the WinUI 3 Acrylic effect and adds a second, richer blur layer.
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                    Root.Background = CreateAcrylicBrush();
                    break;

                case AppBackdropMode.ThinAcrylic:
                    // Keep only the window-level Acrylic layer. It remains translucent but has
                    // less blur than the full Acrylic option, which also uses AcrylicBrush.
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                    break;

                default:
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop
                    {
                        Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt
                    };
                    break;
            }
        }
        catch
        {
            Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop
            {
                Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt
            };
        }
    }

    private AcrylicBrush CreateAcrylicBrush()
    {
        var isDark = IsDarkTheme();

        return new AcrylicBrush
        {
            // Keep tint deliberately light: strong tinting hides the Acrylic blur and appears as gray.
            TintColor = isDark
                ? Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 24)
                : Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255),
            TintOpacity = isDark ? 0.08 : 0.04,
            TintLuminosityOpacity = isDark ? 0.22 : 0.72,
            FallbackColor = isDark
                ? Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)
                : Microsoft.UI.ColorHelper.FromArgb(255, 242, 242, 242)
        };
    }
    private void BackdropModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<AppBackdropMode>(tag, out var mode))
        {
            _backdropMode = mode;
            ApplyBackdrop();
            SaveWindowSettings();
        }
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var titleBarSideVisibility = width >= MinBrandWidth ? Visibility.Visible : Visibility.Collapsed;
        TitleTextPanel.Visibility = titleBarSideVisibility;
        TitleBarArrowButton.Visibility = titleBarSideVisibility;
        SearchBox.Width = Math.Clamp(width - 220, SearchBoxMinWidth, SearchBoxDefaultWidth);
        UpdateTitleBarDragRegion(width);
    }

    private void UpdateTitleBarDragRegion(double width)
    {
        AppTitleBar.Width = Math.Max(0, width);
    }

    private void UpdateWindowCaptionButtons()
    {
        var isMaximized = _overlappedPresenter?.State == OverlappedPresenterState.Maximized;
        MaximizeMenuItem.Text = isMaximized ? "还原" : "最大化";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        _overlappedPresenter?.Minimize();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_overlappedPresenter is null)
        {
            return;
        }

        if (_overlappedPresenter.State == OverlappedPresenterState.Maximized)
        {
            _overlappedPresenter.Restore();
        }
        else
        {
            _overlappedPresenter.Maximize();
        }

        UpdateWindowCaptionButtons();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange || args.DidVisibilityChange)
        {
            UpdateWindowCaptionButtons();
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchText = sender.Text;
        }
    }

    private async void RefreshApps_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAppsAsync(forceRefresh: true);
    }

    private async Task ReloadAppsAsync(bool forceRefresh = false)
    {
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        ViewModel.AppsChanged -= ViewModel_AppsChanged;
        _loadCancellationTokenSource = new CancellationTokenSource();

        await ViewModel.LoadAppsAsync(forceRefresh, _loadCancellationTokenSource.Token);
        ViewModel.ApplyIconMaskTheme(IsDarkTheme());
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyBackdrop();
        ViewModel.ApplyIconMaskTheme(IsDarkTheme());
        UpdateWindowCaptionButtons();
    }

    private bool IsDarkTheme()
    {
        if (Content is FrameworkElement root)
        {
            return root.ActualTheme == ElementTheme.Dark || (root.ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark;
    }

    private async void AppsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppItem item)
        {
            await RunWithErrorDialogAsync(() => _launcherService.LaunchAsync(item), $"无法启动 {item.Name}");
        }
    }

    private async void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuItemApp(sender, out var item))
        {
            await RunWithErrorDialogAsync(() => _launcherService.OpenFileLocationAsync(item), $"无法打开 {item.Name} 的文件位置");
        }
    }

    private async void RunAsAdministrator_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuItemApp(sender, out var item))
        {
            if (item.LaunchKind == AppLaunchKind.UwpAumid)
            {
                await ShowErrorAsync("不支持的操作", "Microsoft Store/UWP 应用不支持通过此菜单以管理员身份运行。");
                return;
            }

            await RunWithErrorDialogAsync(() => _launcherService.LaunchAsync(item, runAsAdministrator: true), $"无法以管理员身份运行 {item.Name}");
        }
    }

    private static bool TryGetMenuItemApp(object sender, out AppItem item)
    {
        if (sender is FrameworkElement { Tag: AppItem appItem })
        {
            item = appItem;
            return true;
        }

        item = null!;
        return false;
    }

    private void AppTile_PointerEntered(object sender, PointerRoutedEventArgs e) => SetTileScale(sender, 1.06);
    private void AppTile_PointerExited(object sender, PointerRoutedEventArgs e) => SetTileScale(sender, 1.0);
    private void AppTile_PointerPressed(object sender, PointerRoutedEventArgs e) => SetTileScale(sender, 0.96);
    private void AppTile_PointerReleased(object sender, PointerRoutedEventArgs e) => SetTileScale(sender, 1.06);

    private static void SetTileScale(object sender, double scale)
    {
        if (sender is FrameworkElement { RenderTransform: ScaleTransform transform } element)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
            element.Opacity = scale < 1 ? 0.84 : 1;
        }
    }

    private void AppsGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Phase != 0)
        {
            return;
        }

        if (args.ItemContainer.ContentTemplateRoot is FrameworkElement { RenderTransform: ScaleTransform scale } tile)
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            tile.Opacity = 1;
        }
    }

    private void ViewModel_AppsChanged(object? sender, EventArgs e)
    {
        ViewModel.ApplyIconMaskTheme(IsDarkTheme());
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsLoading):
                SetPanelVisibility(LoadingPanel, ViewModel.IsLoading);
                break;
            case nameof(MainViewModel.IsEmpty):
                SetPanelVisibility(EmptyPanel, ViewModel.IsEmpty);
                break;
        }
    }

    private void SetPanelVisibility(FrameworkElement panel, bool visible)
    {
        if (!_panelAnimationStates.TryGetValue(panel, out var state))
        {
            state = new PanelAnimationState();
            _panelAnimationStates[panel] = state;
        }

        state.TargetVisible = visible;

        if (!visible && panel.Visibility == Visibility.Collapsed)
        {
            state.Storyboard = null;
            return;
        }

        state.Storyboard?.Stop();

        if (visible)
        {
            panel.Visibility = Visibility.Visible;
            panel.Opacity = 0;
            state.Storyboard = PlayPanelTransition(panel, entering: true, onCompleted: null);
        }
        else
        {
            state.Storyboard = PlayPanelTransition(panel, entering: false, onCompleted: () =>
            {
                if (!state.TargetVisible)
                {
                    panel.Visibility = Visibility.Collapsed;
                }
            });
        }
    }

    private static Storyboard PlayPanelTransition(FrameworkElement panel, bool entering, Action? onCompleted)
    {
        var translate = panel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        panel.RenderTransform = translate;

        var duration = TimeSpan.FromMilliseconds(entering ? 240 : 180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opacity = new DoubleAnimation
        {
            From = entering ? 0 : panel.Opacity,
            To = entering ? 1 : 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacity, panel);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var slide = new DoubleAnimation
        {
            From = entering ? 14 : translate.Y,
            To = entering ? 0 : 8,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, translate);
        Storyboard.SetTargetProperty(slide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(slide);

        if (onCompleted is not null)
        {
            storyboard.Completed += (_, _) => onCompleted();
        }

        storyboard.Begin();
        return storyboard;
    }

    private sealed class PanelAnimationState
    {
        public bool TargetVisible;
        public Storyboard? Storyboard;
    }

    private sealed class AppWindowSettings
    {
        public string BackdropMode { get; set; } = AppBackdropMode.Mica.ToString();
    }

    private enum AppBackdropMode
    {
        GaussianBlur,
        Mica,
        Acrylic,
        ThinAcrylic
    }

    private async Task RunWithErrorDialogAsync(Func<Task> action, string title)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(title, ex.Message);
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        if (Content is not FrameworkElement root || root.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = root.XamlRoot
        };

        await dialog.ShowAsync();
    }
}



