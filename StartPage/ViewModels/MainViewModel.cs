using StartPage.Infrastructure;
using StartPage.Models;
using StartPage.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StartPage.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppDiscoveryService _appDiscoveryService;
    private readonly AppCatalogCacheService _appCatalogCacheService;
    private readonly List<AppItem> _allApps = new();
    private string _searchText = string.Empty;
    private bool _isLoading;
    private string _statusMessage = "准备加载应用";

    public MainViewModel() : this(new AppDiscoveryService(), new AppCatalogCacheService())
    {
    }

    public MainViewModel(AppDiscoveryService appDiscoveryService, AppCatalogCacheService appCatalogCacheService)
    {
        _appDiscoveryService = appDiscoveryService;
        _appCatalogCacheService = appCatalogCacheService;
    }

    public ObservableCollection<AppItem> Apps { get; } = new();

    public event EventHandler? AppsChanged;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsEmpty => !IsLoading && Apps.Count == 0;

    public int AppCount => _allApps.Count;

    /// <summary>
    /// Loads the last known catalog first, then refreshes it in the background. A forced load
    /// is used by the Refresh command and intentionally bypasses the persistent cache.
    /// </summary>
    public async Task LoadAppsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (forceRefresh)
        {
            await RefreshAppsAsync(showLoadingIndicator: true, cancellationToken);
            return;
        }

        IsLoading = true;
        StatusMessage = "正在读取本地应用缓存...";

        try
        {
            var cachedApps = await _appCatalogCacheService.TryLoadAsync(cancellationToken);
            if (cachedApps is { Count: > 0 })
            {
                ReplaceApps(cachedApps);
                IsLoading = false;
                StatusMessage = $"已从本地缓存加载 {_allApps.Count} 个应用，正在后台检查更新...";

                // Awaiting keeps cancellation and errors orderly, while the UI is already usable.
                await RefreshAppsAsync(showLoadingIndicator: false, cancellationToken);
                return;
            }

            await RefreshAppsAsync(showLoadingIndicator: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "应用加载已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"应用加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task RefreshAppsAsync(bool showLoadingIndicator, CancellationToken cancellationToken)
    {
        if (showLoadingIndicator)
        {
            IsLoading = true;
        }

        StatusMessage = showLoadingIndicator ? "正在扫描已安装应用..." : "正在后台检查应用更新...";

        try
        {
            var discoveredApps = await _appDiscoveryService.GetInstalledAppsAsync(cancellationToken);
            ReplaceApps(discoveredApps);

            try
            {
                await _appCatalogCacheService.SaveAsync(discoveredApps, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Discovery succeeded; cache persistence is an optional performance optimization.
            }

            StatusMessage = _allApps.Count == 0 ? "未找到可启动的应用" : $"已加载 {_allApps.Count} 个应用";
        }
        finally
        {
            if (showLoadingIndicator)
            {
                IsLoading = false;
            }

            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    // 根据系统深浅色主题，为所有应用设置当前使用的遮罩颜色。
    public void ApplyIconMaskTheme(bool isDark)
    {
        foreach (var app in _allApps)
        {
            app.IconMaskColor = isDark ? app.IconMaskColorDark : app.IconMaskColorLight;
        }
    }

    private void ReplaceApps(IEnumerable<AppItem> apps)
    {
        _allApps.Clear();
        _allApps.AddRange(apps);
        ApplyFilter();
        OnPropertyChanged(nameof(AppCount));
        AppsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        IEnumerable<AppItem> filteredApps = _allApps;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filteredApps = _allApps.Where(app =>
                app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || app.LaunchPath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Apps.Clear();
        foreach (var app in filteredApps.Take(500))
        {
            Apps.Add(app);
        }

        StatusMessage = Apps.Count == 0 && !string.IsNullOrWhiteSpace(query)
            ? $"没有找到“{query}”"
            : (_allApps.Count == 0 ? StatusMessage : $"显示 {Apps.Count} / {_allApps.Count} 个应用");
        OnPropertyChanged(nameof(IsEmpty));
    }
}