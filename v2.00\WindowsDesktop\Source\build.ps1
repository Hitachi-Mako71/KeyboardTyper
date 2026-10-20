$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 compiler not found.' }
$appDirectory = Split-Path -Parent $PSScriptRoot
$outputFile = Join-Path $appDirectory 'WindowsKeyboardTyper.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$outputFile" (Join-Path $PSScriptRoot 'NativeKeyboard.cs') (Join-Path $PSScriptRoot 'Program.cs') (Join-Path $PSScriptRoot 'AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $outputFile
