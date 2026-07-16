using System.Net;
using System.Net.Http;
using System.Windows;
using JellyPot.App.Services;
using JellyPot.App.ViewModels;
using JellyPot.App.Views;

namespace JellyPot.App;

public partial class App : Application
{
    private HttpClient? _httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        });
        var viewModel = new MainViewModel(
            new JellyfinApiClient(_httpClient),
            new SettingsService(),
            new CredentialService(),
            new PathMappingService(),
            new PotPlayerService(),
            new LocalMediaScanService(),
            new DialogService());
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
