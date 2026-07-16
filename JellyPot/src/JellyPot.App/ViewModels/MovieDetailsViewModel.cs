using JellyPot.App.Infrastructure;
using JellyPot.App.Models;
using JellyPot.App.Services;

namespace JellyPot.App.ViewModels;

public sealed class MovieDetailsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly PathMappingService _pathMappingService;
    private readonly PotPlayerService _potPlayerService;
    private readonly DialogService _dialogService;
    private MediaSourceInfo? _selectedSource;
    private string _windowsPath = string.Empty;
    private string _mappingName = "未匹配";
    private string _pathStatus = string.Empty;
    private bool _canPlay;

    public MovieDetailsViewModel(JellyfinMovie movie, AppSettings settings, PathMappingService pathMappingService, PotPlayerService potPlayerService, DialogService dialogService)
    {
        Movie = movie;
        _settings = settings;
        _pathMappingService = pathMappingService;
        _potPlayerService = potPlayerService;
        _dialogService = dialogService;
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        PlayCommand = new RelayCommand(Play, () => CanPlay);
        SelectedSource = movie.MediaSources.FirstOrDefault() ?? new MediaSourceInfo { Name = "默认版本", Path = movie.Path };
    }

    public event EventHandler? BackRequested;
    public event EventHandler? SettingsRequested;
    public JellyfinMovie Movie { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand PlayCommand { get; }
    public IReadOnlyList<MediaSourceInfo> Sources => Movie.MediaSources.Count > 0 ? Movie.MediaSources : [new MediaSourceInfo { Name = "默认版本", Path = Movie.Path }];

    public MediaSourceInfo? SelectedSource
    {
        get => _selectedSource;
        set { if (SetProperty(ref _selectedSource, value)) ResolvePreview(); }
    }
    public string ServerPath => SelectedSource?.Path ?? Movie.Path ?? "Jellyfin 未返回路径";
    public string WindowsPath { get => _windowsPath; private set => SetProperty(ref _windowsPath, value); }
    public string MappingName { get => _mappingName; private set => SetProperty(ref _mappingName, value); }
    public string PathStatus { get => _pathStatus; private set => SetProperty(ref _pathStatus, value); }
    public bool CanPlay { get => _canPlay; private set { if (SetProperty(ref _canPlay, value)) PlayCommand.NotifyCanExecuteChanged(); } }
    public string GenresText => Movie.Genres.Count > 0 ? string.Join("  ·  ", Movie.Genres) : "类型未知";
    public string SelectedResolutionText => VideoResolution.GetLabel(SelectedVideoStream);
    public string SelectedResolutionDetailText => VideoResolution.GetDescription(SelectedVideoStream);
    public string VideoInfo
    {
        get
        {
            var video = SelectedVideoStream;
            var parts = new List<string> { VideoResolution.GetDescription(video) };
            if (!string.IsNullOrWhiteSpace(video?.Codec)) parts.Add(video.Codec.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(video?.VideoRangeType)) parts.Add(video.VideoRangeType);
            return string.Join("  ·  ", parts);
        }
    }

    private MediaStreamInfo? SelectedVideoStream
    {
        get
        {
            var selected = SelectedSource?.MediaStreams.Where(VideoResolution.IsVideo)
                .OrderByDescending(VideoResolution.PixelCount).FirstOrDefault();
            return selected is not null && VideoResolution.PixelCount(selected) > 0
                ? selected
                : Movie.BestVideoStream ?? selected;
        }
    }

    private void ResolvePreview()
    {
        OnPropertyChanged(nameof(ServerPath));
        OnPropertyChanged(nameof(VideoInfo));
        OnPropertyChanged(nameof(SelectedResolutionText));
        OnPropertyChanged(nameof(SelectedResolutionDetailText));
        try
        {
            var result = _pathMappingService.Resolve(ServerPath, _settings.PathMappings);
            WindowsPath = result.WindowsPath;
            MappingName = result.Mapping.Name;
            PathStatus = File.Exists(result.WindowsPath) || Directory.Exists(result.WindowsPath) ? "路径可访问" : "映射成功，播放时将检查文件";
            CanPlay = true;
        }
        catch (Exception ex)
        {
            WindowsPath = "尚未生成 Windows 路径";
            MappingName = "未匹配";
            PathStatus = ex.Message;
            CanPlay = false;
        }
    }

    private void Play()
    {
        try
        {
            _potPlayerService.Launch(_settings.Playback.PotPlayerExecutable, WindowsPath);
            PathStatus = "已交给 PotPlayer 播放";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法播放", $"{ex.Message}\n\nJellyfin 原始路径：\n{ServerPath}\n\n命中映射：{MappingName}\nWindows 路径：\n{WindowsPath}\n\nPotPlayer：\n{_settings.Playback.PotPlayerExecutable}");
        }
    }
}
