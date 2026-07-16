# JellyPot：Jellyfin 海报管理 + PotPlayer 本地直读客户端

**文档版本：** 0.1<br>
**目标平台：** Windows 11 x64<br>
**推荐技术栈：** C#、.NET 8、WPF、MVVM、VS Code<br>
**核心目标：** 使用 Jellyfin 管理电影资料与海报，播放时让 PotPlayer 直接打开本地硬盘或 NAS 文件路径。

---

## 1. 项目定位

JellyPot 不是 Jellyfin Server 的替代品，也不是视频转码服务器。它是一个 Windows 专用的轻量客户端：

```text
Jellyfin Server
  ├─ 扫描媒体文件
  ├─ 刮削海报、简介、演员、分类
  ├─ 保存收藏、已看状态和播放进度
  └─ 通过 API 提供媒体资料
          │
          ▼
JellyPot（本项目）
  ├─ 登录 Jellyfin
  ├─ 显示海报墙、详情、搜索和筛选
  ├─ 读取媒体条目的服务器路径
  ├─ 把服务器路径转换为 Windows 可访问路径
  └─ 调用 PotPlayer
          │
          ▼
PotPlayer
  └─ 直接读取 D:\Movies\... 或 \\NAS\Movies\...
```

### 1.1 为什么这样设计

当 Jellyfin 客户端以 Direct Play 播放时，视频本身也不会重新编码；但 PotPlayer 方案仍有明显价值：

- 完整保留现有 PotPlayer、LAV、字幕、HDR、音频输出、EQ 和快捷键设置；
- 播放路径由 Windows 直接访问，不依赖浏览器或 Jellyfin 客户端的格式支持；
- Jellyfin 只负责资料管理，播放端不请求转码；
- 可以清楚看到最终打开的真实文件路径，排错更直观。

项目应明确区分两个概念：

- **不转码：** Jellyfin Direct Play 同样可以做到；
- **直接访问文件系统：** JellyPot + PotPlayer 的目标，播放数据从 SMB/本地磁盘进入 PotPlayer。

---

## 2. 第一版范围

### 2.1 必须完成

1. 连接 Jellyfin Server；
2. 用户名和密码登录；
3. 获取电影媒体库；
4. 显示电影海报墙；
5. 显示电影详情；
6. 搜索、排序、已看状态显示；
7. 配置 PotPlayer 可执行文件；
8. 配置一条或多条服务器路径到 Windows 路径的映射；
9. 点击“PotPlayer 播放”后直接打开原文件；
10. 播放前检查路径是否存在，并给出可理解的错误信息；
11. 保存非敏感设置；
12. 不在配置文件中明文保存 Jellyfin 密码。

### 2.2 第一版暂缓

- 精确同步 PotPlayer 当前播放秒数；
- 电视剧连续播放；
- 蓝光导航菜单；
- 远程互联网播放；
- 手机、电视客户端；
- 自动更新；
- 多用户快速切换；
- 修改 Jellyfin 元数据。

第一版的成功标准不是功能数量，而是稳定完成下面的闭环：

```text
启动 → 登录 → 看见海报 → 选择电影 → 路径转换成功 → PotPlayer 打开原文件
```

---

## 3. 四项设置与路径选择界面

用户提出的四项配置都应当在图形界面中完成，不要求手改 JSON。

### 3.1 Jellyfin 服务器地址

**字段：** `ServerBaseUrl`

示例：

```text
http://127.0.0.1:8096
http://192.168.1.20:8096
https://jellyfin.example.com
```

控件设计：

- 可编辑文本框；
- 历史地址下拉；
- “测试连接”按钮；
- “打开服务器网页”按钮；
- 自动去除末尾多余 `/`；
- 显示服务器名称和版本。

服务器地址不是 Windows 文件路径，因此不使用文件夹选择器。用户只需填写一次，程序保存。

### 3.2 Jellyfin 服务器媒体根路径

**字段：** `ServerRoot`

示例：

```text
/media/movies
/mnt/nas/电影
D:\Movies
```

这是 Jellyfin Server 看到的路径。若 Jellyfin 运行在 NAS Docker 中，它可能是 Linux 容器路径，Windows 无法通过本地文件选择框浏览。

因此界面应提供三种方式：

