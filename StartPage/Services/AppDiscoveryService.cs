using Microsoft.Win32;
using StartPage.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace StartPage.Services;

public sealed class AppDiscoveryService
{
    private const int IconOutputSize = 256;
    private const int IconAlphaThreshold = 10;
    private const int IconSafetyPadding = 0;

    // 不能完美填充圆角矩形的图标（四角透明）在蒙版内的缩放比例，四周留白避免贴边。
    private const double IconInsetScale = 0.82;

    private static readonly string IconCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyLunch",
        "IconCache");

    public async Task<IReadOnlyList<AppItem>> GetInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(IconCacheDirectory);

        var items = new List<AppItem>();
        var desktopItems = await Task.Run(() => DiscoverDesktopApps(cancellationToken), cancellationToken);
        items.AddRange(desktopItems);

        var uwpItems = await DiscoverUwpAppsAsync(cancellationToken);
        items.AddRange(uwpItems);

        var result = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.LaunchPath))
            .GroupBy(item => NormalizeKey(item))
            .Select(group => group.OrderByDescending(GetSourcePriority).ThenBy(item => item.Name).First())
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // 为每个图标分析主色，生成圆角矩形遮罩的淡化底色（后台线程执行，避免卡 UI）。
        await Task.Run(() =>
        {
            foreach (var item in result)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(item.IconPath))
                {
                    var colors = IconColorAnalyzer.GetMaskColors(item.IconPath);
                    if (colors is not null)
                    {
                        item.IconMaskColorLight = colors.Light;
                        item.IconMaskColorDark = colors.Dark;
                    }
                }

            }
        }, cancellationToken);

        return result;
    }

    private static IEnumerable<AppItem> DiscoverDesktopApps(CancellationToken cancellationToken)
    {
        var items = new List<AppItem>();
        items.AddRange(DiscoverStartMenuShortcuts(cancellationToken));
        items.AddRange(DiscoverRegistryApplications(cancellationToken));
        return items;
    }

    private static IEnumerable<AppItem> DiscoverStartMenuShortcuts(CancellationToken cancellationToken)
    {
        var startMenuRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        foreach (var root in startMenuRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var shortcutPath in shortcuts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Path.GetFileNameWithoutExtension(shortcutPath).Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var shortcut = ShellLinkReader.Read(shortcutPath);
                var targetPath = File.Exists(shortcut.TargetPath) ? shortcut.TargetPath : null;
                var shortcutIcon = CreateIconSource(shortcut.IconPath, shortcut.IconIndex);
                var targetIcon = CreateIconSource(targetPath);
                var linkIcon = CreateIconSource(shortcutPath);

                yield return new AppItem
                {
                    Id = "shortcut:" + shortcutPath.ToLowerInvariant(),
                    Name = Path.GetFileNameWithoutExtension(shortcutPath),
                    IconPath = TryExtractIcon(shortcutIcon, targetIcon, linkIcon),
                    LaunchPath = shortcutPath,
                    Arguments = shortcut.Arguments,
                    WorkingDirectory = Directory.Exists(shortcut.WorkingDirectory)
                        ? shortcut.WorkingDirectory
                        : (targetPath is not null ? Path.GetDirectoryName(targetPath) : Path.GetDirectoryName(shortcutPath)),
                    FileLocationPath = targetPath ?? shortcutPath,
                    LaunchKind = AppLaunchKind.Shortcut
                };
            }
        }
    }

    private static IEnumerable<AppItem> DiscoverRegistryApplications(CancellationToken cancellationToken)
    {
        var registryRoots = new (RegistryHive Hive, RegistryView View, string Path)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (hive, view, keyPath) in registryRoots)
        {
            using var root = OpenRegistryRoot(hive, view);
            using var uninstallKey = root?.OpenSubKey(keyPath);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var appKey = uninstallKey.OpenSubKey(subKeyName);
                if (appKey is null)
                {
                    continue;
                }

                var name = appKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(name) || IsSystemComponent(appKey))
                {
                    continue;
                }

                var displayIcon = ParseIconSource(appKey.GetValue("DisplayIcon") as string);
                var installLocation = ExpandPath(appKey.GetValue("InstallLocation") as string);
                var launchPath = displayIcon is not null && IsExecutable(displayIcon.Path)
                    ? displayIcon.Path
                    : FindLikelyExecutable(installLocation, name);

                if (!File.Exists(launchPath))
                {
                    continue;
                }

                var launchIcon = CreateIconSource(launchPath);

                yield return new AppItem
                {
                    Id = "registry:" + subKeyName.ToLowerInvariant(),
                    Name = name.Trim(),
                    IconPath = TryExtractIcon(displayIcon, launchIcon),
                    LaunchPath = launchPath,
                    WorkingDirectory = Directory.Exists(installLocation) ? installLocation : Path.GetDirectoryName(launchPath),
                    FileLocationPath = launchPath,
                    LaunchKind = AppLaunchKind.DesktopExecutable
                };
            }
        }
    }

    private static async Task<IEnumerable<AppItem>> DiscoverUwpAppsAsync(CancellationToken cancellationToken)
    {
        var items = new List<AppItem>();

        try
        {
            var packageManager = new PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty).ToList();

            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
                {
                    continue;
                }

                var entriesResult = await GetAppListEntriesAsync(package, cancellationToken);
                if (entriesResult is not System.Collections.IEnumerable entries)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry is null)
                    {
                        continue;
                    }

                    var entryType = entry.GetType();
                    var appUserModelId = entryType.GetProperty("AppUserModelId")?.GetValue(entry) as string;
                    if (string.IsNullOrWhiteSpace(appUserModelId))
                    {
                        continue;
                    }

                    var displayInfo = entryType.GetProperty("DisplayInfo")?.GetValue(entry);
                    var displayName = GetDisplayName(displayInfo)
                        ?? package.DisplayName
                        ?? package.Id.Name;

                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    items.Add(new AppItem
                    {
                        Id = appUserModelId,
                        Name = displayName,
                        IconPath = TryExtractIcon(CreateIconSource(TryResolvePackageLogoPath(package))),
                        LaunchPath = $"shell:AppsFolder\\{appUserModelId}",
                        WorkingDirectory = package.InstalledLocation?.Path,
                        FileLocationPath = package.InstalledLocation?.Path,
                        LaunchKind = AppLaunchKind.UwpAumid
                    });
                }
            }
        }
        catch
        {
            // Package enumeration is best-effort; registry and Start Menu discovery still provide desktop apps.
        }

        return items;
    }

    private static async Task<object?> GetAppListEntriesAsync(Package package, CancellationToken cancellationToken)
    {
        try
        {
            var method = package.GetType().GetMethod("GetAppListEntriesAsync", Type.EmptyTypes);
            if (method is null)
            {
                return null;
            }

            var asyncOperation = method.Invoke(package, null);
            return await AwaitWinRtResultsAsync(asyncOperation, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<object?> AwaitWinRtResultsAsync(object? asyncOperation, CancellationToken cancellationToken)
    {
        if (asyncOperation is null)
        {
            return null;
        }

        var type = asyncOperation.GetType();
        var statusProperty = type.GetProperty("Status");
        var getResultsMethod = type.GetMethod("GetResults", Type.EmptyTypes);
        if (statusProperty is null || getResultsMethod is null)
        {
            return null;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = statusProperty.GetValue(asyncOperation)?.ToString();
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return getResultsMethod.Invoke(asyncOperation, null);
            }

            if (string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private static string? GetDisplayName(object? displayInfo)
    {
        if (displayInfo is null)
        {
            return null;
        }

        var displayName = displayInfo.GetType().GetProperty("DisplayName")?.GetValue(displayInfo) as string;
        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return displayName;
        }

        return null;
    }

    private static string? TryResolvePackageLogoPath(Package package)
    {
        try
        {
            var logoValue = package.GetType().GetProperty("Logo")?.GetValue(package);
            var installedLocation = package.InstalledLocation?.Path;
            if (logoValue is null || string.IsNullOrWhiteSpace(installedLocation))
            {
                return null;
            }

            var logoPath = logoValue switch
            {
                Uri uri => uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString,
                string text => text,
                _ => logoValue.ToString()
            };

            if (string.IsNullOrWhiteSpace(logoPath))
            {
                return null;
            }

            logoPath = logoPath.Trim().Trim('"', '\\');
            logoPath = logoPath.TrimStart('/', '\\').Replace('/', '\\');

            var candidate = Path.IsPathRooted(logoPath)
                ? logoPath
                : Path.Combine(installedLocation, logoPath);

            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private static RegistryKey? OpenRegistryRoot(RegistryHive hive, RegistryView view)
    {
        try
        {
            return RegistryKey.OpenBaseKey(hive, view);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSystemComponent(RegistryKey key)
    {
        return (key.GetValue("SystemComponent") as int? ?? 0) == 1
            || string.Equals(key.GetValue("ReleaseType") as string, "Update", StringComparison.OrdinalIgnoreCase)
            || key.GetValue("ParentKeyName") is not null;
    }

    private static string? FindLikelyExecutable(string? installLocation, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        try
        {
            var normalizedName = NormalizeNameForMatch(displayName ?? string.Empty);
            var executables = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(path => !LooksLikeMaintenanceExecutable(path))
                .ToList();

            return executables
                .OrderByDescending(path => NormalizeNameForMatch(Path.GetFileNameWithoutExtension(path)).Contains(normalizedName)
                    || normalizedName.Contains(NormalizeNameForMatch(Path.GetFileNameWithoutExtension(path))))
                .ThenBy(path => Path.GetFileName(path).Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeMaintenanceExecutable(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var blockedWords = new[] { "unins", "uninstall", "setup", "install", "update", "updater", "helper", "crash", "report" };
        return blockedWords.Any(word => fileName.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeNameForMatch(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string NormalizeKey(AppItem item)
    {
        if (item.LaunchKind == AppLaunchKind.UwpAumid)
        {
            return "uwp:" + item.Id.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(item.FileLocationPath))
        {
            return "file:" + item.FileLocationPath.ToLowerInvariant();
        }

        return "name:" + item.Name.ToLowerInvariant();
    }

    private static int GetSourcePriority(AppItem item)
    {
        return item.LaunchKind switch
        {
            AppLaunchKind.Shortcut => 3,
            AppLaunchKind.UwpAumid => 2,
            AppLaunchKind.DesktopExecutable => 1,
            _ => 0
        };
    }

    private static string? NormalizeExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var iconSource = ParseIconSource(value);
        if (iconSource is not null && IsExecutable(iconSource.Path))
        {
            return iconSource.Path;
        }

        var expanded = ExpandPath(value.Trim());
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return null;
        }

        if (expanded.StartsWith('"'))
        {
            var endQuote = expanded.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return expanded[1..endQuote];
            }
        }

        var exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            return expanded[..(exeIndex + 4)].Trim('"', ' ');
        }

        return expanded.Trim('"', ' ');
    }

    private static string? ExpandPath(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value.Trim());
    }

    private static IconSource? CreateIconSource(string? path, int iconIndex = 0)
    {
        var expanded = ExpandPath(path);
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return null;
        }

        expanded = expanded.Trim('"', ' ');
        return File.Exists(expanded) ? new IconSource(expanded, iconIndex) : null;
    }

    private static IconSource? ParseIconSource(string? value)
    {
        var expanded = ExpandPath(value);
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return null;
        }

        expanded = expanded.Trim();
        var iconIndex = 0;
        string path;

        if (expanded.StartsWith('"'))
        {
            var endQuote = expanded.IndexOf('"', 1);
            if (endQuote <= 1)
            {
                return null;
            }

            path = expanded[1..endQuote];
            var remainder = expanded[(endQuote + 1)..].Trim();
            if (remainder.StartsWith(',') && int.TryParse(remainder[1..].Trim(), out var parsedIndex))
            {
                iconIndex = parsedIndex;
            }
        }
        else
        {
            path = expanded;
            var commaIndex = expanded.LastIndexOf(',');
            if (commaIndex > 0 && int.TryParse(expanded[(commaIndex + 1)..].Trim(), out var parsedIndex))
            {
                path = expanded[..commaIndex];
                iconIndex = parsedIndex;
            }
        }

        path = path.Trim('"', ' ');
        return File.Exists(path) ? new IconSource(path, iconIndex) : null;
    }

    private static bool IsExecutable(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path);
    }

    private static string? TryExtractIcon(params IconSource?[] sources)
    {
        foreach (var source in sources)
        {
            var iconPath = TryExtractIcon(source);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                return iconPath;
            }
        }

        return null;
    }

    private static string? TryExtractIcon(IconSource? source)
    {
        if (source is null || !File.Exists(source.Path))
        {
            return null;
        }

        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(source.Path).Ticks;
            var cacheName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"desktop-icon-v4|{source.Path.ToLowerInvariant()}|{source.IconIndex}|{lastWrite}"))) + ".png";
            var cachePath = Path.Combine(IconCacheDirectory, cacheName);
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            {
                return cachePath;
            }

            Bitmap? bitmap = null;
            try
            {
                bitmap = TryLoadBitmapIconFile(source.Path) ?? TryExtractNativeIconBitmap(source);
                if (bitmap is null)
                {
                    return null;
                }

                using var normalized = NormalizeIconBitmap(bitmap);
                normalized.Save(cachePath, ImageFormat.Png);
                return cachePath;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoadBitmapIconFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            if (string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var icon = new Icon(path);
                return icon.ToBitmap();
            }

            using var source = new Bitmap(path);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryExtractNativeIconBitmap(IconSource source)
    {
        var iconHandles = new IntPtr[1];
        var iconIds = new uint[1];

        try
        {
            _ = PrivateExtractIcons(source.Path, source.IconIndex, IconOutputSize, IconOutputSize, iconHandles, iconIds, 1, 0);
            if (iconHandles[0] != IntPtr.Zero)
            {
                return RenderIconHandle(iconHandles[0]);
            }
        }
        catch
        {
            // Fall back to ExtractAssociatedIcon below.
        }
        finally
        {
            if (iconHandles[0] != IntPtr.Zero)
            {
                DestroyIcon(iconHandles[0]);
            }
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(source.Path);
            return icon is null ? null : RenderIconHandle(icon.Handle);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? RenderIconHandle(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
        {
            return null;
        }

        var bitmap = new Bitmap(IconOutputSize, IconOutputSize, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            var hdc = graphics.GetHdc();
            try
            {
                if (!DrawIconEx(hdc, 0, 0, hIcon, IconOutputSize, IconOutputSize, 0, IntPtr.Zero, DI_NORMAL))
                {
                    bitmap.Dispose();
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            return null;
        }
    }

    private static Bitmap NormalizeIconBitmap(Bitmap source)
    {
        using var argbSource = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(argbSource))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var bounds = GetVisibleBounds(argbSource);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new Bitmap(IconOutputSize, IconOutputSize, PixelFormat.Format32bppArgb);
        }

        var paddedSize = IconOutputSize - (IconSafetyPadding * 2);
        var scale = Math.Min((double)paddedSize / bounds.Width, (double)paddedSize / bounds.Height);
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        var x = (IconOutputSize - width) / 2;
        var y = (IconOutputSize - height) / 2;

        var normalized = new Bitmap(IconOutputSize, IconOutputSize, PixelFormat.Format32bppArgb);
        DrawNormalized(normalized, argbSource, new Rectangle(x, y, width, height), bounds);

        // 不能完美填充圆角矩形的图标（四角透明，如圆形/异形）在蒙版内缩小并居中，
        // 避免蒙版上下左右边缘出现图形直接贴边的情况。
        if (HasTransparentCorners(normalized))
        {
            var insetWidth = Math.Max(1, (int)Math.Round(width * IconInsetScale));
            var insetHeight = Math.Max(1, (int)Math.Round(height * IconInsetScale));
            var insetX = (IconOutputSize - insetWidth) / 2;
            var insetY = (IconOutputSize - insetHeight) / 2;

            var inset = new Bitmap(IconOutputSize, IconOutputSize, PixelFormat.Format32bppArgb);
            DrawNormalized(inset, argbSource, new Rectangle(insetX, insetY, insetWidth, insetHeight), bounds);

            normalized.Dispose();
            normalized = inset;
        }

        return normalized;
    }

    private static void DrawNormalized(Bitmap target, Bitmap source, Rectangle destination, Rectangle sourceBounds)
    {
        using var graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, destination, sourceBounds, GraphicsUnit.Pixel);
    }

    // 检查归一化图标的四个角是否透明；只要有任意一角不透明像素占比过低，
    // 说明图标无法完美填充圆角矩形，需要内缩。
    private static bool HasTransparentCorners(Bitmap bitmap)
    {
        const int cornerSize = 16;
        const double opaqueRatioThreshold = 0.5;

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var buffer = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            var corners = new[]
            {
                new Rectangle(0, 0, cornerSize, cornerSize),
                new Rectangle(bitmap.Width - cornerSize, 0, cornerSize, cornerSize),
                new Rectangle(0, bitmap.Height - cornerSize, cornerSize, cornerSize),
                new Rectangle(bitmap.Width - cornerSize, bitmap.Height - cornerSize, cornerSize, cornerSize)
            };

            foreach (var corner in corners)
            {
                var opaque = 0;
                var total = 0;
                for (var y = corner.Y; y < corner.Bottom; y++)
                {
                    for (var x = corner.X; x < corner.Right; x++)
                    {
                        total++;
                        if (buffer[y * stride + x * 4 + 3] > IconAlphaThreshold)
                        {
                            opaque++;
                        }
                    }
                }

                if (total > 0 && (double)opaque / total < opaqueRatioThreshold)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Rectangle GetVisibleBounds(Bitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A <= IconAlphaThreshold)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint DI_NORMAL = 0x0003;

    private sealed record IconSource(string Path, int IconIndex = 0);

    private sealed record ShortcutInfo(string? TargetPath, string? Arguments, string? WorkingDirectory, string? IconPath, int IconIndex);

    private static class ShellLinkReader
    {
        public static ShortcutInfo Read(string shortcutPath)
        {
            try
            {
                var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
                var shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
                ((IPersistFile)shellLink).Load(shortcutPath, 0);

                var pathBuilder = new StringBuilder(260);
                shellLink.GetPath(pathBuilder, pathBuilder.Capacity, IntPtr.Zero, 0);

                var arguments = new StringBuilder(1024);
                shellLink.GetArguments(arguments, arguments.Capacity);

                var workingDirectory = new StringBuilder(260);
                shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

                var iconPath = new StringBuilder(260);
                shellLink.GetIconLocation(iconPath, iconPath.Capacity, out var iconIndex);

                return new ShortcutInfo(
                    ExpandPath(pathBuilder.ToString()),
                    arguments.ToString(),
                    ExpandPath(workingDirectory.ToString()),
                    ExpandPath(iconPath.ToString()),
                    iconIndex);
            }
            catch
            {
                return new ShortcutInfo(null, null, null, null, 0);
            }
        }


        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}



