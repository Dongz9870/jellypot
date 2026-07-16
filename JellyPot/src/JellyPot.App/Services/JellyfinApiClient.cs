using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using JellyPot.App.Models;

namespace JellyPot.App.Services;

public sealed class JellyfinApiClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private ServerSettings _server = new();
    private string? _accessToken;

    public void Configure(ServerSettings settings, string? accessToken = null)
    {
        _server = settings;
        _server.BaseUrl = NormalizeBaseUrl(settings.BaseUrl);
        _accessToken = accessToken;
    }

    public async Task<JellyfinServerInfo> GetPublicInfoAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<JellyfinServerInfo>(HttpMethod.Get, "/System/Info/Public", null, false, cancellationToken);

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<AuthenticationResult>(HttpMethod.Post, "/Users/AuthenticateByName", new { Username = username, Pw = password }, false, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.User.Id))
            throw new InvalidOperationException("Jellyfin 登录响应不完整。");
        _accessToken = result.AccessToken;
        return result;
    }

    public async Task<IReadOnlyList<JellyfinLibrary>> GetLibrariesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<QueryResult<JellyfinLibrary>>(HttpMethod.Get, $"/Users/{Uri.EscapeDataString(userId)}/Views", null, true, cancellationToken);
        return result.Items;
    }

    public async Task<MoviePage> GetItemsAsync(string userId, string libraryId, string itemType, int startIndex, int limit, string? searchTerm, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["ParentId"] = libraryId, ["Recursive"] = "true", ["IncludeItemTypes"] = itemType,
            ["EnableImages"] = "true", ["EnableUserData"] = "true",
            ["Fields"] = "Path,Overview,Genres,MediaSources,MediaStreams,ProviderIds,DateCreated",
            ["SortBy"] = "SortName", ["SortOrder"] = "Ascending", ["StartIndex"] = startIndex.ToString(), ["Limit"] = limit.ToString()
        };
        if (!string.IsNullOrWhiteSpace(searchTerm)) parameters["SearchTerm"] = searchTerm.Trim();
        var query = string.Join("&", parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var result = await SendAsync<QueryResult<JellyfinMovie>>(HttpMethod.Get, $"/Users/{Uri.EscapeDataString(userId)}/Items?{query}", null, true, cancellationToken);
        foreach (var movie in result.Items)
            movie.PosterUrl = BuildImageUrl(movie.Id, movie.ImageTags.GetValueOrDefault("Primary"), 380);
        return new MoviePage(result.Items, result.TotalRecordCount, result.StartIndex);
    }

    public async Task<IReadOnlyList<JellyfinMovie>> GetEpisodesAsync(string userId, string seriesId, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["ParentId"] = seriesId,
            ["Recursive"] = "true",
            ["IncludeItemTypes"] = "Episode",
            ["EnableUserData"] = "true",
            ["Fields"] = "Path,Overview,Genres,MediaSources,MediaStreams,ProviderIds,DateCreated",
            ["SortBy"] = "ParentIndexNumber,IndexNumber,SortName",
            ["SortOrder"] = "Ascending",
            ["Limit"] = "10000"
        };
        var query = string.Join("&", parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var result = await SendAsync<QueryResult<JellyfinMovie>>(HttpMethod.Get,
            $"/Users/{Uri.EscapeDataString(userId)}/Items?{query}", null, true, cancellationToken);
        return result.Items.OrderBy(item => item.ParentIndexNumber ?? 0)
            .ThenBy(item => item.IndexNumber ?? 0).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public string BuildImageUrl(string itemId, string? tag, int maxWidth)
    {
        var query = $"maxWidth={maxWidth}&quality=90";
        if (!string.IsNullOrWhiteSpace(tag)) query += $"&tag={Uri.EscapeDataString(tag)}";
        if (!string.IsNullOrWhiteSpace(_accessToken)) query += $"&api_key={Uri.EscapeDataString(_accessToken)}";
        return $"{_server.BaseUrl}/Items/{Uri.EscapeDataString(itemId)}/Images/Primary?{query}";
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, bool requireToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_server.BaseUrl)) throw new InvalidOperationException("请先填写 Jellyfin 服务器地址。");
        if (requireToken && string.IsNullOrWhiteSpace(_accessToken)) throw new InvalidOperationException("Jellyfin 登录已失效，请重新登录。");

        using var request = new HttpRequestMessage(method, _server.BaseUrl + relativeUrl);
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader());
        if (!string.IsNullOrWhiteSpace(_accessToken)) request.Headers.TryAddWithoutValidation("X-Emby-Token", _accessToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 300) detail = detail[..300];
            throw new HttpRequestException($"Jellyfin 请求失败（{(int)response.StatusCode} {response.ReasonPhrase}）。{detail}");
        }
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Jellyfin 返回了空响应。");
    }

    private string BuildAuthorizationHeader()
    {
        static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var header = $"MediaBrowser Client=\"{Escape(_server.ClientName)}\", Device=\"{Escape(_server.DeviceName)}\", DeviceId=\"{Escape(_server.DeviceId)}\", Version=\"{Escape(_server.ClientVersion)}\"";
        if (!string.IsNullOrWhiteSpace(_accessToken)) header += $", Token=\"{Escape(_accessToken)}\"";
        return header;
    }

    private static string NormalizeBaseUrl(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("服务器地址必须是有效的 HTTP 或 HTTPS 地址。", nameof(value));
        return value;
    }
}
