using System.Text.Json;
using JellyPot.App.Models;

namespace JellyPot.App.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string SettingsPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JellyPot", "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath)) return CreateDefault();
        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions) ?? CreateDefault();
            EnsureDefaults(settings);
            return settings;
        }
        catch (JsonException)
        {
            return CreateDefault();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions);
        File.Move(temporaryPath, SettingsPath, true);
    }

    private static AppSettings CreateDefault() => new()
    {
        PathMappings = [new PathMapping { Name = "NAS 电影", ServerRoot = "/media/movies", WindowsRoot = @"\\NAS\Movies", Enabled = true }]
    };

    private static void EnsureDefaults(AppSettings settings)
    {
        settings.Server ??= new ServerSettings();
        settings.Playback ??= new PlaybackSettings();
        settings.Ui ??= new UiSettings();
        settings.PathMappings ??= [];
        settings.MediaCategories ??= [];
        settings.LocalMediaSources ??= [];
        if (string.IsNullOrWhiteSpace(settings.Server.DeviceId)) settings.Server.DeviceId = Guid.NewGuid().ToString("N");
    }
}
