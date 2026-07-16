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
    [JsonIgnore] public string ResolutionText
    {
        get
        {
            var video = MediaStreams.FirstOrDefault(x => string.Equals(x.Type, "Video", StringComparison.OrdinalIgnoreCase));
            return video?.Height switch { >= 2100 => "4K", >= 1000 => "1080P", >= 700 => "720P", > 0 => $"{video.Height}P", _ => "HD" };
        }
    }
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
    public string? VideoRangeType { get; set; }
}

public sealed record MoviePage(IReadOnlyList<JellyfinMovie> Items, int TotalCount, int StartIndex);
public sealed record PathResolution(PathMapping Mapping, string SourcePath, string WindowsPath);
