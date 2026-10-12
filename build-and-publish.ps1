param(
    [Parameter(Mandatory=$true)][string]$AndroidSdk,
    [Parameter(Mandatory=$true)][string]$SigningKeyPath,
    [string]$Dotnet = 'dotnet',
    [string]$Repository = 'DefNotRQ/school-kiosk'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" } }
if (-not $env:KIOSK_SIGN_PASSWORD) { throw 'Set KIOSK_SIGN_PASSWORD privately in the environment.' }
if (git status --porcelain) { throw 'Commit reviewed source before publishing. Source tree must be clean.' }
$commit = git rev-parse HEAD
Check-Exit
& $Dotnet run --project tests -c Release
Check-Exit
& $Dotnet build app/SchoolKiosk.csproj -c Release "-p:AndroidSdkDirectory=$AndroidSdk" --nologo -v:q -clp:ErrorsOnly
Check-Exit
$apksigner = Join-Path $AndroidSdk 'build-tools/36.0.0/apksigner.bat'
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
[xml]$project = Get-Content app/SchoolKiosk.csproj
$version = $project.Project.PropertyGroup.ApplicationDisplayVersion
$tag = "v$version-$(Get-Date -AsUTC -Format 'yyyyMMdd-HHmmss')-$($commit.Substring(0,7))"
$apk = "artifacts/SchoolKiosk-$tag.apk"
& $apksigner sign --ks $SigningKeyPath --ks-pass env:KIOSK_SIGN_PASSWORD --out $apk app/bin/Release/net10.0-android/android-arm64/com.ahura.schoolkiosk.apk
Check-Exit
& $apksigner verify --verbose $apk
Check-Exit
$hash = (Get-FileHash -Algorithm SHA256 $apk).Hash.ToLowerInvariant()
"$hash  $(Split-Path $apk -Leaf)" | Set-Content "artifacts/SHA256SUMS.txt"
git push origin HEAD:main
Check-Exit
gh release create $tag $apk artifacts/SHA256SUMS.txt --repo $Repository --target $commit --title "School Kiosk $version" --notes "Signed ARM64 APK. Exact source commit: $commit. See README and docs/verification.md for security limits and device testing. SHA-256: $hash"
Check-Exit
