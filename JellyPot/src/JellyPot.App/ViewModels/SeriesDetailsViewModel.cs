using System.Collections.ObjectModel;
using JellyPot.App.Infrastructure;
using JellyPot.App.Models;
using JellyPot.App.Services;

namespace JellyPot.App.ViewModels;

public sealed class SeriesDetailsViewModel : ObservableObject
{
    private readonly JellyfinApiClient _apiClient;
    private readonly string _userId;
    private readonly bool _demoMode;
    private readonly AppSettings _settings;
    private readonly PathMappingService _pathMappingService;
    private readonly PotPlayerService _potPlayerService;
    private readonly DialogService _dialogService;
    private SeasonGroup? _selectedSeason;
    private JellyfinMovie? _selectedEpisode;
    private MovieDetailsViewModel? _selectedEpisodeDetails;
    private bool _isBusy;
    private string _errorMessage = string.Empty;

    public SeriesDetailsViewModel(JellyfinMovie series, JellyfinApiClient apiClient, string userId, bool demoMode,
        AppSettings settings, PathMappingService pathMappingService, PotPlayerService potPlayerService, DialogService dialogService)
    {
        Series = series;
        _apiClient = apiClient;
        _userId = userId;
        _demoMode = demoMode;
        _settings = settings;
        _pathMappingService = pathMappingService;
        _potPlayerService = potPlayerService;
        _dialogService = dialogService;
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? BackRequested;
    public event EventHandler? SettingsRequested;
    public JellyfinMovie Series { get; }
    public ObservableCollection<SeasonGroup> Seasons { get; } = [];
    public ObservableCollection<JellyfinMovie> VisibleEpisodes { get; } = [];
    public RelayCommand BackCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string SummaryText => $"{Seasons.Count} 季 · {Seasons.Sum(season => season.Episodes.Count)} 集";

    public SeasonGroup? SelectedSeason
    {
        get => _selectedSeason;
        set
        {
            if (!SetProperty(ref _selectedSeason, value)) return;
            VisibleEpisodes.Clear();
            if (value is not null)
                foreach (var episode in value.Episodes) VisibleEpisodes.Add(episode);
            SelectedEpisode = VisibleEpisodes.FirstOrDefault();
        }
    }

    public JellyfinMovie? SelectedEpisode
    {
        get => _selectedEpisode;
        set
        {
            if (!SetProperty(ref _selectedEpisode, value)) return;
            SelectedEpisodeDetails = value is null ? null
                : new MovieDetailsViewModel(value, _settings, _pathMappingService, _potPlayerService, _dialogService);
        }
    }

    public MovieDetailsViewModel? SelectedEpisodeDetails
    {
        get => _selectedEpisodeDetails;
        private set => SetProperty(ref _selectedEpisodeDetails, value);
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            IReadOnlyList<JellyfinMovie> episodes = Series.IsLocal
                ? Series.LocalEpisodes
                : _demoMode ? CreateDemoEpisodes() : await _apiClient.GetEpisodesAsync(_userId, Series.Id);
            Seasons.Clear();
            foreach (var group in episodes.GroupBy(episode => episode.ParentIndexNumber ?? 1).OrderBy(group => group.Key))
            {
                var ordered = group.OrderBy(episode => episode.IndexNumber ?? int.MaxValue)
                    .ThenBy(episode => episode.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Seasons.Add(new SeasonGroup(group.Key, group.Key <= 0 ? "特别篇" : $"第 {group.Key} 季", ordered));
            }
            OnPropertyChanged(nameof(SummaryText));
            SelectedSeason = Seasons.FirstOrDefault();
            if (Seasons.Count == 0) ErrorMessage = "没有找到该剧集的单集信息。请在 Jellyfin 中重新扫描电视媒体库。";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载单集失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private IReadOnlyList<JellyfinMovie> CreateDemoEpisodes() => Enumerable.Range(1, 10).Select(index => new JellyfinMovie
    {
        Id = $"{Series.Id}-episode-{index}", Type = "Episode", SeriesName = Series.Name, SeasonName = "第 1 季",
        ParentIndexNumber = 1, IndexNumber = index, Name = index == 1 ? "序章" : $"第 {index} 集",
        Overview = "电视单集设计预览。连接 Jellyfin 后将显示真实单集名称、播放状态和文件路径。",
        Path = $@"D:\TV\{Series.Name}\Season 01\S01E{index:00}.mkv", RunTimeTicks = TimeSpan.FromMinutes(48).Ticks,
        MediaStreams = [new MediaStreamInfo { Type = "Video", Width = 1920, Height = 1080, Codec = "hevc", VideoRangeType = "SDR" }],
        MediaSources = [new MediaSourceInfo { Id = $"episode-{index}", Name = $"S01E{index:00} · 1080P", Path = $@"D:\TV\{Series.Name}\Season 01\S01E{index:00}.mkv" }],
        UserData = new JellyfinUserData { Played = index <= 2 }
    }).ToList();
}

public sealed record SeasonGroup(int Number, string Name, IReadOnlyList<JellyfinMovie> Episodes);
