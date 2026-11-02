<#
.SYNOPSIS
  同梱する git（T1201、D002）を vendor/portable-git に用意する。ビルドはここを出力の git\ に写す（GitKnife.csproj の BundleGit）。
.DESCRIPTION
  - Git for Windows の PortableGit（64-bit）を落とし、リリースノートの SHA-256 と照合してから展開する。
  - git-flow は Git for Windows 2.51.1 で外れたので、最後に入っていた 2.50.1 の PortableGit から usr/bin の git-flow* と gitflow-* だけを足す
    （gitflow-avh。ユーザー判断 2026-10-25）。
  - 出たばかりの版は避け、2 週間以上たった版を選ぶ。版を上げるときは下の表の版と SHA-256 を書き換えて -Force で流し直す。
.EXAMPLE
  pwsh tools/git/fetch-portable-git.ps1
#>
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$Git     = @{ Version = '2.56.0.2'; Tag = 'v2.56.0.windows.2'; Sha256 = '075e158ef8e1f0ab80b347e245405d3eca735c2dc88fd8e032e137d0ca61f61b' }
$GitFlow = @{ Version = '2.50.1';   Tag = 'v2.50.1.windows.1'; Sha256 = 'c45a7dfa2bde34059f6dbd85f49a95d73d5aea29305f51b79595e56e4f323a3d' }

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$vendor = Join-Path $root 'vendor'
$downloads = Join-Path $vendor 'downloads'
$target = Join-Path $vendor 'portable-git'
$stamp = Join-Path $target '.gitknife-bundle'
$stampText = "git $($Git.Version); git-flow from $($GitFlow.Version); licenses; credential.helper=manager; trimmed"

if ((Test-Path $stamp) -and -not $Force -and (Get-Content $stamp -Raw).Trim() -eq $stampText) {
  Write-Host "already prepared: $stampText"
  return
}
New-Item -ItemType Directory -Force $downloads | Out-Null

function Get-PortableGit($info) {
  $name = "PortableGit-$($info.Version)-64-bit.7z.exe"
  $file = Join-Path $downloads $name
  if (-not (Test-Path $file)) {
    $url = "https://github.com/git-for-windows/git/releases/download/$($info.Tag)/$name"
    Write-Host "downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $file
  }
  $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
  if ($hash -ne $info.Sha256) {
    Remove-Item $file
    throw "SHA-256 mismatch for $name (got $hash). Deleted the download."
  }
  return $file
}

