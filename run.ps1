param([switch]$Configure, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$ProjectRoot = $PSScriptRoot
$LocalFolder = Join-Path $ProjectRoot '.local'
$BuildFolder = Join-Path $ProjectRoot 'bin'
$ExpectedExeHash = '44BF22C71D84B54BDF546AA8FD9CE572AFAA0842B5383E70ED08D6AF769A16B8'

function Select-AppFolder {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = '请选择知云安装文件夹（里面应包含 ZhiYunTranslator.exe）'
    $dialog.ShowNewFolderButton = $false
    try {
        if ($dialog.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) { return $dialog.SelectedPath }
        return $null
    } finally { $dialog.Dispose() }
}

function Get-AppFolder([string]$Path) {
    $folder = [IO.Path]::GetFullPath($Path)
    $exe = Join-Path $folder 'ZhiYunTranslator.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw '目录中找不到 ZhiYunTranslator.exe。' }
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $ExpectedExeHash) { throw '主程序版本不匹配，当前仅支持 README 列出的 V8.9.04 EXE。' }
    return $folder
}

function Build-Bridge {
    $harmony = Join-Path $ProjectRoot '0Harmony.dll'
    if (-not (Test-Path -LiteralPath $harmony -PathType Leaf)) { throw '缺少随附的 0Harmony.dll，请重新下载完整项目。' }
    if ((Get-FileHash -LiteralPath $harmony -Algorithm SHA256).Hash -ne '4C06BB6D9D75785F9B64FD551FC75D54AF9D5A5458A43CFB5BF52A5A22B24E6E') { throw '随附的 Harmony 文件校验失败，请重新下载完整项目。' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到 .NET Framework C# 编译器。' }
    $null = New-Item -ItemType Directory -Path $BuildFolder -Force
    & $compiler /nologo /target:library /optimize+ /langversion:5 /codepage:65001 "/out:$BuildFolder\OwnApiBridge.dll" "/reference:$harmony" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $ProjectRoot 'Bridge.cs')
    if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
    Copy-Item -LiteralPath $harmony -Destination (Join-Path $BuildFolder '0Harmony.dll') -Force
}

if ($BuildOnly) { Build-Bridge; return }
$pathFile = Join-Path $LocalFolder 'app-path.txt'
$choosePath = $Configure -or -not (Test-Path -LiteralPath $pathFile)
if ($choosePath) {
    $selected = Select-AppFolder
    if ([string]::IsNullOrWhiteSpace($selected)) { Write-Output '已取消，未更改配置。'; return }
    $appFolder = Get-AppFolder $selected
} else {
    $appFolder = Get-AppFolder ([IO.File]::ReadAllText($pathFile, [Text.Encoding]::UTF8).Trim())
}
if (-not $Configure -and (Get-Process -Name ZhiYunTranslator -ErrorAction SilentlyContinue)) { throw '请先退出已打开的知云翻译。' }
if ($choosePath -or -not (Test-Path -LiteralPath (Join-Path $BuildFolder 'OwnApiBridge.dll')) -or -not (Test-Path -LiteralPath (Join-Path $BuildFolder '0Harmony.dll'))) { Build-Bridge }
$null = New-Item -ItemType Directory -Path $LocalFolder -Force
if (-not (Test-Path -LiteralPath (Join-Path $LocalFolder 'settings.json'))) {
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'settings.example.json') -Destination (Join-Path $LocalFolder 'settings.json')
}
if ($choosePath) { [IO.File]::WriteAllText($pathFile, $appFolder, (New-Object Text.UTF8Encoding($false))) }
if ($Configure) { Write-Output '安装路径已保存。退出知云后双击 start.cmd 启动。'; return }
foreach ($name in @('OwnApiBridge.dll', '0Harmony.dll')) {
    Copy-Item -LiteralPath (Join-Path $BuildFolder $name) -Destination (Join-Path $appFolder $name) -Force
}
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = Join-Path $appFolder 'ZhiYunTranslator.exe'
$startInfo.WorkingDirectory = $appFolder
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables['APPDOMAIN_MANAGER_ASM'] = 'OwnApiBridge'
$startInfo.EnvironmentVariables['APPDOMAIN_MANAGER_TYPE'] = 'OwnApi.BridgeDomainManager'
$startInfo.EnvironmentVariables['OWNAPI_HOME'] = $LocalFolder
$null = [Diagnostics.Process]::Start($startInfo)
