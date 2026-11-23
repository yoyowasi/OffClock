param([string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'outputs\OffClock.exe'))

$ErrorActionPreference = 'Stop'
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path $OutputPath -Parent) -Force | Out-Null
Add-Type -AssemblyName System.Drawing

$iconPath = Join-Path $PSScriptRoot 'OffClock.ico'
$bitmap = [Drawing.Bitmap]::new(32, 32)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$background = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(26, 32, 43))
$stroke = [Drawing.Pen]::new([Drawing.Color]::FromArgb(123, 232, 184), 2.6)
$png = [IO.MemoryStream]::new()
try {
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.FillEllipse($background, 0, 0, 32, 32)
    $graphics.DrawEllipse($stroke, 6, 6, 20, 20)
    $stroke.StartCap = $stroke.EndCap = 'Round'
    $graphics.DrawLine($stroke, 16, 11, 16, 16)
    $graphics.DrawLine($stroke, 16, 16, 20, 18)
    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)

    $writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]1)
        $writer.Write([byte[]]@(32, 32, 0, 0))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$png.Length)
        $writer.Write([uint32]22)
        $writer.Write($png.ToArray())
    } finally { $writer.Dispose() }
} finally {
    $png.Dispose()
    $stroke.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$arguments = @(
    '/nologo', '/target:winexe', '/codepage:65001', '/optimize+', '/warnaserror+',
    "/out:$OutputPath", "/win32icon:$iconPath", "/win32manifest:$PSScriptRoot\OffClock.manifest",
    "/resource:$PSScriptRoot\ReleaseNotes.md,OffClock.ReleaseNotes",
    '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
    "/r:$wpf\WindowsBase.dll", "/r:$wpf\UIAutomationTypes.dll", "/r:$wpf\UIAutomationClient.dll",
    "$PSScriptRoot\Clock.cs", "$PSScriptRoot\Taskbar.cs", "$PSScriptRoot\App.cs", "$PSScriptRoot\ReleaseUpdate.cs"
)
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$compilerTemp = Join-Path $PSScriptRoot 'compiler-temp'
New-Item -ItemType Directory -Force -Path $compilerTemp | Out-Null
try {
    $env:TEMP = $env:TMP = $compilerTemp
    & (Join-Path $framework 'csc.exe') @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
Get-Item -LiteralPath $OutputPath | Select-Object Name, Length, FullName