# 7-Zip の自己展開形式。-o で展開先、-y で確認なし
function Expand-PortableGit($exe, $dir) {
  if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
  $p = Start-Process -FilePath $exe -ArgumentList "-o`"$dir`"", '-y' -Wait -PassThru -WindowStyle Hidden
  if ($p.ExitCode -ne 0) { throw "extract failed ($exe, exit $($p.ExitCode))" }
}

$gitExe = Get-PortableGit $Git
$flowExe = Get-PortableGit $GitFlow

Expand-PortableGit $gitExe $target

$flowTemp = Join-Path $downloads "flow-$($GitFlow.Version)"
Expand-PortableGit $flowExe $flowTemp
$flowFiles = Get-ChildItem (Join-Path $flowTemp 'usr/bin') | Where-Object { $_.Name -like 'git-flow*' -or $_.Name -like 'gitflow-*' }
if ($flowFiles.Count -eq 0) { throw "git-flow scripts not found in PortableGit $($GitFlow.Version)" }
$flowFiles | Copy-Item -Destination (Join-Path $target 'usr/bin')
Remove-Item -Recurse -Force $flowTemp

# ライセンス文。PortableGit 2.50.1 には別のファイルが無いので、スクリプトの先頭の注記から作る（T1201）
#  - gitflow-avh：git-flow の先頭の BSD 2 条項の注記（"Authors:" から "SUCH DAMAGE." まで）を usr/share/licenses/gitflow-avh/LICENSE に
#  - shFlags：gitflow-shFlags の先頭は "Released under the LGPL"。注記と LGPL 2.1 の全文（同じ PortableGit の libiconv のもの）を usr/share/licenses/shflags/ に
$licenses = Join-Path $target 'usr/share/licenses'
$flowLines = Get-Content (Join-Path $target 'usr/bin/git-flow')
$start = ($flowLines | Select-String -Pattern '^# Authors:' | Select-Object -First 1).LineNumber
$end = ($flowLines | Select-String -Pattern 'SUCH DAMAGE\.' | Select-Object -First 1).LineNumber
if (-not $start -or -not $end) { throw 'license header not found in git-flow' }
New-Item -ItemType Directory -Force (Join-Path $licenses 'gitflow-avh') | Out-Null
$flowLines[($start - 1)..($end - 1)] | ForEach-Object { $_ -replace '^# ?', '' } |
  Set-Content -Path (Join-Path $licenses 'gitflow-avh/LICENSE') -Encoding utf8
$shLines = Get-Content (Join-Path $target 'usr/bin/gitflow-shFlags') -TotalCount 12 | Where-Object { $_ -match 'Copyright|Released under|shFlags' }
New-Item -ItemType Directory -Force (Join-Path $licenses 'shflags') | Out-Null
$shLines | ForEach-Object { $_ -replace '^# ?', '' } | Set-Content -Path (Join-Path $licenses 'shflags/NOTICE') -Encoding utf8
Copy-Item (Join-Path $target 'ucrt64/share/licenses/libiconv/COPYING.LIB') (Join-Path $licenses 'shflags/COPYING.LIB')

# 資格情報ヘルパー：PortableGit の etc/gitconfig は credential.helper = helper-selector（使うたびに「<no helper> / wincred / manager」を選ぶ画面を出す）。
# Git for Windows のインストーラは manager（GCM）なので、同じにする。
# helper-selector のままだと、選んだヘルパーに資格情報が無いと GIT_TERMINAL_PROMPT=0 のため
# "could not read Username for '...': terminal prompts disabled" で失敗する（2026-10-28、http のリモートへの push で起きた）。
$gitconfig = Join-Path $target 'etc/gitconfig'
$text = Get-Content $gitconfig -Raw
if ($text -notmatch '(?m)^\s*helper\s*=\s*helper-selector\s*$') { throw "credential.helper = helper-selector not found in $gitconfig" }
$text = $text -replace '(?m)^(\s*helper\s*=\s*)helper-selector(\s*)$', '${1}manager${2}'
$text = $text.TrimEnd() + "`n[credential `"https://dev.azure.com`"]`n`tuseHttpPath = true`n"
Set-Content -Path $gitconfig -Value $text -NoNewline

# GitKnife が使わず、消しても git・git-flow・git-lfs・GCM・カスタムコマンドの Bash に響かないもののうち、大きいものだけ消す（2026-10-28）。
#  - vim（2,366 ファイル、61 MB）：エディタは GitKnife が -c core.editor / GIT_EDITOR で RI か true を渡す。何かが編集を求めても nano が残る
#  - git の HTML マニュアル（1,156 ファイル、22 MB）：git help を使わない。ほかの doc はライセンス文を含むので残す
#  - gitk / git-gui / Tcl/Tk（約 1,060 ファイル、13 MB）：GitKnife から呼ばない
# 残すもの：Perl（git svn、send-email、利用者の hook）、zoneinfo（git-lfs と libstdc++ が参照）、ucrt64/bin と libexec/git-core の同じファイル（どちらからも呼ばれる）
$trim = @(
  'usr/share/vim', 'usr/bin/vim.exe', 'usr/bin/vimdiff.exe', 'usr/bin/ex.exe', 'usr/bin/rview.exe', 'usr/bin/rvim.exe', 'usr/bin/view.exe', 'usr/bin/vimtutor',
  'ucrt64/share/doc/git-doc',
  'ucrt64/lib/tcl8', 'ucrt64/lib/tcl8.6', 'ucrt64/lib/tk8.6', 'ucrt64/lib/reg1.3', 'ucrt64/lib/thread2.8.13', 'ucrt64/lib/tclConfig.sh', 'ucrt64/lib/tkConfig.sh',
  'ucrt64/share/git-gui', 'ucrt64/bin/gitk', 'ucrt64/bin/tcl86.dll', 'ucrt64/bin/tk86.dll', 'ucrt64/bin/tclsh.exe', 'ucrt64/bin/tclsh86.exe', 'ucrt64/bin/wish.exe', 'ucrt64/bin/wish86.exe',
  'ucrt64/libexec/git-core/git-gui', 'ucrt64/libexec/git-core/git-gui.tcl', 'ucrt64/libexec/git-core/git-gui--askpass', 'ucrt64/libexec/git-core/git-gui--askyesno',
  'ucrt64/libexec/git-core/git-citool', 'ucrt64/libexec/git-core/tcl86.dll', 'ucrt64/libexec/git-core/tk86.dll',
  'cmd/gitk.exe', 'cmd/git-gui.exe'
)
$missing = @()
foreach ($rel in $trim) {
  $p = Join-Path $target $rel
  if (Test-Path $p) { Remove-Item -Recurse -Force $p } else { $missing += $rel }
}
# 版を上げて置き場所が変わったら気づけるように（消し残しは害が無いので止めない）
if ($missing.Count -gt 0) { Write-Warning "not found (layout changed?): $($missing -join ', ')" }

Write-Host "prepared $target ($stampText, git-flow files: $($flowFiles.Count))"
