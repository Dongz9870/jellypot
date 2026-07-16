using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JellyPot.App.Models;

namespace JellyPot.App.Services;

public sealed class LocalMediaScanService
{
    private readonly MediaProbeService _mediaProbeService;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m2ts", ".ts", ".avi", ".iso", ".wmv", ".mov"
    };
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp"];
    private static readonly string[] PosterNames = ["poster", "folder", "cover", "movie", "fanart"];
    private static readonly Regex SeasonEpisodePattern = new(@"^(?<series>.*?)[\s._-]*S(?<season>\d{1,2})[\s._-]*E(?<episode>\d{1,3})(?:[\s._-]+(?<title>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChineseEpisodePattern = new(@"^(?<series>.*?)\s*第\s*(?<episode>\d{1,3})\s*集(?:[\s._-]+(?<title>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberedEpisodePattern = new(@"^(?<series>.+?)[\s._-]+(?<episode>\d{1,3})(?:[\s._-]+(?<title>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonDirectoryPattern = new(@"^(?:Season|S|第)\s*0*(?<season>\d{1,2})\s*(?:季)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LocalMediaScanService(MediaProbeService? mediaProbeService = null)
    {
        _mediaProbeService = mediaProbeService ?? new MediaProbeService();
    }

    public Task<IReadOnlyList<JellyfinMovie>> ScanAsync(IEnumerable<string> directories, string itemType, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<JellyfinMovie>>(() => Scan(directories, itemType, cancellationToken), cancellationToken);

    private IReadOnlyList<JellyfinMovie> Scan(IEnumerable<string> directories, string itemType, CancellationToken cancellationToken)
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
                results.Add(string.Equals(itemType, "Series", StringComparison.OrdinalIgnoreCase)
                    ? CreateEpisode(file, directory)
                    : CreateItem(file, itemType));
            }
        }
        if (string.Equals(itemType, "Series", StringComparison.OrdinalIgnoreCase)) return GroupSeries(results);
        return results.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private JellyfinMovie CreateEpisode(string videoPath, string scanRoot)
    {
        var episode = CreateItem(videoPath, "Episode");
        var identity = ParseEpisodeIdentity(videoPath, scanRoot);
        episode.SeriesName = identity.SeriesName;
        episode.SeasonName = $"第 {identity.SeasonNumber} 季";
        episode.ParentIndexNumber = identity.SeasonNumber;
        episode.IndexNumber = identity.EpisodeNumber;
        episode.Name = string.IsNullOrWhiteSpace(identity.EpisodeTitle)
            ? identity.EpisodeNumber is > 0 ? $"第 {identity.EpisodeNumber} 集" : CleanName(Path.GetFileNameWithoutExtension(videoPath))
            : identity.EpisodeTitle;
        episode.Overview = "由 JellyPot 从本地或 NAS 扫描目录发现的电视单集。";
        episode.Path = videoPath;
        episode.OriginalTitle = identity.SeriesKey;
        return episode;
    }

    private static IReadOnlyList<JellyfinMovie> GroupSeries(IEnumerable<JellyfinMovie> episodes)
    {
        var results = new List<JellyfinMovie>();
        foreach (var group in episodes.GroupBy(episode => episode.OriginalTitle ?? episode.SeriesName ?? episode.Path ?? episode.Id, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(episode => episode.ParentIndexNumber ?? 1)
                .ThenBy(episode => episode.IndexNumber ?? int.MaxValue)
                .ThenBy(episode => episode.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var nextNumbers = new Dictionary<int, int>();
            foreach (var episode in ordered)
            {
                var season = episode.ParentIndexNumber ?? 1;
                nextNumbers.TryGetValue(season, out var lastNumber);
                if (episode.IndexNumber is null or <= 0) episode.IndexNumber = lastNumber + 1;
                nextNumbers[season] = Math.Max(lastNumber, episode.IndexNumber.Value);
                if (string.IsNullOrWhiteSpace(episode.Name)) episode.Name = $"第 {episode.IndexNumber} 集";
            }

            var first = ordered[0];
            var seriesDirectory = GetSeriesDirectory(first.Path!);
            var seriesName = first.SeriesName ?? seriesDirectory.Name;
            var yearMatch = Regex.Match(seriesName, @"(?<!\d)(19|20)\d{2}(?!\d)");
            results.Add(new JellyfinMovie
            {
                Id = StableId("series:" + group.Key),
                Type = "Series",
                Name = seriesName,
                ProductionYear = yearMatch.Success ? int.Parse(yearMatch.Value) : null,
                Overview = $"本地扫描发现 {ordered.Count} 集。点击查看季和单集列表。",
                Path = seriesDirectory.FullName,
                PosterUrl = FindPosterInDirectory(seriesDirectory.FullName) ?? ordered.Select(x => x.PosterUrl).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                IsLocal = true,
                LocalEpisodes = ordered,
                UserData = new JellyfinUserData { Played = ordered.Count > 0 && ordered.All(x => x.UserData.Played) }
            });
        }
        return results.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static EpisodeIdentity ParseEpisodeIdentity(string videoPath, string scanRoot)
    {
        var fileName = Path.GetFileNameWithoutExtension(videoPath);
        var parent = Directory.GetParent(videoPath) ?? new DirectoryInfo(scanRoot);
        var seasonDirectory = SeasonDirectoryPattern.Match(parent.Name);
        var season = seasonDirectory.Success && int.TryParse(seasonDirectory.Groups["season"].Value, out var folderSeason) ? folderSeason : 1;
        var seriesDirectory = seasonDirectory.Success && parent.Parent is not null ? parent.Parent : parent;
        var fallbackSeries = CleanName(seriesDirectory.Name);

        Match match = SeasonEpisodePattern.Match(fileName);
        if (match.Success && int.TryParse(match.Groups["season"].Value, out var namedSeason)) season = namedSeason;
        if (!match.Success) match = ChineseEpisodePattern.Match(fileName);
        if (!match.Success) match = NumberedEpisodePattern.Match(fileName);

        var seriesName = match.Success ? CleanName(match.Groups["series"].Value) : fallbackSeries;
        if (string.IsNullOrWhiteSpace(seriesName)) seriesName = fallbackSeries;
        var episodeNumber = match.Success && int.TryParse(match.Groups["episode"].Value, out var parsedEpisode) ? parsedEpisode : (int?)null;
        var episodeTitle = match.Success ? CleanEpisodeTitle(match.Groups["title"].Value) : string.Empty;
        var key = Path.GetFullPath(seriesDirectory.FullName).TrimEnd('\\') + "|" + seriesName;
        return new EpisodeIdentity(seriesName, key, season, episodeNumber, episodeTitle);
    }

    private static DirectoryInfo GetSeriesDirectory(string episodePath)
    {
        var parent = Directory.GetParent(episodePath) ?? new DirectoryInfo(Path.GetDirectoryName(episodePath)!);
        return SeasonDirectoryPattern.IsMatch(parent.Name) && parent.Parent is not null ? parent.Parent : parent;
    }

    private static string CleanName(string value) => Regex.Replace(value.Replace('.', ' ').Replace('_', ' '), @"\s+", " ").Trim(' ', '-', '_', '.');

    private static string CleanEpisodeTitle(string value)
    {
        var cleaned = CleanName(value);
        cleaned = Regex.Replace(cleaned, @"\s+(?:2160p|1080p|720p|480p|WEB[- ]?DL|BluRay|HDTV)\b.*$", string.Empty, RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private JellyfinMovie CreateItem(string videoPath, string itemType)
    {
        var fileName = Path.GetFileNameWithoutExtension(videoPath);
        var parentName = Directory.GetParent(videoPath)?.Name;
        var displayName = fileName.Equals("movie", StringComparison.OrdinalIgnoreCase) || fileName.Equals("video", StringComparison.OrdinalIgnoreCase)
            ? parentName ?? fileName
            : Regex.Replace(fileName.Replace('.', ' ').Replace('_', ' '), "\\s+", " ").Trim();
        var yearMatch = Regex.Match(displayName, @"(?<!\d)(19|20)\d{2}(?!\d)");
        var poster = FindPoster(videoPath);
        var extension = Path.GetExtension(videoPath).TrimStart('.').ToUpperInvariant();
        var videoStream = _mediaProbeService.ProbeVideo(videoPath);
        var resolution = VideoResolution.GetLabel(videoStream);
        var isBluRay = VideoMediaFormat.IsBluRay(null, null, null, videoPath);
        var sourceParts = new List<string> { "本地" };
        if (isBluRay) sourceParts.Add("蓝光");
        if (resolution != "未知") sourceParts.Add(resolution);
        sourceParts.Add(extension);
        var sourceName = string.Join(" · ", sourceParts);
        var streams = videoStream is null ? new List<MediaStreamInfo>() : [videoStream];

        return new JellyfinMovie
        {
            Id = StableId(videoPath),
            Type = itemType,
            Name = displayName,
            ProductionYear = yearMatch.Success ? int.Parse(yearMatch.Value) : null,
            Overview = "由 JellyPot 从本地或 NAS 扫描目录发现。",
            Path = videoPath,
            VideoType = isBluRay ? "BluRay" : "VideoFile",
            PosterUrl = poster,
            IsLocal = true,
            MediaSources = [new MediaSourceInfo { Id = StableId(videoPath), Name = sourceName, Path = videoPath, Container = extension.ToLowerInvariant(), VideoType = isBluRay ? "BluRay" : "VideoFile", MediaStreams = streams }],
            MediaStreams = streams,
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

    private static string? FindPosterInDirectory(string directory)
    {
        foreach (var name in PosterNames)
        foreach (var extension in ImageExtensions)
        {
            var candidate = Path.Combine(directory, name + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string StableId(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..24];

    private sealed record EpisodeIdentity(string SeriesName, string SeriesKey, int SeasonNumber, int? EpisodeNumber, string EpisodeTitle);
}
