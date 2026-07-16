$ErrorActionPreference = "Stop"

$Root = Join-Path $PSScriptRoot "..\JellyPot"
$Root = [System.IO.Path]::GetFullPath($Root)

if (Test-Path $Root) {
    throw "目标目录已经存在：$Root。请先重命名或删除后再运行。"
}

New-Item -ItemType Directory -Path $Root | Out-Null
Push-Location $Root
try {
    dotnet new sln -n JellyPot
    dotnet new wpf -n JellyPot.App -o src/JellyPot.App --framework net8.0
    dotnet new xunit -n JellyPot.Tests -o tests/JellyPot.Tests --framework net8.0

    $TestProject = "tests/JellyPot.Tests/JellyPot.Tests.csproj"
    (Get-Content $TestProject -Raw).Replace("<TargetFramework>net8.0</TargetFramework>", "<TargetFramework>net8.0-windows</TargetFramework>") | Set-Content $TestProject -Encoding UTF8

    dotnet sln JellyPot.sln add src/JellyPot.App/JellyPot.App.csproj
    dotnet sln JellyPot.sln add tests/JellyPot.Tests/JellyPot.Tests.csproj
    dotnet add tests/JellyPot.Tests/JellyPot.Tests.csproj reference src/JellyPot.App/JellyPot.App.csproj

    dotnet add src/JellyPot.App/JellyPot.App.csproj package CommunityToolkit.Mvvm
    dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Hosting
    dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Http
    dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Configuration.Json
    dotnet add src/JellyPot.App/JellyPot.App.csproj package Microsoft.Extensions.Logging.Debug

    $Folders = @(
        "src/JellyPot.App/Models",
        "src/JellyPot.App/Services",
        "src/JellyPot.App/ViewModels",
        "src/JellyPot.App/Views",
        "src/JellyPot.App/Configuration",
        "src/JellyPot.App/Converters",
        "src/JellyPot.App/Assets",
        "src/JellyPot.App/Infrastructure"
    )
    foreach ($Folder in $Folders) {
        New-Item -ItemType Directory -Path $Folder -Force | Out-Null
    }

    dotnet build JellyPot.sln

    Write-Host ""
    Write-Host "项目已生成：$Root" -ForegroundColor Green
    Write-Host "在 VS Code 中打开该目录，然后按开发设计文档逐步实现。"
}
finally {
    Pop-Location
}
