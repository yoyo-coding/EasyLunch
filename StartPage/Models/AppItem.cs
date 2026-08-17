using StartPage.Infrastructure;

namespace StartPage.Models;

public enum AppLaunchKind
{
    DesktopExecutable,
    Shortcut,
    UwpAumid,
    Uri,
    Unknown
}

public sealed class AppItem : ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string? _iconPath;
    private string _launchPath = string.Empty;
    private string? _arguments;
    private string? _workingDirectory;
    private string? _fileLocationPath;
    private AppLaunchKind _launchKind = AppLaunchKind.Unknown;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    // 当前主题实际使用的遮罩颜色（"#AARRGGBB"），用于图标下方的圆角矩形底色。
    private string? _iconMaskColor;

    public string? IconMaskColor
    {
        get => _iconMaskColor;
        set => SetProperty(ref _iconMaskColor, value);
    }

    // 浅色主题下的遮罩颜色（"#AARRGGBB"）。
    private string? _iconMaskColorLight;

    public string? IconMaskColorLight
    {
        get => _iconMaskColorLight;
        set => SetProperty(ref _iconMaskColorLight, value);
    }

    // 深色主题下的遮罩颜色（"#AARRGGBB"）。
    private string? _iconMaskColorDark;

    public string? IconMaskColorDark
    {
        get => _iconMaskColorDark;
        set => SetProperty(ref _iconMaskColorDark, value);
    }

    public string LaunchPath
    {
        get => _launchPath;
        set => SetProperty(ref _launchPath, value);
    }

    public string? Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set => SetProperty(ref _workingDirectory, value);
    }

    public string? FileLocationPath
    {
        get => _fileLocationPath;
        set => SetProperty(ref _fileLocationPath, value);
    }

    public AppLaunchKind LaunchKind
    {
        get => _launchKind;
        set => SetProperty(ref _launchKind, value);
    }
}
