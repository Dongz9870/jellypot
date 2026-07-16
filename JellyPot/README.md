# JellyPot

JellyPot 是面向 Windows 11 的 Jellyfin 海报墙客户端。它从 Jellyfin 获取电影资料与观看状态，把服务端媒体路径转换为 Windows 本地/UNC 路径，再由 PotPlayer 直接打开原文件。

## 已实现的 MVP

- Jellyfin 服务器连接测试、用户名/密码登录；
- Access Token 存入 Windows 凭据管理器，密码不落盘；
- 自动恢复登录，获取电影/电视媒体库和分页海报列表；
- 深色海报墙、搜索、观看状态/收藏筛选、排序；
- 内置电影和电视入口，可通过侧栏加号添加、删除自定义分类并绑定 Jellyfin 片源库；
- 自定义分类可同时创建 Jellyfin 服务端路径到 Windows/UNC 的播放路径映射；
- Jellyfin 返回 Windows/UNC 文件路径时直接播放，不要求额外映射；
- 右击电影、电视或自定义分类可手动添加扫描目录、自动扫描或清除目录；扫描会关联同名图片、`poster.jpg`、`folder.jpg` 和 `cover.jpg`；
- 电影详情、媒体版本选择、服务器路径和实际路径诊断；
- 多条路径映射、最长前缀优先、实时转换预览；
- PotPlayer 选择、自动查找、测试启动和安全播放；
- 本地设置写入 `%LocalAppData%\JellyPot\settings.json`；
- 无服务器也能从登录页进入“界面设计预览”。

第一版聚焦电影及单文件资源；精确进度同步、电视剧连续播放和完整蓝光导航暂不在当前范围。

## 开发与运行

要求：Windows 11 x64、.NET 8 SDK、可访问的 Jellyfin Server、PotPlayer。

```powershell
cd JellyPot
dotnet build JellyPot.sln
dotnet run --project src/JellyPot.App/JellyPot.App.csproj
```

运行零第三方依赖的路径映射测试：

```powershell
dotnet run --project tests/JellyPot.Tests/JellyPot.Tests.csproj
```

## 首次使用

1. 填写 Jellyfin 地址并测试连接；
2. 登录后打开“设置与路径”；
3. 选择 PotPlayer 可执行文件；
4. 配置 Jellyfin 服务端根路径到 Windows 根路径的映射；
5. 使用样本路径确认转换结果，保存后从详情页播放。

JellyPot 是独立的 Jellyfin 客户端，与 Jellyfin 项目没有隶属关系。
