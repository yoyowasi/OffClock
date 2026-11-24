$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$exe = Join-Path $PSScriptRoot 'VerifyClock.exe'
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$compilerTemp = Join-Path $PSScriptRoot 'compiler-temp'
New-Item -ItemType Directory -Path $compilerTemp -Force | Out-Null
$testArguments = @(
    '/nologo', '/codepage:65001', '/warnaserror+', '/main:OffClock.VerifyClock', "/out:$exe",
    "/resource:$PSScriptRoot\ReleaseNotes.md,OffClock.ReleaseNotes",
    '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', "/r:$wpf\WindowsBase.dll",
    '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
    "/r:$wpf\UIAutomationTypes.dll", "/r:$wpf\UIAutomationClient.dll",
    "$PSScriptRoot\VerifyClock.cs", "$PSScriptRoot\App.cs", "$PSScriptRoot\Clock.cs", "$PSScriptRoot\Theme.cs", "$PSScriptRoot\Taskbar.cs",
    "$PSScriptRoot\ReleaseUpdate.cs"
)
try {
    $env:TEMP = $env:TMP = $compilerTemp
    & (Join-Path $framework 'csc.exe') @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'The test build failed.' }
    & $exe $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
