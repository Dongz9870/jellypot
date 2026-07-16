using System.Net.Http;
using JellyPot.App.Infrastructure;
using JellyPot.App.Models;
using JellyPot.App.Services;

namespace JellyPot.App.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private readonly JellyfinApiClient _apiClient;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentialService;
    private string _serverUrl;
    private string _username;
    private string _password = string.Empty;
    private string _statusMessage = "填写服务器地址并测试连接";
    private bool _statusIsError;
    private bool _isBusy;

    public LoginViewModel(JellyfinApiClient apiClient, AppSettings settings, SettingsService settingsService, CredentialService credentialService)
    {
        _apiClient = apiClient;
        _settings = settings;
        _settingsService = settingsService;
        _credentialService = credentialService;
        _serverUrl = settings.Server.BaseUrl;
        _username = settings.Server.Username ?? string.Empty;
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);
        DemoCommand = new RelayCommand(() => DemoRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler<LoginCompletedEventArgs>? LoginCompleted;
    public event EventHandler? DemoRequested;
    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand LoginCommand { get; }
    public RelayCommand DemoCommand { get; }

    public string ServerUrl { get => _serverUrl; set => SetProperty(ref _serverUrl, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            TestConnectionCommand.NotifyCanExecuteChanged();
            LoginCommand.NotifyCanExecuteChanged();
        }
    }

    public void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        try
        {
            ApplyServerUrl();
            _apiClient.Configure(_settings.Server);
            var info = await _apiClient.GetPublicInfoAsync();
            SetStatus($"已连接到 {info.ServerName} · Jellyfin {info.Version}", false);
        }
        catch (Exception ex) { SetStatus(ToFriendlyMessage(ex), true); }
        finally { IsBusy = false; }
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            SetStatus("请输入用户名和密码。", true);
            return;
        }

        IsBusy = true;
        try
        {
            ApplyServerUrl();
            _apiClient.Configure(_settings.Server);
            var info = await _apiClient.GetPublicInfoAsync();
            var result = await _apiClient.AuthenticateAsync(Username.Trim(), Password);
            _settings.Server.UserId = result.User.Id;
            _settings.Server.Username = result.User.Name;
            _credentialService.SaveToken(_settings.Server.BaseUrl, result.AccessToken);
            await _settingsService.SaveAsync(_settings);
            Password = string.Empty;
            SetStatus($"欢迎回来，{result.User.Name}", false);
            LoginCompleted?.Invoke(this, new LoginCompletedEventArgs(result, info));
        }
        catch (Exception ex) { SetStatus(ToFriendlyMessage(ex), true); }
        finally { IsBusy = false; }
    }

    private void ApplyServerUrl()
    {
        _settings.Server.BaseUrl = ServerUrl.Trim().TrimEnd('/');
        ServerUrl = _settings.Server.BaseUrl;
    }

    private static string ToFriendlyMessage(Exception ex) => ex switch
    {
        TaskCanceledException => "连接超时，请检查服务器地址或网络。",
        HttpRequestException => $"无法连接 Jellyfin：{ex.Message}",
        ArgumentException => ex.Message,
        _ => ex.Message
    };
}

public sealed record LoginCompletedEventArgs(AuthenticationResult Result, JellyfinServerInfo ServerInfo);
