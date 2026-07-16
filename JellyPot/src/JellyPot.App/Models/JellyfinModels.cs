using System.Text.Json.Serialization;

namespace JellyPot.App.Models;

public sealed class JellyfinServerInfo
{
    public string ServerName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public sealed class AuthenticationResult
{
    public string AccessToken { get; set; } = string.Empty;
    public JellyfinUser User { get; set; } = new();
    public string ServerId { get; set; } = string.Empty;
}

public sealed class JellyfinUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class QueryResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalRecordCount { get; set; }
    public int StartIndex { get; set; }
}

public sealed class JellyfinLibrary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CollectionType { get; set; }
    [JsonIgnore] public string TypeText => CollectionType?.ToLowerInvariant() switch { "tvshows" => "电视", "movies" => "电影", _ => "媒体" };
    [JsonIgnore] public string DisplayName => $"{Name} · {TypeText}";
}

public sealed class JellyfinMovie
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "Movie";
    public string Name { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public int? ProductionYear { get; set; }
    public string? Overview { get; set; }
    public double? CommunityRating { get; set; }
    public long? RunTimeTicks { get; set; }
    public string? Path { get; set; }
    public string? VideoType { get; set; }
    public string? IsoType { get; set; }
    public List<string> Genres { get; set; } = [];
    public Dictionary<string, string> ImageTags { get; set; } = [];
    public List<MediaSourceInfo> MediaSources { get; set; } = [];
    public JellyfinUserData UserData { get; set; } = new();
    public List<MediaStreamInfo> MediaStreams { get; set; } = [];

    [JsonIgnore] public string? PosterUrl { get; set; }
    [JsonIgnore] public bool IsLocal { get; set; }
    [JsonIgnore] public string YearText => ProductionYear?.ToString() ?? "年份未知";
    [JsonIgnore] public string RatingText => CommunityRating is > 0 ? CommunityRating.Value.ToString("0.0") : "—";
    [JsonIgnore] public string DurationText => RunTimeTicks is > 0 ? $"{TimeSpan.FromTicks(RunTimeTicks.Value).TotalMinutes:0} 分钟" : "时长未知";
    [JsonIgnore] public string Initial => string.IsNullOrWhiteSpace(Name) ? "J" : Name.Trim()[0].ToString().ToUpperInvariant();
    [JsonIgnore] public string ProgressText => UserData.Played ? "已观看" : UserData.PlaybackPositionTicks > 0 ? $"已看 {ProgressPercent:0}%" : "未观看";
    [JsonIgnore] public double ProgressPercent => RunTimeTicks is > 0 ? Math.Clamp(UserData.PlaybackPositionTicks * 100d / RunTimeTicks.Value, 0, 100) : 0;
    [JsonIgnore] public MediaStreamInfo? BestVideoStream => MediaSources
        .SelectMany(source => source.MediaStreams)
        .Concat(MediaStreams)
        .Where(VideoResolution.IsVideo)
        .OrderByDescending(VideoResolution.PixelCount)
        .FirstOrDefault();
    [JsonIgnore] public string ResolutionText => VideoResolution.GetLabel(BestVideoStream);
    [JsonIgnore] public string ResolutionDetailText => VideoResolution.GetDescription(BestVideoStream);
    [JsonIgnore] public string DynamicRangeText => VideoDynamicRange.GetLabel(BestVideoStream);
    [JsonIgnore] public string DynamicRangeDetailText => VideoDynamicRange.GetDescription(BestVideoStream);
    [JsonIgnore] public bool HasDynamicRange => VideoDynamicRange.IsKnown(BestVideoStream);
    [JsonIgnore] public bool HasBluRay => VideoMediaFormat.IsBluRay(this);
    [JsonIgnore] public string BluRayDetailText => VideoMediaFormat.GetBluRayDescription(this);
}

public sealed class JellyfinUserData
{
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public long PlaybackPositionTicks { get; set; }
}

public sealed class MediaSourceInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Container { get; set; }
    public string? VideoType { get; set; }
    public string? IsoType { get; set; }
    public long? Size { get; set; }
    public List<MediaStreamInfo> MediaStreams { get; set; } = [];
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (Container?.ToUpperInvariant() ?? "默认版本") : Name;
}

public sealed class MediaStreamInfo
{
    public string? Type { get; set; }
    public string? Codec { get; set; }
    public string? DisplayTitle { get; set; }
    public string? Language { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Channels { get; set; }
    public string? VideoRange { get; set; }
    public string? VideoRangeType { get; set; }
    public string? ColorTransfer { get; set; }
}

public static class VideoResolution
{
    public static bool IsVideo(MediaStreamInfo stream) =>
        string.Equals(stream.Type, "Video", StringComparison.OrdinalIgnoreCase);

    public static long PixelCount(MediaStreamInfo stream) =>
        Math.Max(0, stream.Width ?? 0) * (long)Math.Max(0, stream.Height ?? 0);

    public static string GetLabel(MediaStreamInfo? stream) =>
        stream is null ? "未知" : GetLabel(stream.Width, stream.Height);