1. 手动填写；
2. 从已加载电影的 `Path` 字段自动提取；
3. 点击“选择样本电影”，显示其完整服务器路径并让用户截取根目录。

不要只设计“浏览文件夹”按钮，否则 Docker/Linux 服务端路径无法选择。

### 3.3 Windows 实际媒体根路径

**字段：** `WindowsRoot`

示例：

```text
Z:\Movies
\\NAS\Movies
\\192.168.1.20\Movies
D:\Movies
```

控件设计：

- 文本框，允许直接粘贴 UNC；
- “选择文件夹”按钮；
- “测试访问”按钮；
- 显示是否存在、是否可读；
- 建议优先保存 UNC，不强制依赖映射盘符。

.NET 8 WPF 可以使用 `Microsoft.Win32.OpenFolderDialog`：

```csharp
using Microsoft.Win32;

public static string? SelectFolder(string? initialDirectory = null)
{
    var dialog = new OpenFolderDialog
    {
        Title = "选择 Windows 可访问的电影根目录",
        Multiselect = false,
        InitialDirectory = Directory.Exists(initialDirectory)
            ? initialDirectory
            : null
    };

    return dialog.ShowDialog() == true ? dialog.FolderName : null;
}
```

### 3.4 PotPlayer 程序路径

**字段：** `PotPlayerExecutable`

常见路径：

```text
C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe
```

控件设计：

- 文本框；
- “选择 EXE”按钮；
- “自动查找”按钮；
- “测试启动”按钮；
- 文件过滤器只显示 `.exe`；
- 选择后验证文件名和文件存在性，但不要强制限定安装目录。

```csharp
using Microsoft.Win32;

public static string? SelectPotPlayer()
{
    var dialog = new OpenFileDialog
    {
        Title = "选择 PotPlayer 可执行文件",
        Filter = "PotPlayer (*.exe)|*.exe|所有文件 (*.*)|*.*",
        CheckFileExists = true,
        Multiselect = false
    };

    return dialog.ShowDialog() == true ? dialog.FileName : null;
}
```

### 3.5 不要限制为一组路径

最终设置页面应使用“路径映射表”，支持增加多行：

| 名称 | Jellyfin 服务器根路径 | Windows 根路径 | 启用 |
|---|---|---|---|
| NAS 电影 | `/media/movies` | `\\NAS\Movies` | 是 |
| NAS UHD | `/media/uhd` | `\\NAS\UHD` | 是 |
| 本地硬盘 | `D:\Movies` | `D:\Movies` | 是 |

播放时使用“最长前缀优先”，避免 `/media` 抢先匹配 `/media/movies`。

---

## 4. 推荐技术方案

### 4.1 桌面框架

推荐：

```text
C# + .NET 8 + WPF + MVVM
```

原因：

- Windows 11 原生桌面应用；
- 调用外部 EXE 和访问 UNC 路径简单；
- .NET 8 WPF 自带文件和文件夹选择对话框；
- 可发布为普通 x64 EXE；
- 适合 VS Code + C# Dev Kit；
- 后期可接入 Windows Credential Manager 或 DPAPI。

### 4.2 Jellyfin API 接入方式

两种可选方案：

#### 方案 A：第一版直接使用 `HttpClient`

优点：

- 只实现需要的少量接口；
- 请求和返回容易调试；
- 不受生成 SDK 类名变化影响；
- 更适合先理解登录、媒体库和图片请求。

#### 方案 B：使用官方 `Jellyfin.Sdk`

优点：

- 类型完整；
- 接口覆盖更广；
- 后期实现播放状态、会话和更多数据时更方便。

注意：官方 C# SDK 是 Kiota 生成项目，文档仍标注为完善中。建议 MVP 先用 `HttpClient`，接口稳定后再评估迁移。

### 4.3 MVVM 包

推荐：

```text
CommunityToolkit.Mvvm
Microsoft.Extensions.Hosting
Microsoft.Extensions.Http
Microsoft.Extensions.Configuration.Json
Microsoft.Extensions.Logging.Debug
```

---

## 5. VS Code 开发环境

### 5.1 安装

- .NET 8 SDK x64；
- VS Code；
- C# Dev Kit；
- .NET Install Tool；
- Git；
- 已安装并可运行的 PotPlayer；
- 可访问的 Jellyfin Server。

检查：

```powershell
dotnet --info
dotnet --list-sdks
git --version
```

