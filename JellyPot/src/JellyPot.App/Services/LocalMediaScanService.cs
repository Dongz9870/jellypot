using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JellyPot.App.Models;

namespace JellyPot.App.Services;

public sealed class LocalMediaScanService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m2ts", ".ts", ".avi", ".iso", ".wmv", ".mov"
    };
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp"];
    private static readonly string[] PosterNames = ["poster", "folder", "cover", "movie", "fanart"];

    public Task<IReadOnlyList<JellyfinMovie>> ScanAsync(IEnumerable<string> directories, string itemType, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<JellyfinMovie>>(() => Scan(directories, itemType, cancellationToken), cancellationToken);

    private static IReadOnlyList<JellyfinMovie> Scan(IEnumerable<string> directories, string itemType, CancellationToken cancellationToken)
    {
        var results = new List<JellyfinMovie>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        foreach (var directory in directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*", options); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!VideoExtensions.Contains(Path.GetExtension(file)) || !seenPaths.Add(Path.GetFullPath(file))) continue;
                results.Add(CreateItem(file, itemType));
            }
        }
        return results.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static JellyfinMovie CreateItem(string videoPath, string itemType)
    {
        var fileName = Path.GetFileNameWithoutExtension(videoPath);
        var parentName = Directory.GetParent(videoPath)?.Name;
        var displayName = fileName.Equals("movie", StringComparison.OrdinalIgnoreCase) || fileName.Equals("video", StringComparison.OrdinalIgnoreCase)
            ? parentName ?? fileName
            : Regex.Replace(fileName.Replace('.', ' ').Replace('_', ' '), "\\s+", " ").Trim();
        var yearMatch = Regex.Match(displayName, @"(?<!\d)(19|20)\d{2}(?!\d)");
        var poster = FindPoster(videoPath);
        var extension = Path.GetExtension(videoPath).TrimStart('.').ToUpperInvariant();

        return new JellyfinMovie
        {
            Id = StableId(videoPath),
            Type = itemType,
            Name = displayName,
            ProductionYear = yearMatch.Success ? int.Parse(yearMatch.Value) : null,
            Overview = "由 JellyPot 从本地或 NAS 扫描目录发现。",
            Path = videoPath,
            PosterUrl = poster,
            IsLocal = true,
            MediaSources = [new MediaSourceInfo { Id = StableId(videoPath), Name = $"本地 · {extension}", Path = videoPath, Container = extension.ToLowerInvariant() }],
            UserData = new JellyfinUserData()
        };
    }

    private static string? FindPoster(string videoPath)
    {
        var directory = Path.GetDirectoryName(videoPath);
        if (directory is null) return null;
        var basePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(videoPath));
        foreach (var extension in ImageExtensions)
        {
            var candidate = basePath + extension;
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var name in PosterNames)
        foreach (var extension in ImageExtensions)
        {
            var candidate = Path.Combine(directory, name + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string StableId(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..24];
}
