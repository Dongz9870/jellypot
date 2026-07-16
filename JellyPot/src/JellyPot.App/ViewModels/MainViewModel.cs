using System.Collections.ObjectModel;
using JellyPot.App.Infrastructure;
using JellyPot.App.Models;
using JellyPot.App.Services;

namespace JellyPot.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly JellyfinApiClient _apiClient;
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentialService;
    private readonly PathMappingService _pathMappingService;
    private readonly PotPlayerService _potPlayerService;
    private readonly LocalMediaScanService _localMediaScanService;
    private readonly DialogService _dialogService;
    private AppSettings _settings = new();
    private LoginViewModel _login;
    private LibraryViewModel? _library;
    private object? _currentPage;
    private bool _isAuthenticated;
    private string _serverName = "Jellyfin";
    private string _userDisplayName = "未登录";
    private string _pageTitle = "登录";
    private MediaCategory _activeCategory = MediaCategory.Movies();

    public MainViewModel(JellyfinApiClient apiClient, SettingsService settingsService, CredentialService credentialService, PathMappingService pathMappingService, PotPlayerService potPlayerService, LocalMediaScanService localMediaScanService, DialogService dialogService)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _credentialService = credentialService;
        _pathMappingService = pathMappingService;
        _potPlayerService = potPlayerService;
        _localMediaScanService = localMediaScanService;
        _dialogService = dialogService;
        _login = CreateLogin();
        _currentPage = _login;
        InitializeNavigationCategories();
        LibraryCommand = new RelayCommand(ShowLibrary, () => IsAuthenticated && _library is not null);
        SettingsCommand = new RelayCommand(ShowSettings, () => IsAuthenticated);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => IsAuthenticated);
        OpenCategoryCommand = new RelayCommand<MediaCategory>(category => { if (category is not null) _ = OpenCategoryAsync(category); });
        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync, () => IsAuthenticated && _library is not null);
        RemoveCategoryCommand = new RelayCommand<MediaCategory>(category => { if (category is not null) _ = RemoveCategoryAsync(category); }, category => category?.CanRemove == true);
        ManualAddScanDirectoryCommand = new RelayCommand<MediaCategory>(category => { if (category is not null) _ = ManualAddScanDirectoryAsync(category); });
        AutoScanCategoryCommand = new RelayCommand<MediaCategory>(category => { if (category is not null) _ = AutoScanCategoryAsync(category); });
        ClearScanDirectoriesCommand = new RelayCommand<MediaCategory>(category => { if (category is not null) _ = ClearScanDirectoriesAsync(category); });
    }

    public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public LoginViewModel Login { get => _login; private set => SetProperty(ref _login, value); }
    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (!SetProperty(ref _isAuthenticated, value)) return;
            LibraryCommand.NotifyCanExecuteChanged();
            SettingsCommand.NotifyCanExecuteChanged();
            LogoutCommand.NotifyCanExecuteChanged();
            AddCategoryCommand.NotifyCanExecuteChanged();
        }
    }
    public string ServerName { get => _serverName; private set => SetProperty(ref _serverName, value); }
    public string UserDisplayName { get => _userDisplayName; private set => SetProperty(ref _userDisplayName, value); }
    public string PageTitle { get => _pageTitle; private set => SetProperty(ref _pageTitle, value); }
    public RelayCommand LibraryCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public ObservableCollection<MediaCategory> NavigationCategories { get; } = [];
    public RelayCommand<MediaCategory> OpenCategoryCommand { get; }
    public AsyncRelayCommand AddCategoryCommand { get; }
    public RelayCommand<MediaCategory> RemoveCategoryCommand { get; }
    public RelayCommand<MediaCategory> ManualAddScanDirectoryCommand { get; }
    public RelayCommand<MediaCategory> AutoScanCategoryCommand { get; }
    public RelayCommand<MediaCategory> ClearScanDirectoriesCommand { get; }

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();
        InitializeNavigationCategories();
        Login = CreateLogin();
        CurrentPage = Login;
        if (string.IsNullOrWhiteSpace(_settings.Server.UserId)) return;
        var token = _credentialService.ReadToken(_settings.Server.BaseUrl);
        if (string.IsNullOrWhiteSpace(token)) return;

        try
        {
            _apiClient.Configure(_settings.Server, token);
            var info = await _apiClient.GetPublicInfoAsync();
            ServerName = info.ServerName;
            UserDisplayName = _settings.Server.Username ?? "Jellyfin 用户";
            await EnterLibraryAsync(_settings.Server.UserId, false);
        }
        catch (Exception ex)
        {
            Login.SetStatus($"自动登录暂时不可用：{ex.Message}", true);
            CurrentPage = Login;
        }
    }

    private LoginViewModel CreateLogin()
    {
        var viewModel = new LoginViewModel(_apiClient, _settings, _settingsService, _credentialService);
        viewModel.LoginCompleted += async (_, e) =>
        {
            ServerName = e.ServerInfo.ServerName;
            UserDisplayName = e.Result.User.Name;
            await EnterLibraryAsync(e.Result.User.Id, false);
        };
        viewModel.DemoRequested += async (_, _) =>
        {
            ServerName = "JellyPot 设计预览";
            UserDisplayName = "体验用户";
            await EnterLibraryAsync("demo", true);
        };
        return viewModel;
    }

    private async Task EnterLibraryAsync(string userId, bool demoMode)
    {
        _activeCategory = NavigationCategories.FirstOrDefault() ?? MediaCategory.Movies();
        _library = new LibraryViewModel(_apiClient, _settings, _settingsService, _localMediaScanService, userId, _activeCategory, demoMode);
        _library.MovieOpened += (_, movie) => ShowDetails(movie);
        IsAuthenticated = true;
        ShowLibrary();
        await _library.LoadAsync();
    }

    private void ShowLibrary()
    {
        if (_library is null) return;
        CurrentPage = _library;
        PageTitle = _activeCategory.Name;
    }

    private async Task OpenCategoryAsync(MediaCategory category)
    {
        if (_library is null) return;
        _activeCategory = category;
        CurrentPage = _library;
        PageTitle = category.Name;
        await _library.ShowCategoryAsync(category);
    }

    private async Task AddCategoryAsync()
    {
        if (_library is null) return;
        var category = _dialogService.ShowAddCategory(_library.AvailableLibraries);
        if (category is null) return;

        _settings.MediaCategories.Add(category);
        if (!string.IsNullOrWhiteSpace(category.ServerRoot) && !string.IsNullOrWhiteSpace(category.WindowsRoot))
        {
            _settings.PathMappings.Add(new PathMapping
            {
                CategoryId = category.Id,
                Name = $"{category.Name} 片源",
                ServerRoot = category.ServerRoot,
                WindowsRoot = category.WindowsRoot,
                Enabled = true
            });
        }
        NavigationCategories.Add(category);
        await _settingsService.SaveAsync(_settings);
        await OpenCategoryAsync(category);
    }

    private async Task RemoveCategoryAsync(MediaCategory category)
    {
        if (category.IsBuiltIn || !_dialogService.Confirm("删除自定义分类", $"确定删除“{category.Name}”吗？关联的片源路径映射也会删除。")) return;
        _settings.MediaCategories.RemoveAll(x => x.Id == category.Id);
        _settings.PathMappings.RemoveAll(x => x.CategoryId == category.Id);
        _settings.LocalMediaSources.RemoveAll(x => x.CategoryId == category.Id);
        NavigationCategories.Remove(category);
        await _settingsService.SaveAsync(_settings);
        if (_activeCategory.Id == category.Id)
            await OpenCategoryAsync(NavigationCategories.First());
    }

    private async Task ManualAddScanDirectoryAsync(MediaCategory category)
    {
        if (_library is null) return;
        var directory = _dialogService.SelectFolder();
        if (directory is null) return;
        if (!_settings.LocalMediaSources.Any(x => x.CategoryId == category.Id && string.Equals(x.DirectoryPath, directory, StringComparison.OrdinalIgnoreCase)))
            _settings.LocalMediaSources.Add(new LocalMediaSource { CategoryId = category.Id, DirectoryPath = directory });
        await _settingsService.SaveAsync(_settings);
        await OpenCategoryAsync(category);
        var count = await _library.ScanLocalSourcesAsync(category, GetScanDirectories(category));
        _dialogService.ShowInfo("扫描完成", $"“{category.Name}”共发现 {count} 个可播放视频文件。\n\n扫描目录：\n{directory}");
    }

    private async Task AutoScanCategoryAsync(MediaCategory category)
    {
        if (_library is null) return;
        await OpenCategoryAsync(category);
        var directories = GetScanDirectories(category).ToList();
        var inferred = _library.SuggestScanDirectory(category);
        if (!string.IsNullOrWhiteSpace(inferred) && !directories.Contains(inferred, StringComparer.OrdinalIgnoreCase))
        {
            directories.Add(inferred);
            _settings.LocalMediaSources.Add(new LocalMediaSource { CategoryId = category.Id, DirectoryPath = inferred });
            await _settingsService.SaveAsync(_settings);
        }
        if (directories.Count == 0)
        {
            _dialogService.ShowInfo("没有扫描目录", "暂时无法从 Jellyfin 路径推断 Windows 可访问的公共目录。请先右击分类，选择“手动添加扫描目录”。");
            return;
        }
        var count = await _library.ScanLocalSourcesAsync(category, directories);
        foreach (var source in _settings.LocalMediaSources.Where(x => x.CategoryId == category.Id)) source.LastScanUtc = DateTimeOffset.UtcNow;
        await _settingsService.SaveAsync(_settings);
        _dialogService.ShowInfo("自动扫描完成", $"“{category.Name}”在 {directories.Count} 个目录中发现 {count} 个可播放视频文件。\n\n同目录海报已自动关联。" );
    }

    private async Task ClearScanDirectoriesAsync(MediaCategory category)
    {
        if (_library is null || !_settings.LocalMediaSources.Any(x => x.CategoryId == category.Id))
        {
            _dialogService.ShowInfo("扫描目录", $"“{category.Name}”还没有保存扫描目录。");
            return;
        }
        if (!_dialogService.Confirm("清除扫描目录", $"确定清除“{category.Name}”保存的全部扫描目录吗？\n这不会删除磁盘上的任何文件。")) return;
        _settings.LocalMediaSources.RemoveAll(x => x.CategoryId == category.Id);
        await _settingsService.SaveAsync(_settings);
        await _library.ClearLocalItemsAsync(category);
    }

    private IEnumerable<string> GetScanDirectories(MediaCategory category)
    {
        var configured = _settings.LocalMediaSources
            .Where(x => x.Enabled && x.CategoryId == category.Id)
            .Select(x => x.DirectoryPath);
        IEnumerable<string> categoryRoot = string.IsNullOrWhiteSpace(category.WindowsRoot) ? Array.Empty<string>() : [category.WindowsRoot];
        var mappedRoots = category.Id == "builtin-movies"
            ? _settings.PathMappings.Where(x => x.Enabled && string.IsNullOrWhiteSpace(x.CategoryId)).Select(x => x.WindowsRoot)
            : _settings.PathMappings.Where(x => x.Enabled && x.CategoryId == category.Id).Select(x => x.WindowsRoot);
        return configured.Concat(categoryRoot).Concat(mappedRoots).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void InitializeNavigationCategories()
    {
        NavigationCategories.Clear();
        NavigationCategories.Add(MediaCategory.Movies());
        NavigationCategories.Add(MediaCategory.Television());
        foreach (var category in _settings.MediaCategories.Where(x => !x.IsBuiltIn))
            NavigationCategories.Add(category);
    }

    private void ShowDetails(JellyfinMovie movie)
    {
        var details = new MovieDetailsViewModel(movie, _settings, _pathMappingService, _potPlayerService, _dialogService);
        details.BackRequested += (_, _) => ShowLibrary();
        details.SettingsRequested += (_, _) => ShowSettings();
        CurrentPage = details;
        PageTitle = "影片详情";
    }

    private void ShowSettings()
    {
        var settings = new SettingsViewModel(_settings, _settingsService, _apiClient, _credentialService, _pathMappingService, _potPlayerService, _dialogService);
        settings.BackRequested += (_, _) => ShowLibrary();
        settings.Saved += (_, _) => ShowLibrary();
        CurrentPage = settings;
        PageTitle = "设置";
    }

    private async Task LogoutAsync()
    {
        _credentialService.DeleteToken(_settings.Server.BaseUrl);
        _settings.Server.UserId = null;
        await _settingsService.SaveAsync(_settings);
        IsAuthenticated = false;
        _library = null;
        ServerName = "Jellyfin";
        UserDisplayName = "未登录";
        PageTitle = "登录";
        Login = CreateLogin();
        CurrentPage = Login;
    }
}