### 5.2 创建项目

资料包附带 `scripts/create-jellypot.ps1`，也可以手动执行：

```powershell
mkdir JellyPot
cd JellyPot

dotnet new sln -n JellyPot
dotnet new wpf -n JellyPot.App -o src/JellyPot.App --framework net8.0
dotnet new xunit -n JellyPot.Tests -o tests/JellyPot.Tests --framework net8.0

dotnet sln add src/JellyPot.App/JellyPot.App.csproj
dotnet sln add tests/JellyPot.Tests/JellyPot.Tests.csproj
dotnet add tests/JellyPot.Tests/JellyPot.Tests.csproj reference src/JellyPot.App/JellyPot.App.csproj

# 将测试项目的 TargetFramework 改为 net8.0-windows，才能引用 WPF 项目。

dotnet add src/JellyPot.App/JellyPot.App.csproj package CommunityToolkit.Mvvm
dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Hosting
dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Http
dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Configuration.Json
dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Logging.Debug

dotnet build
```

### 5.3 第一版目录结构

先保持一个 WPF 主项目，不要过早拆成很多类库：

```text
JellyPot/
├─ JellyPot.sln
├─ src/
│  └─ JellyPot.App/
│     ├─ App.xaml
│     ├─ App.xaml.cs
│     ├─ Models/
│     │  ├─ AppSettings.cs
│     │  ├─ ServerSettings.cs
│     │  ├─ PlaybackSettings.cs
│     │  ├─ PathMapping.cs
│     │  ├─ JellyfinMovie.cs
│     │  └─ LoginResult.cs
│     ├─ Services/
│     │  ├─ JellyfinApiClient.cs
│     │  ├─ AuthenticationService.cs
│     │  ├─ SettingsService.cs
│     │  ├─ PathMappingService.cs
│     │  ├─ PotPlayerService.cs
│     │  ├─ DialogService.cs
│     │  └─ CredentialService.cs
│     ├─ ViewModels/
│     │  ├─ MainViewModel.cs
│     │  ├─ LoginViewModel.cs
│     │  ├─ LibraryViewModel.cs
│     │  ├─ MovieDetailsViewModel.cs
│     │  └─ SettingsViewModel.cs
│     ├─ Views/
│     │  ├─ MainWindow.xaml
│     │  ├─ LoginView.xaml
│     │  ├─ LibraryView.xaml
│     │  ├─ MovieDetailsView.xaml
│     │  └─ SettingsView.xaml
│     ├─ Configuration/
│     ├─ Converters/
│     ├─ Infrastructure/
│     └─ Assets/
└─ tests/
   └─ JellyPot.Tests/
      ├─ PathMappingServiceTests.cs
      └─ PotPlayerServiceTests.cs
```

---

## 6. 配置模型

```csharp
public sealed class AppSettings
{
    public ServerSettings Server { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
    public List<PathMapping> PathMappings { get; set; } = [];
    public UiSettings Ui { get; set; } = new();
}

public sealed class ServerSettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8096";
    public string ClientName { get; set; } = "JellyPot";
    public string ClientVersion { get; set; } = "0.1.0";
    public string DeviceName { get; set; } = Environment.MachineName;
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string? UserId { get; set; }
    public string? AccessTokenProtected { get; set; }
}

public sealed class PlaybackSettings
{
    public string PotPlayerExecutable { get; set; } = string.Empty;
    public string ArgumentsTemplate { get; set; } = "{path}";
    public bool AskToMarkPlayedAfterExit { get; set; } = true;
    public double MinimumPlayedPercent { get; set; } = 90;
}

public sealed class PathMapping
{
    public string Name { get; set; } = "新映射";
    public bool Enabled { get; set; } = true;
    public string ServerRoot { get; set; } = string.Empty;
    public string WindowsRoot { get; set; } = string.Empty;
}
```

### 6.1 配置文件位置

建议放在：

```text
%LocalAppData%\JellyPot\settings.json
```

不要把用户设置写进程序安装目录，否则可能遇到权限和升级覆盖问题。

### 6.2 敏感信息

- 用户名可以保存；
- 密码不保存；
- Access Token 使用 DPAPI 或 Windows Credential Manager 保存；
- 日志不得输出密码和完整 Token；
- “导出设置”默认不包含 Token。

---

