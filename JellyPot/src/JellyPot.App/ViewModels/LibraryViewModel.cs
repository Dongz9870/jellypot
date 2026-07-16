using System.Collections.ObjectModel;
using JellyPot.App.Infrastructure;
using JellyPot.App.Models;
using JellyPot.App.Services;

namespace JellyPot.App.ViewModels;

public sealed class LibraryViewModel : ObservableObject
{
    private readonly JellyfinApiClient _apiClient;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly LocalMediaScanService _localMediaScanService;
    private readonly string _userId;
    private readonly bool _demoMode;
    private readonly List<JellyfinLibrary> _allLibraries = [];
    private readonly Dictionary<string, List<JellyfinMovie>> _localItemsByCategory = new(StringComparer.OrdinalIgnoreCase);
    private MediaCategory _activeCategory;
    private JellyfinLibrary? _selectedLibrary;
    private string _searchText = string.Empty;
    private string _selectedFilter = "全部电影";
    private string _selectedSort = "片名 A–Z";
    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private int _startIndex;
    private int _totalCount;
    private bool _suppressLibraryLoad;

    public LibraryViewModel(JellyfinApiClient apiClient, AppSettings settings, SettingsService settingsService, LocalMediaScanService localMediaScanService, string userId, MediaCategory initialCategory, bool demoMode = false)
    {
        _apiClient = apiClient;
        _settings = settings;
        _settingsService = settingsService;
        _localMediaScanService = localMediaScanService;
        _userId = userId;
        _activeCategory = initialCategory;
        _demoMode = demoMode;
        OpenMovieCommand = new RelayCommand<JellyfinMovie>(movie => { if (movie is not null) MovieOpened?.Invoke(this, movie); });
        RefreshCommand = new AsyncRelayCommand(() => LoadMoviesAsync(_startIndex), () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(() => LoadMoviesAsync(0), () => !IsBusy);
        PreviousPageCommand = new AsyncRelayCommand(() => LoadMoviesAsync(Math.Max(0, _startIndex - PageSize)), () => !IsBusy && _startIndex > 0);
        NextPageCommand = new AsyncRelayCommand(() => LoadMoviesAsync(_startIndex + PageSize), () => !IsBusy && _startIndex + PageSize < _totalCount);
    }

    public event EventHandler<JellyfinMovie>? MovieOpened;
    public ObservableCollection<JellyfinLibrary> Libraries { get; } = [];
    public ObservableCollection<JellyfinMovie> Movies { get; } = [];
    public ObservableCollection<JellyfinMovie> VisibleMovies { get; } = [];
    public IReadOnlyList<JellyfinLibrary> AvailableLibraries => _allLibraries;
    public IReadOnlyList<string> Filters { get; } = ["全部电影", "未观看", "已观看", "收藏"];
    public IReadOnlyList<string> SortOptions { get; } = ["片名 A–Z", "年份（最新）", "评分（最高）"];
    public RelayCommand<JellyfinMovie> OpenMovieCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public int PageSize => Math.Clamp(_settings.Ui.PageSize, 20, 100);

    public JellyfinLibrary? SelectedLibrary
    {
        get => _selectedLibrary;
        set
        {
            if (!SetProperty(ref _selectedLibrary, value) || value is null || _suppressLibraryLoad) return;
            _settings.Server.LastLibraryId = value.Id;
            _ = _settingsService.SaveAsync(_settings);
            _ = LoadMoviesAsync(0);
        }
    }

    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyView(); } }
    public string SelectedFilter { get => _selectedFilter; set { if (SetProperty(ref _selectedFilter, value)) ApplyView(); } }
    public string SelectedSort { get => _selectedSort; set { if (SetProperty(ref _selectedSort, value)) ApplyView(); } }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RefreshCommand.NotifyCanExecuteChanged();
            SearchCommand.NotifyCanExecuteChanged();
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsEmpty => !IsBusy && VisibleMovies.Count == 0;
    public string SectionTitle => _activeCategory.Name;
    public string ResultText => _demoMode ? $"设计预览 · {VisibleMovies.Count} {ItemNoun}" : $"{_totalCount:N0} {ItemNoun}";
    public string PageText => _totalCount == 0 ? "0 / 0" : $"{_startIndex / PageSize + 1} / {Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize))}";
    private string ItemNoun => string.Equals(_activeCategory.ItemType, "Series", StringComparison.OrdinalIgnoreCase) ? "部剧集" : "部电影";

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            _allLibraries.Clear();
            if (_demoMode)
            {
                _allLibraries.Add(new JellyfinLibrary { Id = "demo-movies", Name = "我的电影", CollectionType = "movies" });
                _allLibraries.Add(new JellyfinLibrary { Id = "demo-television", Name = "我的电视", CollectionType = "tvshows" });
            }
            else
            {
                _allLibraries.AddRange(await _apiClient.GetLibrariesAsync(_userId));
            }
            await ShowCategoryAsync(_activeCategory);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task ShowCategoryAsync(MediaCategory category)
    {
        _activeCategory = category;
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(ResultText));
        ErrorMessage = string.Empty;

        var expectedCollectionType = string.Equals(category.ItemType, "Series", StringComparison.OrdinalIgnoreCase) ? "tvshows" : "movies";
        IEnumerable<JellyfinLibrary> matches = _allLibraries.Where(x => string.Equals(x.CollectionType, expectedCollectionType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(category.LibraryId))
            matches = matches.Where(x => x.Id == category.LibraryId);

        Libraries.Clear();
        foreach (var library in matches) Libraries.Add(library);
        var preferred = Libraries.FirstOrDefault(x => x.Id == category.LibraryId)
            ?? Libraries.FirstOrDefault(x => x.Id == _settings.Server.LastLibraryId)
            ?? Libraries.FirstOrDefault();
        _suppressLibraryLoad = true;
        SelectedLibrary = preferred;
        _suppressLibraryLoad = false;

        if (preferred is null)
        {
            Movies.Clear();
            VisibleMovies.Clear();
            _totalCount = 0;
            ErrorMessage = string.Equals(category.ItemType, "Series", StringComparison.OrdinalIgnoreCase)
                ? "Jellyfin 中没有找到电视媒体库，可使用左侧加号绑定一个片源库。"
                : "Jellyfin 中没有找到电影媒体库。";
            NotifyPageState();
            return;
        }
        await LoadMoviesAsync(0);
    }

    private async Task LoadMoviesAsync(int startIndex)
    {
        if (SelectedLibrary is null) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            if (_demoMode)
            {
                LoadDemoItems();
                return;
            }
            var page = await _apiClient.GetItemsAsync(_userId, SelectedLibrary.Id, _activeCategory.ItemType, startIndex, PageSize, SearchText);
            _startIndex = page.StartIndex;
            _totalCount = page.TotalCount;
            Movies.Clear();
            foreach (var movie in page.Items) Movies.Add(movie);
            MergeLocalItems();
            ApplyView();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            IsBusy = false;
            NotifyPageState();
        }
    }

    public async Task<int> ScanLocalSourcesAsync(MediaCategory category, IEnumerable<string> directories)
    {
        if (_activeCategory.Id != category.Id) await ShowCategoryAsync(category);
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var items = await _localMediaScanService.ScanAsync(directories, category.ItemType);
            _localItemsByCategory[category.Id] = items.ToList();
            MergeLocalItems();
            ApplyView();
            return items.Count;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"扫描失败：{ex.Message}";
            return 0;
        }
        finally { IsBusy = false; }
    }

    public async Task ClearLocalItemsAsync(MediaCategory category)
    {
        _localItemsByCategory.Remove(category.Id);
        if (_activeCategory.Id == category.Id) await LoadMoviesAsync(0);
    }

    public string? SuggestScanDirectory(MediaCategory category)
    {
        var directories = Movies
            .Where(x => !x.IsLocal && LooksLikeWindowsPath(x.Path))
            .Select(x => Path.GetDirectoryName(x.Path!))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
        if (directories.Count == 0) return null;
        var common = directories[0].TrimEnd('\\', '/');
        foreach (var directory in directories.Skip(1))
        {
            while (!IsDirectoryPrefix(directory, common))
            {
                common = Path.GetDirectoryName(common)?.TrimEnd('\\', '/') ?? string.Empty;
                if (string.IsNullOrWhiteSpace(common)) return null;
            }
        }
        return Directory.Exists(common) ? common : null;
    }

    private void MergeLocalItems()
    {
        if (!_localItemsByCategory.TryGetValue(_activeCategory.Id, out var localItems)) return;
        var removed = 0;
        for (var index = Movies.Count - 1; index >= 0; index--)
        {
            if (!Movies[index].IsLocal) continue;
            Movies.RemoveAt(index);
            removed++;
        }
        _totalCount = Math.Max(0, _totalCount - removed);
        var paths = new HashSet<string>(Movies.Select(x => NormalizePath(x.Path)), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var item in localItems)
        {
            if (!paths.Add(NormalizePath(item.Path))) continue;
            Movies.Add(item);
            added++;
        }
        _totalCount += added;
    }

    private static string NormalizePath(string? path) => path?.Trim().Replace('/', '\\') ?? string.Empty;
    private static bool LooksLikeWindowsPath(string? path) => !string.IsNullOrWhiteSpace(path)
        && (path.StartsWith("\\\\", StringComparison.Ordinal) || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':'));
    private static bool IsDirectoryPrefix(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private void ApplyView()
    {
        IEnumerable<JellyfinMovie> result = Movies;
        if (!string.IsNullOrWhiteSpace(SearchText))
            result = result.Where(x => x.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || (x.OriginalTitle?.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase) ?? false));
        result = SelectedFilter switch
        {
            "未观看" => result.Where(x => !x.UserData.Played),
            "已观看" => result.Where(x => x.UserData.Played),
            "收藏" => result.Where(x => x.UserData.IsFavorite),
            _ => result
        };
        result = SelectedSort switch
        {
            "年份（最新）" => result.OrderByDescending(x => x.ProductionYear ?? 0).ThenBy(x => x.Name),
            "评分（最高）" => result.OrderByDescending(x => x.CommunityRating ?? 0).ThenBy(x => x.Name),
            _ => result.OrderBy(x => x.Name)
        };
        VisibleMovies.Clear();
        foreach (var movie in result) VisibleMovies.Add(movie);
        NotifyPageState();
    }

    private void NotifyPageState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(PageText));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private void LoadDemoItems()
    {
        Movies.Clear();
        if (string.Equals(_activeCategory.ItemType, "Series", StringComparison.OrdinalIgnoreCase))
        {
            string[] series = ["三体", "幕府将军", "最后生还者", "漫长的季节", "黑镜", "风骚律师", "王冠", "繁花"];
            foreach (var name in series)
            {
                Movies.Add(new JellyfinMovie
                {
                    Id = Guid.NewGuid().ToString("N"), Type = "Series", Name = name, OriginalTitle = name,
                    ProductionYear = 2024, CommunityRating = 8.4,
                    Overview = "电视分类设计预览。连接 Jellyfin 电视媒体库后，这里会显示真实剧集资料与海报。",
                    Genres = ["剧情"], UserData = new JellyfinUserData(),
                    MediaStreams = []
                });
            }
            _totalCount = Movies.Count;
            _startIndex = 0;
            ApplyView();
            return;
        }
        var samples = new (string Name, string Original, int Year, double Rating, bool Played, bool Favorite, string Genre, string Resolution)[]
        {
            ("沙丘：第二部", "Dune: Part Two", 2024, 8.7, false, true, "科幻 / 冒险", "4K"),
            ("银翼杀手 2049", "Blade Runner 2049", 2017, 8.3, true, true, "科幻 / 剧情", "4K"),
            ("奥本海默", "Oppenheimer", 2023, 8.6, false, false, "剧情 / 历史", "4K"),
            ("寄生虫", "Parasite", 2019, 8.5, true, false, "剧情 / 惊悚", "1080P"),
            ("瞬息全宇宙", "Everything Everywhere All at Once", 2022, 7.9, false, true, "奇幻 / 喜剧", "4K"),
            ("花样年华", "In the Mood for Love", 2000, 8.1, true, false, "爱情 / 剧情", "1080P"),
            ("星际穿越", "Interstellar", 2014, 8.7, true, true, "科幻 / 冒险", "4K"),
            ("坠落的审判", "Anatomy of a Fall", 2023, 7.8, false, false, "剧情 / 悬疑", "1080P"),
            ("驾驶我的车", "Drive My Car", 2021, 7.6, false, false, "剧情", "1080P"),
            ("机器人之梦", "Robot Dreams", 2023, 8.0, false, true, "动画 / 剧情", "1080P")
        };
        foreach (var (name, original, year, rating, played, favorite, genre, resolution) in samples)
        {
            var height = resolution == "4K" ? 2160 : 1080;
            Movies.Add(new JellyfinMovie
            {
                Id = Guid.NewGuid().ToString("N"), Type = "Movie", Name = name, OriginalTitle = original, ProductionYear = year,
                CommunityRating = rating, Overview = "这是用于预览 JellyPot 视觉与交互设计的示例条目。连接 Jellyfin 后，这里会显示真实简介、海报、媒体参数和观看状态。",
                RunTimeTicks = TimeSpan.FromMinutes(138).Ticks, Path = $"/media/movies/{original}/movie.mkv",
                Genres = genre.Split(" / ").ToList(), UserData = new JellyfinUserData { Played = played, IsFavorite = favorite },
                MediaStreams = [new MediaStreamInfo { Type = "Video", Height = height, Width = height == 2160 ? 3840 : 1920, Codec = "hevc", VideoRangeType = resolution == "4K" ? "HDR10" : "SDR" }],
                MediaSources = [new MediaSourceInfo { Id = "default", Name = $"{resolution} · HEVC", Path = $"/media/movies/{original}/movie.mkv", Container = "mkv" }]
            });
        }
        _totalCount = Movies.Count;
        _startIndex = 0;
        ApplyView();
    }
}
