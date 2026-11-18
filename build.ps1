$ErrorActionPreference = 'Stop'

$compiler = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { [IO.File]::Exists($_) } | Select-Object -First 1

if (!$compiler) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

$scratch = Join-Path $PSScriptRoot ('.build-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$iconBuilder = Join-Path $scratch 'BuildIcon.exe'
$icon = Join-Path $scratch 'CodexResumeTimer.ico'
$stagedExe = Join-Path $scratch 'CodexResumeTimer.exe'
$previousExe = Join-Path $scratch 'Previous.exe'
$output = Join-Path $PSScriptRoot 'CodexResumeTimer.exe'

try {
    & $compiler /nologo /optimize+ /codepage:65001 "/out:$iconBuilder" (Join-Path $PSScriptRoot 'BuildIcon.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The icon builder could not be compiled.' }
    & $iconBuilder $icon
    if ($LASTEXITCODE -ne 0) { throw 'The icon could not be generated.' }

    $license = Join-Path $PSScriptRoot 'LICENSE'
    $compilerArgs = @(
        '/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
        '/r:System.Web.dll', '/r:System.Web.Extensions.dll',
        "/win32icon:$icon", "/resource:$icon,CodexResumeTimer.ico", "/resource:$license,LICENSE", "/out:$stagedExe",
        (Join-Path $PSScriptRoot 'CodexResumeTimer.cs'),
        (Join-Path $PSScriptRoot 'QueueWindow.cs')
    )
    & $compiler @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'The application could not be compiled.' }

    if ([IO.File]::Exists($output)) {
        [IO.File]::Replace($stagedExe, $output, $previousExe)
    } else {
        [IO.File]::Move($stagedExe, $output)
    }
    Write-Output $output
} finally {
    foreach ($file in @($iconBuilder, $icon, $stagedExe, $previousExe)) {
        if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
    }
    [IO.Directory]::Delete($scratch, $false)
}