## 7. Jellyfin API 工作流程

Jellyfin Server 自带 Swagger 页面，开发时优先查看当前服务器实际接口：

```text
http://服务器地址:8096/api-docs/swagger/index.html
```

由于 Jellyfin API 和 SDK会随版本变化，代码应把 API 调用集中在 `JellyfinApiClient` 中，不要散落在 ViewModel。

### 7.1 测试服务器

无需登录即可尝试获取公开系统信息：

```http
GET /System/Info/Public
```

用途：

- 判断地址是否可访问；
- 获取服务器名称、版本；
- 在登录前给用户明确反馈。

### 7.2 登录

典型流程：

```http
POST /Users/AuthenticateByName
Content-Type: application/json
Authorization: MediaBrowser Client="JellyPot", Device="Windows 11 Desktop", DeviceId="固定设备ID", Version="0.1.0"

{
  "Username": "用户名",
  "Pw": "密码"
}
```

返回中保存：

- `AccessToken`；
- `User.Id`；
- `ServerId`。

后续请求的 Authorization 头加入 Token。不要每次启动都重新提交密码；优先使用安全保存的 Token，Token 无效时再返回登录界面。

### 7.3 获取媒体库

```http
GET /Users/{userId}/Views
```

筛选电影类型媒体库，用户可选择一个或多个。

### 7.4 获取电影列表

概念请求：

```http
GET /Users/{userId}/Items
    ?ParentId={libraryId}
    &Recursive=true
    &IncludeItemTypes=Movie
    &EnableImages=true
    &EnableUserData=true
    &Fields=Path,Overview,Genres,People,MediaSources,MediaStreams,ProviderIds,DateCreated
    &SortBy=SortName
    &SortOrder=Ascending
    &StartIndex=0
    &Limit=60
```

应使用分页加载，禁止一次性下载上万条完整信息。

### 7.5 海报 URL

概念形式：

```text
{BaseUrl}/Items/{ItemId}/Images/Primary?maxWidth=380&quality=90&tag={PrimaryImageTag}
```

实现要点：

- 列表使用较小图片；
- 详情页再加载背景图和大海报；
- 使用内存和磁盘缓存；
- 图片失败显示默认占位图；
- 滚动时延迟加载，避免同时请求数百张海报。

### 7.6 媒体路径

优先读取：

1. 选定版本的 `MediaSource.Path`；
2. 若不存在，再读取条目的 `Path`；
3. 多版本电影先弹出版本选择窗口。

注意：Jellyfin Server 在 Docker 中返回的通常是容器路径，不是 Windows SMB 路径，必须经过路径映射。

---

## 8. 路径映射算法

### 8.1 基本示例

```text
Jellyfin 路径：/media/movies/Dune Part Two (2024)/movie.mkv
映射规则：   /media/movies  →  \\NAS\Movies
结果：       \\NAS\Movies\Dune Part Two (2024)\movie.mkv
```

### 8.2 规则要求

- 忽略根目录末尾的 `/` 或 `\`；
- 兼容 Linux `/` 和 Windows `\`；
- 多条规则按 `ServerRoot` 长度从长到短匹配；
- 只替换路径开头，不能做全局字符串替换；
- 转换完成后统一为 Windows 分隔符；
- 播放前执行 `File.Exists` 或 `Directory.Exists`；
- 显示命中的映射规则；
- 无匹配规则时禁止静默播放 Jellyfin 流地址。

### 8.3 示例实现

```csharp
public sealed class PathMappingService
{
    public string Resolve(string serverPath, IEnumerable<PathMapping> mappings)
    {
        if (string.IsNullOrWhiteSpace(serverPath))
            throw new ArgumentException("Jellyfin 没有返回媒体路径。", nameof(serverPath));

        string normalizedSource = NormalizeServerPath(serverPath);

        PathMapping? mapping = mappings
            .Where(x => x.Enabled)
            .Where(x => !string.IsNullOrWhiteSpace(x.ServerRoot))
            .OrderByDescending(x => NormalizeServerPath(x.ServerRoot).Length)
            .FirstOrDefault(x => IsPathPrefix(
                normalizedSource,
                NormalizeServerPath(x.ServerRoot)));

        if (mapping is null)
            throw new InvalidOperationException($"没有路径映射可处理：{serverPath}");

        string sourceRoot = NormalizeServerPath(mapping.ServerRoot).TrimEnd('/');
        string relative = normalizedSource[sourceRoot.Length..].TrimStart('/');
        string windowsRoot = mapping.WindowsRoot.TrimEnd('\\', '/');
        string result = string.IsNullOrEmpty(relative)
            ? windowsRoot
            : windowsRoot + "\\" + relative.Replace('/', '\\');

        return result;
    }

