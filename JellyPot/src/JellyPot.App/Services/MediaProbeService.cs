using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using JellyPot.App.Models;

namespace JellyPot.App.Services;

public sealed class MediaProbeService
{
    private readonly string? _ffprobePath = FindFfprobe();

    public MediaStreamInfo? ProbeVideo(string path)
    {
        if (!File.Exists(path)) return null;
        return TryProbeWithFfprobe(path) ?? TryReadWindowsMetadata(path);
    }

    private MediaStreamInfo? TryProbeWithFfprobe(string path)
    {
        if (_ffprobePath is null) return null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[]
            {
                "-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=codec_name,width,height,color_transfer",
                "-of", "json", path
            }) startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(8_000))
            {
                process.Kill(true);
                return null;
            }
            Task.WaitAll([outputTask, errorTask], 1_000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(outputTask.Result)) return null;

            using var document = JsonDocument.Parse(outputTask.Result);
            if (!document.RootElement.TryGetProperty("streams", out var streams)) return null;
            foreach (var stream in streams.EnumerateArray())
            {
                var width = ReadInt(stream, "width");
                var height = ReadInt(stream, "height");
                if (width is null && height is null) continue;
                var transfer = ReadString(stream, "color_transfer");
                return new MediaStreamInfo
                {
                    Type = "Video",
                    Width = width,
                    Height = height,
                    Codec = ReadString(stream, "codec_name"),
                    VideoRangeType = transfer?.ToLowerInvariant() switch
                    {
                        "smpte2084" => "HDR10",
                        "arib-std-b67" => "HLG",
                        _ => null
                    }
                };
            }
        }
        catch (Exception) when (_ffprobePath is not null)
        {
            // Fall back to the Windows property system when ffprobe cannot read a file.
        }
        return null;
    }

    private static MediaStreamInfo? TryReadWindowsMetadata(string path)
    {
        object? shell = null;
        object? folder = null;
        object? item = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            shell = Activator.CreateInstance(shellType);
            var directory = Path.GetDirectoryName(path);
            if (shell is null || directory is null) return null;
            folder = ((dynamic)shell).NameSpace(directory);
            if (folder is null) return null;
            item = ((dynamic)folder).ParseName(Path.GetFileName(path));
            if (item is null) return null;

            var width = ToInt(((dynamic)item).ExtendedProperty("System.Video.FrameWidth"));
            var height = ToInt(((dynamic)item).ExtendedProperty("System.Video.FrameHeight"));
            return width is null && height is null
                ? null
                : new MediaStreamInfo { Type = "Video", Width = width, Height = height };
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(item);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) && result > 0 ? result : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ToInt(object? value)
    {
        try
        {
            var result = Convert.ToInt32(value);
            return result > 0 ? result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
        catch (Exception)
        {
            // Metadata probing is best-effort and must never fail a library scan.
        }
    }

    private static string? FindFfprobe()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("JELLYPOT_FFPROBE"),
            Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Jellyfin", "Server", "ffprobe.exe")
        };
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, "ffprobe.exe")));
        return candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }
}
