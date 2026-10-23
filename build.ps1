param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework x64 compiler was not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$target = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'Rasklad.exe'
$wpf = Join-Path (Split-Path $compiler) 'WPF'
$arguments = @('/nologo', '/optimize+', '/target:winexe', '/platform:x64',
    "/win32manifest:$PSScriptRoot\src\Rasklad.manifest", "/win32icon:$PSScriptRoot\assets\Rasklad.ico",
    "/resource:$PSScriptRoot\assets\Rasklad.ico,App.Icon", "/resource:$PSScriptRoot\assets\Rasklad-tray.ico,Tray.Icon",
    "/out:$target", '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:Accessibility.dll',
    "/reference:$wpf\WindowsBase.dll", "/reference:$wpf\UIAutomationClient.dll", "/reference:$wpf\UIAutomationTypes.dll",
    "/resource:$PSScriptRoot\dictionaries\FOLDOC.txt,IT.FOLDOC", "/resource:$PSScriptRoot\dictionaries\ru_terms.tsv,IT.Russian",
    "/resource:$PSScriptRoot\dictionaries\NOTICE.txt,IT.Notice", "/resource:$PSScriptRoot\dictionaries\Rasklad-LICENSE.txt,IT.MIT")
foreach ($name in @('RaskladWindows', 'ITDictionary', 'RuntimeChecks', 'RuntimeSupport', 'ReplacementRules', 'HintDesign', 'TrayMenu', 'AppBranding', 'UiDpiChecks', 'CopilotChecks', 'FnKeyChecks', 'PunctuationChecks', 'StartupRegistration', 'UiText')) {
    $arguments += Join-Path $PSScriptRoot "src\$name.cs"
}
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed: $LASTEXITCODE" }
Write-Output $target