    private static string NormalizeServerPath(string value) =>
        value.Trim().Replace('\\', '/').TrimEnd('/');

    private static bool IsPathPrefix(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
}
```

### 8.4 测试用例

必须为路径服务写单元测试：

```text
/media/movies/a.mkv + /media/movies → \\NAS\Movies\a.mkv
/media/uhd/a.mkv 同时存在 /media 和 /media/uhd 时，命中 /media/uhd
D:\Movies\a.mkv → D:\Movies\a.mkv
无映射 → 抛出明确异常
路径包含空格和中文 → 正常
UNC 根路径 → 正常
```

---

## 9. 调用 PotPlayer

### 9.1 基础实现

使用 `ProcessStartInfo.ArgumentList`，避免手工拼接引号导致中文、空格或特殊字符路径失败。

```csharp
using System.Diagnostics;

public sealed class PotPlayerService
{
    public Process Launch(string executablePath, string mediaPath)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("没有找到 PotPlayer。", executablePath);

        bool pathExists = File.Exists(mediaPath) || Directory.Exists(mediaPath);
        if (!pathExists)
            throw new FileNotFoundException("无法访问电影文件或目录。", mediaPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(mediaPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("PotPlayer 启动失败。\n");
    }
}
```

### 9.2 可配置启动参数

建议保留：

```text
ArgumentsTemplate = {path}
```

后期可支持：

```text
/fullscreen {path}
```

不要直接把模板整体写进 `Arguments`。应解析模板，把 `{path}` 单独加入 `ArgumentList`，防止命令注入和转义错误。

### 9.3 播放错误提示

错误窗口至少显示：

- Jellyfin 原始路径；
- 命中的路径映射；
- 转换后的 Windows 路径；
- PotPlayer 路径；
- “在资源管理器中打开根目录”按钮；
- “打开设置”按钮；
- “复制诊断信息”按钮。

这会显著降低用户排查 NAS 权限、盘符断开和路径拼写问题的难度。

---

## 10. 媒体格式处理

### 10.1 第一版优先级

优先稳定支持：

```text
.mkv
.mp4
.m2ts
.ts
.avi
.iso
```

PotPlayer 能否正确播放取决于其自身设置，本项目只负责传递路径。

### 10.2 BDMV

BDMV 可能返回目录、`index.bdmv`、播放列表或其他路径。第一版先做诊断输出，不要假定所有原盘结构相同。

建议解析顺序：

1. 若路径是文件，直接传给 PotPlayer；
2. 若路径是 `BDMV` 目录，尝试 `BDMV\index.bdmv`；
3. 若路径是电影根目录且存在 `BDMV\index.bdmv`，传递该文件；
4. 若无法识别，弹出“选择实际播放文件”窗口；
5. 将用户选择保存为该条目的本地覆盖规则。

### 10.3 多版本

一部电影可能有多个 `MediaSources`：

```text
1080P REMUX
4K HDR REMUX
4K Dolby Vision
导演剪辑版
```

详情页应显示版本列表，默认选择上次播放版本，点击播放前允许切换。

---

## 11. 界面设计

### 11.1 首次启动向导

#### 第一步：服务器

- 服务器地址；
- 测试连接；
- 用户名、密码；
- 登录。

#### 第二步：播放器

- PotPlayer EXE；
- 自动查找；
- 测试启动。

#### 第三步：路径映射

- 从 Jellyfin 选择一部样本电影；
- 显示服务器原始路径；
- 选择 Windows 根目录；
- 实时预览转换结果；
- 测试文件是否存在。

#### 第四步：完成测试

- 显示样本电影海报；
- “使用 PotPlayer 测试播放”；
- 成功后进入主界面。

### 11.2 主界面

```text
┌────────────────────────────────────────────────────┐
│ JellyPot   媒体库▼  搜索框     筛选  排序  设置   │
├────────────────────────────────────────────────────┤
│ 最近添加 / 全部电影 / 未观看 / 收藏 / 4K / HDR    │
├────────────────────────────────────────────────────┤
│ [海报] [海报] [海报] [海报] [海报] [海报]         │
│ 片名   片名   片名   片名   片名   片名           │
│ 年份   4K    HDR    已看   进度   收藏            │
└────────────────────────────────────────────────────┘
```

要求：

- 海报列表虚拟化；
- 分页或增量加载；
- 图片异步加载；
- 键盘方向键、Enter、Esc 可操作；
- 双击海报默认打开详情，不建议直接播放，避免误触；
- 详情页提供明显的“PotPlayer 播放”按钮。

### 11.3 详情页

显示：

- 背景图、海报；
- 中文片名、原名、年份；
- 简介；
- 时长、分辨率、帧率；
- HDR、Dolby Vision；
- 视频编码；
- 音频编码、声道；
- 字幕；
- 版本列表；
- Jellyfin 原始路径；
- Windows 实际路径；
- 收藏、已看；
- PotPlayer 播放。

---

## 12. 播放状态同步

### 12.1 第一版建议

第一版不要尝试精确读取 PotPlayer 时间轴。先提供三种简单模式：

1. 不同步；
2. PotPlayer 关闭后询问“是否标记已观看”；
3. 启动后直接标记已观看（不推荐作为默认）。

默认使用第 2 种。

### 12.2 后续精确同步

若未来能通过稳定的 PotPlayer IPC 获取：

- 当前播放位置；
- 总时长；
- 暂停状态；
- 停止事件；

则可以向 Jellyfin 报告：

```text
播放开始
每 10 秒播放进度
播放停止
```

但 PotPlayer 外部控制接口不像 MPV IPC 那样标准化，必须先做独立技术验证。不要让进度同步阻塞第一版发布。

---

## 13. 性能与缓存

### 13.1 分页

建议：

```text
PageSize = 60
```

滚动到接近底部时加载下一页。

### 13.2 图片缓存

缓存目录：

```text
%LocalAppData%\JellyPot\Cache\Images
```

缓存键建议包含：

```text
ItemId + ImageTag + Width
```

Jellyfin 海报更换后 ImageTag 变化，自然生成新缓存。

### 13.3 HTTP

- 复用 `HttpClient`；
- 使用 `IHttpClientFactory`；
- 设置合理超时；
- 图片请求与数据请求使用不同命名客户端；
- 取消已离开页面的请求；
- 限制并发图片下载数量。

---

## 14. 日志与诊断

日志目录：

```text
%LocalAppData%\JellyPot\Logs
```

记录：

- 应用版本；
- .NET 版本；
- Jellyfin 服务器版本；
- API 状态码；
- 路径映射名称；
- 转换前后路径；
- PotPlayer 启动结果；
- 异常堆栈。

禁止记录：

- 用户密码；
- 完整 Access Token；
- NAS 密码；
- 远程服务器敏感 Cookie。

提供“导出诊断包”，内容包括日志、脱敏配置和版本信息。

---

## 15. 安全边界

1. 只允许从用户配置的 PotPlayer EXE 启动；
2. 只允许播放由 Jellyfin 条目和路径映射共同生成的路径；
3. 不把媒体路径交给 `cmd.exe` 或 PowerShell；
4. 使用 `ProcessStartInfo.ArgumentList`；
5. 路径映射后再次验证最终路径；
6. 局域网 HTTP 可用于初期开发，远程连接必须使用 HTTPS 或 VPN；
7. Token 使用 Windows 加密能力保存；
8. 不自动把 Jellyfin 8096 暴露到公网。

---

## 16. 开源许可证

推荐做法：

- 使用 Jellyfin API 或官方 SDK；
- 自己编写 WPF 界面；
- 不直接复制 Jellyfin Web 大量前端代码和资源。

原因：

- Jellyfin Web 使用 GPL-2.0；
- 官方 C# SDK 使用 MIT；
- 使用 API/SDK 并自行实现 UI，许可证边界更清晰；
- 若复制或修改 GPL 客户端代码并分发，应按 GPL 要求处理源码和许可证。

项目名称和图标也不要让用户误以为是 Jellyfin 官方客户端。可在关于页面写：

```text
JellyPot is an independent client for Jellyfin and is not affiliated with the Jellyfin project.
```

---

## 17. 开发阶段

### 阶段 0：验证环境

- Jellyfin Swagger 可打开；
- PotPlayer 路径可启动；
- Windows 能访问 NAS UNC；
- 选一部 MKV 作为样本。

### 阶段 1：最小 API

- 测试服务器；
- 登录；
- 获取用户媒体库；
- 获取 20 部电影；
- 控制台打印名称、ID、Path。

验收：程序能够获取实际 Jellyfin 路径。

### 阶段 2：路径与播放器

- 设置页面；
- 路径映射表；
- 转换预览；
- 文件存在检查；
- PotPlayer 启动。

验收：点击样本电影能由 PotPlayer 打开 NAS 原文件。

### 阶段 3：海报墙

- 海报卡片；
- 分页；
- 搜索；
- 排序；
- 详情页。

验收：可以日常浏览和播放整个电影库。

### 阶段 4：稳定性

- Token 安全保存；
- 图片缓存；
- 断线重试；
- 诊断日志；
- 路径映射单元测试；
- 发布 x64 包。

### 阶段 5：增强

- 多版本；
- BDMV；
- ISO；
- 收藏和已看状态；
- 关闭后询问标记已看；
- 播放进度技术验证。

---

## 18. 第一批任务清单

可以直接在 GitHub Issues 或 VS Code TODO 中建立：

```text
[ ] 初始化 WPF + MVVM 项目
[ ] 建立 AppSettings 数据模型
[ ] 实现 settings.json 读写
[ ] 实现服务器地址验证
[ ] 实现 Jellyfin 登录
[ ] 安全保存 Access Token
[ ] 获取媒体库列表
[ ] 获取电影分页列表
[ ] 加载海报 URL
[ ] 建立路径映射设置页
[ ] 实现 OpenFolderDialog
[ ] 实现 OpenFileDialog 选择 PotPlayer
[ ] 实现 PathMappingService
[ ] 为路径映射写单元测试
[ ] 实现 PotPlayerService
[ ] 创建样本电影测试流程
[ ] 创建海报墙
[ ] 创建电影详情页
[ ] 添加错误诊断窗口
[ ] 发布 win-x64 测试版
```

---

## 19. 版本规划

### v0.1

- 登录；
- 单一电影库；
- 海报墙；
- 一条路径映射；
- PotPlayer 播放 MKV。

### v0.2

- 多媒体库；
- 多路径映射；
- 搜索、筛选、排序；
- 图片缓存；
- 设置向导。

### v0.3

- 多版本选择；
- ISO、M2TS、BDMV 路径处理；
- 已看和收藏；
- 播放后询问标记已看。

### v1.0

- 稳定安装包；
- 完整日志与诊断；
- 自动迁移配置；
- 大型媒体库性能优化；
- 文档和发布说明。

---

## 20. 关键决定总结

1. **不重写 Jellyfin Server。**
2. **不直接 fork 完整 Jellyfin Desktop。**
3. **使用 Jellyfin API 获取海报和媒体资料。**
4. **使用 WPF 自己实现 Windows 海报墙。**
5. **播放时不请求 Jellyfin 视频流。**
6. **把 Jellyfin 返回路径转换为 Windows 本地或 UNC 路径。**
7. **四项配置全部提供图形化输入与测试。**
8. **服务器路径允许手填或从样本条目提取，不能只依赖文件夹选择框。**
9. **Windows 媒体根目录和 PotPlayer 使用系统选择框。**
10. **第一版先完成 MKV 播放闭环，进度同步和复杂原盘后做。**

---

## 21. 官方参考资料

- Jellyfin Server 源码和本机 Swagger：`/api-docs/swagger/index.html`
- Jellyfin C# SDK：`jellyfin/jellyfin-sdk-csharp`
- Jellyfin Web：`jellyfin/jellyfin-web`
- Jellyfin Kodi Native Mode 文档：用于理解“元数据由 Jellyfin 管理，播放直接访问文件系统”的路径替换模式
- Jellyfin 转码文档：用于区分 Direct Play、Remux、Direct Stream 和 Transcode
- Microsoft WPF Common Dialog 文档：`.NET 8 Microsoft.Win32.OpenFolderDialog`
- Microsoft `ProcessStartInfo` 文档：用于安全启动 PotPlayer
