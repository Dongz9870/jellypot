using JellyPot.App.Models;

namespace JellyPot.App.Services;

public sealed class PathMappingService
{
    public PathResolution Resolve(string serverPath, IEnumerable<PathMapping> mappings)
    {
        if (string.IsNullOrWhiteSpace(serverPath)) throw new ArgumentException("Jellyfin 没有返回媒体路径。", nameof(serverPath));

        var source = NormalizeServerPath(serverPath);
        var mapping = mappings
            .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.ServerRoot) && !string.IsNullOrWhiteSpace(x.WindowsRoot))
            .OrderByDescending(x => NormalizeServerPath(x.ServerRoot).Length)
            .FirstOrDefault(x => IsPathPrefix(source, NormalizeServerPath(x.ServerRoot)));

        if (mapping is null)
        {
            if (LooksLikeWindowsPath(serverPath))
            {
                var directPath = serverPath.Trim().Replace('/', '\\');
                return new PathResolution(
                    new PathMapping { Name = "Windows / UNC 直连", Enabled = true, ServerRoot = Path.GetPathRoot(directPath) ?? directPath, WindowsRoot = Path.GetPathRoot(directPath) ?? directPath },
                    serverPath,
                    directPath);
            }
            throw new InvalidOperationException($"没有启用的路径映射可处理：{serverPath}");
        }

        var root = NormalizeServerPath(mapping.ServerRoot);
        var relative = source[root.Length..].TrimStart('/');
        var windowsRoot = mapping.WindowsRoot.Trim().TrimEnd('\\', '/');
        var result = string.IsNullOrEmpty(relative) ? windowsRoot : windowsRoot + "\\" + relative.Replace('/', '\\');
        return new PathResolution(mapping, serverPath, result);
    }

    private static string NormalizeServerPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.Length > 1) normalized = normalized.TrimEnd('/');
        return normalized;
    }

    private static bool IsPathPrefix(string path, string root) =>
        root == "/"
            ? path.StartsWith("/", StringComparison.Ordinal)
            : path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeWindowsPath(string value) =>
        value.TrimStart().StartsWith("\\\\", StringComparison.Ordinal)
        || (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'));
}
