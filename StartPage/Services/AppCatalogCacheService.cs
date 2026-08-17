using StartPage.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StartPage.Services;

/// <summary>
/// Persists the normalized application catalog so repeat launches can render immediately
/// instead of waiting for Start Menu, registry, and package discovery to finish.
/// </summary>
public sealed class AppCatalogCacheService
{
    private const int CurrentSchemaVersion = 1;

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StartPage",
        "Cache");

    private static readonly string CachePath = Path.Combine(CacheDirectory, "apps.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<IReadOnlyList<AppItem>?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<AppItem>?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(CachePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(CachePath, Encoding.UTF8);
                var document = JsonSerializer.Deserialize<CacheDocument>(json, SerializerOptions);
                if (document is null || document.SchemaVersion != CurrentSchemaVersion || document.Apps is null)
                {
                    return null;
                }

                // Do not touch each target path here: that would turn cache loading back into a full scan.
                var apps = document.Apps
                    .Where(IsUsable)
                    .Select(entry => entry.ToAppItem())
                    .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                return apps.Count == 0 ? null : apps;
            }
            catch (Exception)
            {
                // A cache is optional. Corrupt or inaccessible data simply triggers a normal scan.
                return null;
            }
        }, cancellationToken);
    }

    public Task SaveAsync(IEnumerable<AppItem> apps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apps);

        // Snapshot UI-bound objects before moving serialization and I/O to a worker thread.
        var entries = apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.LaunchPath))
            .Select(CachedAppItem.FromAppItem)
            .ToList();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(CacheDirectory);

            var document = new CacheDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Apps = entries
            };

            var temporaryPath = CachePath + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(document, SerializerOptions);
                File.WriteAllText(temporaryPath, json, Encoding.UTF8);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, CachePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }, cancellationToken);
    }

    private static bool IsUsable(CachedAppItem entry) =>
        !string.IsNullOrWhiteSpace(entry.Id) &&
        !string.IsNullOrWhiteSpace(entry.Name) &&
        !string.IsNullOrWhiteSpace(entry.LaunchPath);

    private sealed class CacheDocument
    {
        public int SchemaVersion { get; set; }

        public DateTimeOffset UpdatedUtc { get; set; }

        public List<CachedAppItem>? Apps { get; set; }
    }

    private sealed class CachedAppItem
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? IconPath { get; set; }

        public string? IconMaskColorLight { get; set; }

        public string? IconMaskColorDark { get; set; }

        public string LaunchPath { get; set; } = string.Empty;

        public string? Arguments { get; set; }

        public string? WorkingDirectory { get; set; }

        public string? FileLocationPath { get; set; }

        public AppLaunchKind LaunchKind { get; set; }

        public static CachedAppItem FromAppItem(AppItem app) => new()
        {
            Id = app.Id,
            Name = app.Name,
            IconPath = app.IconPath,
            IconMaskColorLight = app.IconMaskColorLight,
            IconMaskColorDark = app.IconMaskColorDark,
            LaunchPath = app.LaunchPath,
            Arguments = app.Arguments,
            WorkingDirectory = app.WorkingDirectory,
            FileLocationPath = app.FileLocationPath,
            LaunchKind = app.LaunchKind
        };

        public AppItem ToAppItem() => new()
        {
            Id = Id,
            Name = Name,
            IconPath = IconPath,
            IconMaskColorLight = IconMaskColorLight,
            IconMaskColorDark = IconMaskColorDark,
            LaunchPath = LaunchPath,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            FileLocationPath = FileLocationPath,
            LaunchKind = LaunchKind
        };
    }
}