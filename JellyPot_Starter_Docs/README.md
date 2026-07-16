# JellyPot 开发资料包

JellyPot 是一个面向 Windows 11 的 Jellyfin 海报墙客户端：

- Jellyfin Server 负责媒体库、海报、简介、演员和观看状态；
- JellyPot 负责浏览、搜索、路径转换和调用播放器；
- PotPlayer 直接打开本地硬盘或 NAS 的 SMB/UNC 原文件，不经过 Jellyfin 视频流和转码。

## 从这里开始

1. 阅读 [`docs/JellyPot_开发设计文档.md`](docs/JellyPot_开发设计文档.md)。
2. 安装 .NET 8 SDK、VS Code、C# Dev Kit。
3. 在 PowerShell 中运行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\create-jellypot.ps1
```

4. 用 VS Code 打开生成的 `JellyPot` 文件夹。
5. 参考 [`config/appsettings.example.json`](config/appsettings.example.json) 实现设置页面和路径映射。

> 第一版建议只支持电影和单文件资源（MKV、MP4、M2TS、ISO），先完成“登录—海报墙—路径映射—PotPlayer 播放”的闭环，再增加 BDMV、多版本和进度同步。
