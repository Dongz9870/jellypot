using JellyPot.App.Infrastructure;

namespace JellyPot.App.Models;

public sealed class AppSettings
{
    public ServerSettings Server { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
    public List<PathMapping> PathMappings { get; set; } = [];
    public List<MediaCategory> MediaCategories { get; set; } = [];
    public List<LocalMediaSource> LocalMediaSources { get; set; } = [];
    public UiSettings Ui { get; set; } = new();
}

public sealed class ServerSettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8096";
    public string ClientName { get; set; } = "JellyPot";
    public string ClientVersion { get; set; } = "0.1.0";
    public string DeviceName { get; set; } = Environment.MachineName;
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? LastLibraryId { get; set; }
}

public sealed class PlaybackSettings
{
    public string PotPlayerExecutable { get; set; } = string.Empty;
    public string ArgumentsTemplate { get; set; } = "{path}";
    public bool AskToMarkPlayedAfterExit { get; set; } = true;
    public double MinimumPlayedPercent { get; set; } = 90;
}

public sealed class UiSettings
{
    public double PosterWidth { get; set; } = 184;
    public int PageSize { get; set; } = 60;
    public string Language { get; set; } = "zh-CN";
    public bool RememberLastLibrary { get; set; } = true;
}

public sealed class PathMapping : ObservableObject
{
    private string _name = "新映射";
    private bool _enabled = true;
    private string _serverRoot = string.Empty;
    private string _windowsRoot = string.Empty;

    public string? CategoryId { get; set; }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string ServerRoot { get => _serverRoot; set => SetProperty(ref _serverRoot, value); }
    public string WindowsRoot { get => _windowsRoot; set => SetProperty(ref _windowsRoot, value); }
}

public sealed class MediaCategory : ObservableObject
{
    private string _name = "新分类";
    private string _itemType = "Movie";
    private string? _libraryId;
    private string _serverRoot = string.Empty;
    private string _windowsRoot = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsBuiltIn { get; set; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string ItemType { get => _itemType; set { if (SetProperty(ref _itemType, value)) OnPropertyChanged(nameof(Glyph)); } }
    public string? LibraryId { get => _libraryId; set => SetProperty(ref _libraryId, value); }
    public string ServerRoot { get => _serverRoot; set => SetProperty(ref _serverRoot, value); }
    public string WindowsRoot { get => _windowsRoot; set => SetProperty(ref _windowsRoot, value); }
    public string Glyph => string.Equals(ItemType, "Series", StringComparison.OrdinalIgnoreCase) ? "▤" : "▦";
    public bool CanRemove => !IsBuiltIn;

    public static MediaCategory Movies() => new() { Id = "builtin-movies", Name = "电影", ItemType = "Movie", IsBuiltIn = true };
    public static MediaCategory Television() => new() { Id = "builtin-television", Name = "电视", ItemType = "Series", IsBuiltIn = true };
}

public sealed class LocalMediaSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CategoryId { get; set; } = "builtin-movies";
    public string DirectoryPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastScanUtc { get; set; }
}
