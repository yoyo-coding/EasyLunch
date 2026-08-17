using StartPage.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace StartPage.Services;

public sealed class AppLauncherService
{
    public Task LaunchAsync(AppItem item, bool runAsAdministrator = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.LaunchKind == AppLaunchKind.UwpAumid)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = item.LaunchPath,
                UseShellExecute = false
            });
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(item.LaunchPath))
        {
            throw new FileNotFoundException("没有可用的启动路径。", item.LaunchPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = item.LaunchPath,
            Arguments = item.LaunchKind == AppLaunchKind.DesktopExecutable ? item.Arguments ?? string.Empty : string.Empty,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory) && Directory.Exists(item.WorkingDirectory))
        {
            startInfo.WorkingDirectory = item.WorkingDirectory;
        }

        if (runAsAdministrator)
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    public Task OpenFileLocationAsync(AppItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var path = item.FileLocationPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = item.LaunchPath;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = Quote(path),
                UseShellExecute = false
            });
            return Task.CompletedTask;
        }

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,{Quote(path)}",
                UseShellExecute = false
            });
            return Task.CompletedTask;
        }

        throw new FileNotFoundException("无法找到应用文件或安装目录。", path);
    }

    private static string Quote(string value) => $"\"{value}\"";
}
