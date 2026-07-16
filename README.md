# JellyPot

JellyPot 是一个面向 Windows 11 的 Jellyfin 海报墙客户端。它从 Jellyfin 获取媒体资料和观看状态，将服务端媒体路径解析成 Windows 本地或 UNC 路径，再交给 PotPlayer 直接播放原始文件。

> 当前为可运行的 MVP。项目与 Jellyfin、PotPlayer 官方均无隶属关系。

## 主要功能

- 使用 Jellyfin 账号登录，展示电影、电视和自定义媒体分类
- 深色海报墙，支持搜索、观看状态/收藏筛选和排序
- 查看简介、年份、时长、评分、视频规格和多个媒体版本
- 分辨率角标读取媒体流的真实宽高，详情页显示精确像素尺寸并随播放版本切换
- 通过 PotPlayer 直接播放本地文件或 NAS 上的原始媒体文件
- Windows 盘符路径和 UNC 路径可直接使用，无需额外映射
- Linux、Docker 等服务端路径支持按媒体库根目录映射，最长前缀优先
- 电影、电视和自定义分类支持右键手动添加扫描目录、自动扫描及清空目录
- 自动扫描视频文件，并关联同目录的同名图片或 `poster`、`folder`、`cover`、`movie`、`fanart` 海报
- 支持通过侧栏加号新增自定义分类，并绑定 Jellyfin 媒体库及播放路径
- Jellyfin Access Token 保存到 Windows 凭据管理器，密码不写入本地配置

## 路径如何工作

路径映射不需要为每一部影片手工创建。通常一个媒体库只配置一条根目录映射：

```text
Jellyfin 服务端根路径：/media/movies
Windows 根路径：       \\NAS\Movies

/media/movies/No Time to Die/movie.mkv
                        ↓
\\NAS\Movies\No Time to Die\movie.mkv
```

Jellyfin 如果直接返回 `D:\Movies\...` 或 `\\NAS\Movies\...`，JellyPot 会直接播放，不要求创建映射。若海报与片源位于同一目录，可以右键电影/电视分类选择自动扫描；扫描器会从目录中找到视频，并按以下顺序查找海报：

1. 与视频文件同名的图片
2. `poster.*`
3. `folder.*`
4. `cover.*`
5. `movie.*`
6. `fanart.*`

支持的视频格式包括 MKV、MP4、M2TS、TS、AVI、ISO、WMV 和 MOV；海报支持 JPG、JPEG、PNG 和 BMP。

Jellyfin 媒体的分辨率来自服务端扫描得到的 `MediaStreams.Width/Height`，不是从文件名猜测。本地/NAS 扫描会优先使用可用的 `ffprobe.exe`，并回退到 Windows 媒体文件属性；如果两者都无法读取，角标会明确显示“未知”。可以通过环境变量 `JELLYPOT_FFPROBE` 指定 ffprobe 路径，也可以将 `ffprobe.exe` 放在 JellyPot 程序目录。

## 环境要求

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 可访问的 Jellyfin Server
- PotPlayer（播放原始媒体文件时需要）

## 构建与运行

在仓库根目录执行：

```powershell
dotnet build JellyPot/JellyPot.sln
dotnet run --project JellyPot/src/JellyPot.App/JellyPot.App.csproj
```

生成 Release 构建：

```powershell
dotnet publish JellyPot/src/JellyPot.App/JellyPot.App.csproj -c Release
```

## 首次使用

1. 输入 Jellyfin 服务地址，例如 `http://localhost:8096`，测试连接后登录。
2. 在“设置与路径”中选择或自动查找 PotPlayer 可执行文件。
3. 如果 Jellyfin 返回 Linux/Docker 路径，为每个媒体库添加一条根目录映射并保存。
4. 如果片源本身已经是 Windows/UNC 路径，可跳过路径映射。
5. 右键电影或电视分类，可以手动添加扫描目录或执行自动扫描。
6. 点击海报进入详情页，选择媒体版本后使用 PotPlayer 播放。

## 测试

项目包含路径解析、UNC 直连、同目录海报扫描以及 WPF 电影/电视/详情页渲染回归测试：

```powershell
dotnet run --project JellyPot/tests/JellyPot.Tests/JellyPot.Tests.csproj -c Release
```

## 项目结构

```text
.
├─ JellyPot/
│  ├─ src/JellyPot.App/          WPF 客户端
│  ├─ tests/JellyPot.Tests/      无第三方依赖的回归测试
│  └─ JellyPot.sln
└─ JellyPot_Starter_Docs/
   ├─ docs/                      产品与开发设计文档
   ├─ config/                    配置示例
   └─ scripts/                   启动和路径检查脚本
```

更完整的需求、交互和架构说明见 [JellyPot 开发设计文档](JellyPot_Starter_Docs/docs/JellyPot_开发设计文档.md)。

## 当前范围

当前版本聚焦海报浏览、根路径映射和单个原始媒体文件播放。精确播放进度同步、电视剧连续播放以及完整蓝光导航尚未包含在本版本中。
