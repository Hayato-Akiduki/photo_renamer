# アプリをビルドしてインストーラーを作る
# 使い方: PowerShell で  .\build.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet build PhotoRenamer\PhotoRenamer.csproj -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 が見つかりません (winget install JRSoftware.InnoSetup)" }

& $iscc installer\PhotoRenamer.iss
if ($LASTEXITCODE -ne 0) { throw "インストーラーの作成に失敗しました" }

Get-ChildItem dist-installer\*.exe | Select-Object Name, @{n="KB"; e={[int]($_.Length / 1KB)}}