    public static string GetLabel(int? width, int? height)
    {
        var validWidth = width is > 0 ? width.Value : 0;
        var validHeight = height is > 0 ? height.Value : 0;
        if (validWidth == 0 && validHeight == 0) return "未知";

        if (validWidth == 0) return LabelFromHeight(validHeight);
        if (validHeight == 0) return LabelFromWidth(validWidth);

        var longSide = Math.Max(validWidth, validHeight);
        var shortSide = Math.Min(validWidth, validHeight);

        // Width is considered as well as height so cropped scope formats such as
        // 3840x1608 and 1920x800 keep their source resolution class.
        if (longSide >= 7000 || shortSide >= 4000) return "8K";
        if (longSide >= 3800 || shortSide >= 2000) return "4K";
        if (longSide >= 2500 && shortSide >= 1200) return "1440P";
        if (longSide >= 1900 || shortSide >= 1000) return "1080P";
        if (longSide >= 1200 || shortSide >= 700) return "720P";
        return $"{shortSide}P";
    }

    public static string GetDimensions(MediaStreamInfo? stream) =>
        stream?.Width is > 0 && stream.Height is > 0 ? $"{stream.Width}×{stream.Height}" : "尺寸未知";

    public static string GetDescription(MediaStreamInfo? stream)
    {
        var label = GetLabel(stream);
        var dimensions = GetDimensions(stream);
        return dimensions == "尺寸未知" ? label : $"{label} · {dimensions}";
    }

    private static string LabelFromHeight(int height) => height switch
    {
        >= 4000 => "8K",
        >= 2000 => "4K",
        >= 1300 => "1440P",
        >= 1000 => "1080P",
        >= 700 => "720P",
        > 0 => $"{height}P",
        _ => "未知"
    };

    private static string LabelFromWidth(int width) => width switch
    {
        >= 7000 => "8K",
        >= 3800 => "4K",
        >= 2500 => "1440P",
        >= 1900 => "1080P",
        >= 1200 => "720P",
        _ => "未知"
    };
}

public static class VideoDynamicRange
{
    public static bool IsKnown(MediaStreamInfo? stream) => GetLabel(stream) != "未知";

    public static string GetLabel(MediaStreamInfo? stream)
    {
        if (stream is null) return "未知";
        foreach (var value in new[] { stream.VideoRangeType, stream.VideoRange, stream.ColorTransfer, stream.DisplayTitle })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var normalized = value.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            if (normalized.Contains("DOVI") || normalized.Contains("DOLBYVISION") || normalized.Contains("HDR")
                || normalized.Contains("HLG") || normalized.Contains("SMPTE2084") || normalized == "PQ") return "HDR";
            if (normalized.Contains("SDR") || normalized.Contains("BT709") || normalized.Contains("SMPTE170M")
                || normalized.Contains("BT470") || normalized.Contains("GAMMA") || normalized.Contains("IEC61966")) return "SDR";
        }
        return "未知";
    }

    public static string GetDescription(MediaStreamInfo? stream)
    {
        var label = GetLabel(stream);
        if (label == "未知" || stream is null) return label;
        var type = stream.VideoRangeType?.Trim();
        return label == "HDR" && !string.IsNullOrWhiteSpace(type) && !type.Equals("HDR", StringComparison.OrdinalIgnoreCase)
            ? $"HDR · {type.ToUpperInvariant()}"
            : label;
    }
}

public static class VideoMediaFormat
{
    public static bool IsBluRay(JellyfinMovie item) =>
        IsBluRay(item.VideoType, item.IsoType, null, item.Path) || item.MediaSources.Any(IsBluRay);

    public static bool IsBluRay(MediaSourceInfo source) =>
        IsBluRay(source.VideoType, source.IsoType, source.Container, source.Path);

    public static bool IsBluRay(string? videoType, string? isoType, string? container, string? path)
    {
        if (IsBluRayValue(videoType) || IsBluRayValue(isoType) || IsBluRayValue(container)) return true;
        return HasBdmvPath(path);
    }

    public static string GetBluRayDescription(JellyfinMovie item)
    {
        var source = item.MediaSources.FirstOrDefault(IsBluRay);
        return source is null
            ? GetBluRayDescription(item.VideoType, item.IsoType, null, item.Path)
            : GetBluRayDescription(source);
    }

    public static string GetBluRayDescription(MediaSourceInfo source) =>
        GetBluRayDescription(source.VideoType, source.IsoType, source.Container, source.Path);

    private static string GetBluRayDescription(string? videoType, string? isoType, string? container, string? path)
    {
        if (!IsBluRay(videoType, isoType, container, path)) return "";
        if (IsBluRayValue(isoType) || string.Equals(container, "iso", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase)) return "蓝光原盘 · ISO";
        if (HasBdmvPath(path)) return "蓝光原盘 · BDMV";
        return "蓝光原盘";
    }

    private static bool HasBdmvPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.EndsWith("/BDMV", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/BDMV/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBluRayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        return normalized.Equals("BluRay", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("BDMV", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MoviePage(IReadOnlyList<JellyfinMovie> Items, int TotalCount, int StartIndex);
public sealed record PathResolution(PathMapping Mapping, string SourcePath, string WindowsPath);
