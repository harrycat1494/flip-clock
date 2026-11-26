# Builds the Microsoft Store package of Flip Clock (an .msix file).
#
#   powershell -File store\build-store.ps1 -IdentityName "<Package/Identity/Name>" -Publisher "<Package/Identity/Publisher>" `
#       -PublisherDisplayName "<Package/Properties/PublisherDisplayName>" [-DisplayName "Flip Clock"]
#
# The three identity values are shown in Partner Center under Product management, Product identity.
# Needs the .NET 10 SDK and Microsoft's packaging tools (makeappx.exe, makepri.exe), which come in the
# NuGet package Microsoft.Windows.SDK.BuildTools or with the Windows SDK: pass the folder that holds them
# as -SdkBin, or put them on the PATH. The Store signs the package itself, so nothing is signed here.
param(
  [Parameter(Mandatory)] [string]$IdentityName,
  [Parameter(Mandatory)] [string]$Publisher,
  [Parameter(Mandatory)] [string]$PublisherDisplayName,
  [string]$DisplayName = 'Flip Clock',
  [string]$SdkBin = '',
  [string]$WorkDir = (Join-Path $env:TEMP 'FlipClock-store')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$layout = Join-Path $WorkDir 'layout'
function Tool([string]$name) {
  if ($SdkBin) { $p = Join-Path $SdkBin $name; if (Test-Path $p) { return $p } }
  $c = Get-Command $name -ErrorAction SilentlyContinue
  if ($c) { return $c.Source }
  throw "$name not found. Pass -SdkBin with the folder that holds makeappx.exe and makepri.exe."
}
$makeappx = Tool 'makeappx.exe'; $makepri = Tool 'makepri.exe'

[xml]$proj = Get-Content (Join-Path $root 'FlipClock.csproj')
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$packageVersion = "$version.0" # the fourth part belongs to the Store and has to be 0

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
& dotnet publish (Join-Path $root 'FlipClock.csproj') -c Release -r win-x64 --self-contained true -p:Store=true -p:PublishSingleFile=false `
    "-p:BaseIntermediateOutputPath=$WorkDir\obj\" "-p:BaseOutputPath=$WorkDir\bin\" --nologo -v q -o $layout
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item (Join-Path $PSScriptRoot 'Assets') (Join-Path $layout 'Assets') -Recurse
$esc = { param($s) [Security.SecurityElement]::Escape($s) }
$manifest = (Get-Content (Join-Path $PSScriptRoot 'AppxManifest.template.xml') -Raw).
  Replace('{IdentityName}', (& $esc $IdentityName)).Replace('{Publisher}', (& $esc $Publisher)).
  Replace('{PublisherDisplayName}', (& $esc $PublisherDisplayName)).Replace('{DisplayName}', (& $esc $DisplayName)).
  Replace('{Version}', $packageVersion)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

# the index that lets Windows pick the right icon size
$priConfig = Join-Path $WorkDir 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed' }
& $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri new failed' }

$msix = Join-Path $WorkDir "FlipClock-$version-store.msix"
& $makeappx pack /o /h SHA256 /d $layout /p $msix | Select-Object -Last 2
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed' }
"package: $msix ($([math]::Round((Get-Item $msix).Length / 1MB, 1)) MB), version $packageVersion"
