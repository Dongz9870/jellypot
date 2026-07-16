using System.Net.Http;
using System.IO;
using System.Windows;
using JellyPot.App.Models;
using JellyPot.App.Services;
using JellyPot.App.ViewModels;
using JellyPot.App.Views;

namespace JellyPot.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Linux 路径转 UNC", LinuxPathToUnc),
            ("最长前缀优先", LongestPrefixWins),
            ("Windows 同路径映射", WindowsIdentityMapping),
            ("未命中时抛出明确异常", MissingMappingThrows),
            ("路径前缀必须位于目录边界", PrefixRequiresDirectoryBoundary),
            ("中文和空格路径", ChineseAndSpaces),
            ("禁用规则不参与匹配", DisabledMappingIgnored),
            ("UNC 路径无需映射可直接播放", DirectUncPath),
            ("扫描视频并关联同目录海报", ScanVideoWithSidecarPoster),
            ("宽银幕分辨率按真实宽高分档", CroppedResolutionClassification),
            ("HDR 与 SDR 动态范围识别", DynamicRangeClassification),
            ("蓝光原盘格式识别", BluRayFormatIdentification),
            ("优先读取媒体版本内的视频流", NestedMediaSourceResolution),
            ("详情页跟随所选播放版本显示分辨率", SelectedSourceResolution),
            ("电影、电视与详情模板可渲染", MediaViewsRender)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL  {test.Name}: {ex}"); }
        }
        Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} tests passed");
        return failed == 0 ? 0 : 1;
    }

    private static void LinuxPathToUnc()
    {
        var result = Resolve("/media/movies/a.mkv", new() { Name = "NAS", ServerRoot = "/media/movies", WindowsRoot = @"\\NAS\Movies" });
        Equal(@"\\NAS\Movies\a.mkv", result.WindowsPath);
        Equal("NAS", result.Mapping.Name);
    }

    private static void LongestPrefixWins()
    {
        var service = new PathMappingService();
        var result = service.Resolve("/media/uhd/a.mkv",
        [
            new PathMapping { Name = "media", ServerRoot = "/media", WindowsRoot = @"D:\Media" },
            new PathMapping { Name = "uhd", ServerRoot = "/media/uhd", WindowsRoot = @"\\NAS\UHD" }
        ]);
        Equal(@"\\NAS\UHD\a.mkv", result.WindowsPath);
        Equal("uhd", result.Mapping.Name);
    }

    private static void WindowsIdentityMapping()
    {
        var result = Resolve(@"D:\Movies\a.mkv", new() { ServerRoot = @"D:\Movies", WindowsRoot = @"D:\Movies" });
        Equal(@"D:\Movies\a.mkv", result.WindowsPath);
    }

    private static void MissingMappingThrows()
    {
        try
        {
            Resolve("/other/a.mkv", new() { ServerRoot = "/media", WindowsRoot = @"D:\Media" });
            throw new Exception("预期抛出 InvalidOperationException。");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("没有启用的路径映射")) { }
    }

    private static void PrefixRequiresDirectoryBoundary()
    {
        try
        {
            Resolve("/media/movies-old/a.mkv", new() { ServerRoot = "/media/movies", WindowsRoot = @"D:\Movies" });
            throw new Exception("相似字符串不应被当成目录前缀。");
        }
        catch (InvalidOperationException) { }
    }

    private static void ChineseAndSpaces()
    {
        var result = Resolve("/mnt/nas/电影/花样年华 (2000)/正片.mkv", new() { ServerRoot = "/mnt/nas/电影", WindowsRoot = @"\\NAS\电影" });
        Equal(@"\\NAS\电影\花样年华 (2000)\正片.mkv", result.WindowsPath);
    }

    private static void DisabledMappingIgnored()
    {
        var service = new PathMappingService();
        var result = service.Resolve("/media/a.mkv",
        [
            new PathMapping { Enabled = false, ServerRoot = "/media", WindowsRoot = @"D:\Wrong" },
            new PathMapping { Name = "enabled", Enabled = true, ServerRoot = "/", WindowsRoot = @"D:\Root" }
        ]);
        Equal(@"D:\Root\media\a.mkv", result.WindowsPath);
    }

    private static void DirectUncPath()
    {
        const string path = @"\\NAS\Movies\No.Time.to.Die\movie.mkv";
        var result = new PathMappingService().Resolve(path, []);
        Equal(path, result.WindowsPath);
        Equal("Windows / UNC 直连", result.Mapping.Name);
    }

    private static void ScanVideoWithSidecarPoster()
    {
        var root = Path.Combine(Path.GetTempPath(), "JellyPotTests", Guid.NewGuid().ToString("N"));
        var movieDirectory = Path.Combine(root, "No Time to Die (2021)");
        Directory.CreateDirectory(movieDirectory);
        try
        {
            var video = Path.Combine(movieDirectory, "movie.mkv");
            var poster = Path.Combine(movieDirectory, "poster.jpg");
            File.WriteAllBytes(video, []);
            File.WriteAllBytes(poster, [0xFF, 0xD8, 0xFF, 0xD9]);
            var items = new LocalMediaScanService().ScanAsync([root], "Movie").GetAwaiter().GetResult();
            if (items.Count != 1) throw new Exception($"预期发现 1 个视频，实际 {items.Count}。 ");
            Equal(video, items[0].Path!);
            Equal(poster, items[0].PosterUrl!);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void CroppedResolutionClassification()
    {
        Equal("4K", VideoResolution.GetLabel(3840, 1608));
        Equal("1080P", VideoResolution.GetLabel(1920, 800));
        Equal("720P", VideoResolution.GetLabel(1280, 536));
        Equal("未知", VideoResolution.GetLabel(null, null));
    }

    private static void DynamicRangeClassification()
    {
        Equal("HDR", VideoDynamicRange.GetLabel(new MediaStreamInfo { Type = "Video", VideoRangeType = "HDR10" }));
        Equal("HDR", VideoDynamicRange.GetLabel(new MediaStreamInfo { Type = "Video", VideoRangeType = "DOVIWithHDR10" }));
        Equal("HDR", VideoDynamicRange.GetLabel(new MediaStreamInfo { Type = "Video", VideoRange = "HDR", ColorTransfer = "smpte2084" }));
        Equal("SDR", VideoDynamicRange.GetLabel(new MediaStreamInfo { Type = "Video", VideoRangeType = "SDR" }));
        Equal("SDR", VideoDynamicRange.GetLabel(new MediaStreamInfo { Type = "Video", ColorTransfer = "bt709" }));
        Equal("未知", VideoDynamicRange.GetLabel(new MediaStreamInfo { Type = "Video" }));
    }

    private static void BluRayFormatIdentification()
    {
        if (!VideoMediaFormat.IsBluRay(new MediaSourceInfo { VideoType = "BluRay" })) throw new Exception("Jellyfin BluRay 类型应显示蓝光角标。");
        if (!VideoMediaFormat.IsBluRay(new MediaSourceInfo { VideoType = "Iso", IsoType = "BluRay", Path = @"D:\Movies\disc.iso" })) throw new Exception("蓝光 ISO 应显示蓝光角标。");
        if (!VideoMediaFormat.IsBluRay(new MediaSourceInfo { Path = @"D:\Movies\Film\BDMV\STREAM\00001.m2ts" })) throw new Exception("BDMV 目录应显示蓝光角标。");
        if (VideoMediaFormat.IsBluRay(new MediaSourceInfo { VideoType = "Iso", Path = @"D:\Movies\disc.iso" })) throw new Exception("类型未知的 ISO 不应误标为蓝光。");
        if (VideoMediaFormat.IsBluRay(new MediaSourceInfo { Path = @"D:\Movies\Film.BluRay.1080p.mkv" })) throw new Exception("不能仅凭文件名中的 BluRay 字样判断蓝光原盘。");
    }

    private static void NestedMediaSourceResolution()
    {
        var movie = new JellyfinMovie
        {
            MediaStreams = [new MediaStreamInfo { Type = "Video", Width = 1280, Height = 720 }],
            MediaSources =
            [
                new MediaSourceInfo
                {
                    MediaStreams = [new MediaStreamInfo { Type = "Video", Width = 3840, Height = 1608 }]
                }
            ]
        };
        Equal("4K", movie.ResolutionText);
        Equal("4K · 3840×1608", movie.ResolutionDetailText);
    }

    private static void SelectedSourceResolution()
    {
        var movie = new JellyfinMovie
        {
            Path = @"D:\Movies\movie.mkv",
            MediaSources =
            [
                new MediaSourceInfo { Name = "4K 蓝光", Path = @"D:\Movies\movie-4k.iso", VideoType = "Iso", IsoType = "BluRay", MediaStreams = [new MediaStreamInfo { Type = "Video", Width = 3840, Height = 1608, VideoRangeType = "HDR10" }] },
                new MediaSourceInfo { Name = "1080P", Path = @"D:\Movies\movie-1080p.mkv", VideoType = "VideoFile", MediaStreams = [new MediaStreamInfo { Type = "Video", Width = 1920, Height = 800, VideoRangeType = "SDR" }] }
            ]
        };
        var details = new MovieDetailsViewModel(movie, new AppSettings(), new PathMappingService(), new PotPlayerService(), new DialogService());
        Equal("4K", details.SelectedResolutionText);
        Equal("HDR", details.SelectedDynamicRangeText);
        if (!details.SelectedHasBluRay) throw new Exception("所选蓝光版本应显示蓝光角标。");
        details.SelectedSource = movie.MediaSources[1];
        Equal("1080P", details.SelectedResolutionText);
        Equal("1080P · 1920×800", details.SelectedResolutionDetailText);
        Equal("SDR", details.SelectedDynamicRangeText);
        if (details.SelectedHasBluRay) throw new Exception("切换到普通视频版本后应隐藏蓝光角标。");
    }

    private static void MediaViewsRender()
    {
        var application = new JellyPot.App.App();
        application.InitializeComponent();
        if (application.FindResource("AppIcon") is not System.Windows.Media.ImageSource) throw new Exception("应用图标资源没有加载。");
        var iconWindow = new AddCategoryWindow([]);
        if (iconWindow.Icon is null) throw new Exception("窗口没有应用任务栏图标。");
        using var httpClient = new HttpClient();
        var settings = new AppSettings
        {
            PathMappings = [new PathMapping { Name = "Demo", ServerRoot = "/media/movies", WindowsRoot = @"D:\Movies" }]
        };
        var library = new LibraryViewModel(new JellyfinApiClient(httpClient), settings, new SettingsService(), new LocalMediaScanService(), "demo", MediaCategory.Movies(), true);
        library.LoadAsync().GetAwaiter().GetResult();
        Render(new LibraryView { DataContext = library });

        var movie = library.VisibleMovies.First();
        var details = new MovieDetailsViewModel(movie, settings, new PathMappingService(), new PotPlayerService(), new DialogService());
        Render(new MovieDetailsView { DataContext = details });

        library.ShowCategoryAsync(MediaCategory.Television()).GetAwaiter().GetResult();
        if (!library.VisibleMovies.Any(x => x.Type == "Series")) throw new Exception("电视分类没有加载示例剧集。");
        Render(new LibraryView { DataContext = library });
    }

    private static void Render(FrameworkElement element)
    {
        element.Measure(new Size(1200, 760));
        element.Arrange(new Rect(0, 0, 1200, 760));
        element.UpdateLayout();
    }

    private static PathResolution Resolve(string path, PathMapping mapping) => new PathMappingService().Resolve(path, [mapping]);
    private static void Equal(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw new Exception($"Expected '{expected}', actual '{actual}'.");
    }
}
