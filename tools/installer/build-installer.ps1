<#
.SYNOPSIS
  GitKnife のインストーラ（T1201）を作る → .work/installer/GitKnife-<版>-setup.exe
.DESCRIPTION
  1. 同梱の git を用意する（tools/git/fetch-portable-git.ps1。用意済みなら何もしない）
  2. self-contained で publish する（win-x64、Release → .work/publish）
  3. Inno Setup 6 の ISCC で installer/GitKnife.iss をコンパイルする（版は Directory.Build.props の <Version>）
  インストーラは配布先によらず同じもの。配布先（更新とバグ報告の送り先）はダウンロードした場所で決まる（GitKnife.iss の install-source.txt）
.EXAMPLE
  pwsh tools/installer/build-installer.ps1
#>
param([string]$Iscc)
$ErrorActionPreference = 'Stop'

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$publish = Join-Path $root '.work/publish'
$output = Join-Path $root '.work/installer'
$gitDir = Join-Path $root 'vendor/portable-git'

if (-not $Iscc) {
  $Iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
  ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw 'ISCC.exe (Inno Setup 6) not found. Pass -Iscc <path>.' }

& pwsh -NoProfile -File (Join-Path $root 'tools/git/fetch-portable-git.ps1')
if ($LASTEXITCODE -ne 0) { throw 'fetch-portable-git failed' }

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root 'src/GitKnife') -c Release -r win-x64 --self-contained -o $publish -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

& $Iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/DGitDir=$gitDir" "/DOutputDir=$output" (Join-Path $root 'installer/GitKnife.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
$setup = Join-Path $output "GitKnife-$version-setup.exe"
# 更新（Updater.cs）が照合する。リリースにはインストーラと一緒にこの .sha256 も添付する
$hash = (Get-FileHash -Algorithm SHA256 $setup).Hash.ToLowerInvariant()
Set-Content -Path "$setup.sha256" -Value "$hash  $(Split-Path $setup -Leaf)" -NoNewline
Write-Host "installer: $setup"
Write-Host "sha256:    $hash"
