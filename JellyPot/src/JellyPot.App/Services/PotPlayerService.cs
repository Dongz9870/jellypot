using System.Diagnostics;

namespace JellyPot.App.Services;

public sealed class PotPlayerService
{
    public Process Launch(string executablePath, string mediaPath)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("没有找到 PotPlayer，请先在设置中选择可执行文件。", executablePath);
        var playablePath = ResolvePlayablePath(mediaPath);
        var startInfo = new ProcessStartInfo { FileName = executablePath, UseShellExecute = false };
        startInfo.ArgumentList.Add(playablePath);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("PotPlayer 启动失败。");
    }

    public Process LaunchPlayerOnly(string executablePath)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("没有找到 PotPlayer。", executablePath);
        return Process.Start(new ProcessStartInfo { FileName = executablePath, UseShellExecute = false })
            ?? throw new InvalidOperationException("PotPlayer 启动失败。");
    }

    public string ResolvePlayablePath(string mediaPath)
    {
        if (File.Exists(mediaPath)) return mediaPath;
        if (!Directory.Exists(mediaPath)) throw new FileNotFoundException("无法访问电影文件或目录，请检查 NAS 权限和路径映射。", mediaPath);

        var directIndex = Path.Combine(mediaPath, "index.bdmv");
        var nestedIndex = Path.Combine(mediaPath, "BDMV", "index.bdmv");
        if (File.Exists(directIndex)) return directIndex;
        if (File.Exists(nestedIndex)) return nestedIndex;
        throw new FileNotFoundException("目录存在，但未找到可播放文件或 BDMV\\index.bdmv。", mediaPath);
    }

    public string? AutoLocate()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DAUM", "PotPlayer", "PotPlayerMini64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DAUM", "PotPlayer", "PotPlayerMini.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PotPlayer", "PotPlayerMini64.exe")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
