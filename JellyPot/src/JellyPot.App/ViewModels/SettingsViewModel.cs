using System.Collections.ObjectModel;
using System.ComponentModel;
using JellyPot.App.Infrastructure;
using JellyPot.App.Models;
using JellyPot.App.Services;

namespace JellyPot.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly JellyfinApiClient _apiClient;
    private readonly CredentialService _credentialService;
    private readonly PathMappingService _pathMappingService;
    private readonly PotPlayerService _potPlayerService;
    private readonly DialogService _dialogService;
    private PathMapping? _selectedMapping;
    private string _sampleServerPath = "/media/movies/Sample Movie/movie.mkv";
    private string _previewPath = "填写样本路径后可预览转换结果";
    private string _statusMessage = string.Empty;
    private bool _statusIsError;

    public SettingsViewModel(AppSettings settings, SettingsService settingsService, JellyfinApiClient apiClient, CredentialService credentialService, PathMappingService pathMappingService, PotPlayerService potPlayerService, DialogService dialogService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _apiClient = apiClient;
        _credentialService = credentialService;
        _pathMappingService = pathMappingService;
        _potPlayerService = potPlayerService;
        _dialogService = dialogService;
        Mappings = new ObservableCollection<PathMapping>(settings.PathMappings);
        foreach (var mapping in Mappings) mapping.PropertyChanged += MappingOnPropertyChanged;
        SelectedMapping = Mappings.FirstOrDefault();
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        TestServerCommand = new AsyncRelayCommand(TestServerAsync);
        BrowsePlayerCommand = new RelayCommand(BrowsePlayer);
        AutoFindPlayerCommand = new RelayCommand(AutoFindPlayer);
        TestPlayerCommand = new RelayCommand(TestPlayer);
        AddMappingCommand = new RelayCommand(AddMapping);
        RemoveMappingCommand = new RelayCommand(RemoveMapping, () => SelectedMapping is not null);
        BrowseWindowsRootCommand = new RelayCommand(BrowseWindowsRoot, () => SelectedMapping is not null);
        PreviewMappingCommand = new RelayCommand(UpdatePreview);
    }

    public event EventHandler? BackRequested;
    public event EventHandler? Saved;
    public ObservableCollection<PathMapping> Mappings { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand TestServerCommand { get; }
    public RelayCommand BrowsePlayerCommand { get; }
    public RelayCommand AutoFindPlayerCommand { get; }
    public RelayCommand TestPlayerCommand { get; }
    public RelayCommand AddMappingCommand { get; }
    public RelayCommand RemoveMappingCommand { get; }
    public RelayCommand BrowseWindowsRootCommand { get; }
    public RelayCommand PreviewMappingCommand { get; }

    public string ServerBaseUrl { get => _settings.Server.BaseUrl; set { _settings.Server.BaseUrl = value; OnPropertyChanged(); } }
    public string PotPlayerExecutable { get => _settings.Playback.PotPlayerExecutable; set { _settings.Playback.PotPlayerExecutable = value; OnPropertyChanged(); } }
    public bool AskToMarkPlayedAfterExit { get => _settings.Playback.AskToMarkPlayedAfterExit; set { _settings.Playback.AskToMarkPlayedAfterExit = value; OnPropertyChanged(); } }
    public PathMapping? SelectedMapping
    {
        get => _selectedMapping;
        set
        {
            if (!SetProperty(ref _selectedMapping, value)) return;
            RemoveMappingCommand?.NotifyCanExecuteChanged();
            BrowseWindowsRootCommand?.NotifyCanExecuteChanged();
            UpdatePreview();
        }
    }
    public string SampleServerPath { get => _sampleServerPath; set { if (SetProperty(ref _sampleServerPath, value)) UpdatePreview(); } }
    public string PreviewPath { get => _previewPath; private set => SetProperty(ref _previewPath, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }

    private async Task SaveAsync()
    {
        try
        {
            _settings.Server.BaseUrl = ServerBaseUrl.Trim().TrimEnd('/');
            _settings.PathMappings = Mappings.ToList();
            await _settingsService.SaveAsync(_settings);
            SetStatus("设置已保存", false);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    private async Task TestServerAsync()
    {
        try
        {
            _settings.Server.BaseUrl = ServerBaseUrl.Trim().TrimEnd('/');
            _apiClient.Configure(_settings.Server, _credentialService.ReadToken(_settings.Server.BaseUrl));
            var info = await _apiClient.GetPublicInfoAsync();
            SetStatus($"服务器可用：{info.ServerName} · {info.Version}", false);
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    private void BrowsePlayer()
    {
        var selected = _dialogService.SelectPotPlayer();
        if (selected is not null) PotPlayerExecutable = selected;
    }

    private void AutoFindPlayer()
    {
        var path = _potPlayerService.AutoLocate();
        if (path is null) SetStatus("没有在常见安装目录找到 PotPlayer。", true);
        else { PotPlayerExecutable = path; SetStatus("已找到 PotPlayer。", false); }
    }

    private void TestPlayer()
    {
        try { _potPlayerService.LaunchPlayerOnly(PotPlayerExecutable); SetStatus("PotPlayer 已启动。", false); }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    private void AddMapping()
    {
        var mapping = new PathMapping { Name = $"路径映射 {Mappings.Count + 1}" };
        mapping.PropertyChanged += MappingOnPropertyChanged;
        Mappings.Add(mapping);
        SelectedMapping = mapping;
    }

    private void RemoveMapping()
    {
        if (SelectedMapping is null) return;
        SelectedMapping.PropertyChanged -= MappingOnPropertyChanged;
        Mappings.Remove(SelectedMapping);
        SelectedMapping = Mappings.FirstOrDefault();
    }

    private void BrowseWindowsRoot()
    {
        if (SelectedMapping is null) return;
        var selected = _dialogService.SelectFolder(SelectedMapping.WindowsRoot);
        if (selected is not null) SelectedMapping.WindowsRoot = selected;
    }

    private void MappingOnPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        try
        {
            var result = _pathMappingService.Resolve(SampleServerPath, Mappings);
            PreviewPath = $"{result.WindowsPath}  ·  命中「{result.Mapping.Name}」";
        }
        catch (Exception ex) { PreviewPath = ex.Message; }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
