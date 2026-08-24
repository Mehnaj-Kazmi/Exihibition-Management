<#
.SYNOPSIS
    Generates the Android and iOS platform folders for the Exhibition Companion
    app and applies the configuration it needs.

.DESCRIPTION
    The Dart source, pubspec and platform configuration are all under source
    control. The android/ and ios/ folders are not, because they are thousands
    of lines of generated Gradle and Xcode project files that Flutter recreates
    from the SDK you actually have — checking in the ones from a different SDK
    version is how a project stops building on somebody else's machine.

    This script fills them in:

      1. flutter create, for the Android and iOS scaffolding
      2. the camera permission and its usage strings, which the scanner needs
         and which neither store will accept the app without
      3. flutter pub get

    Safe to run again. flutter create does not overwrite lib/, and the patches
    below check before inserting.

.EXAMPLE
    ./setup.ps1
    ./setup.ps1 -BaseUrl https://exhibition.smatech.example
#>
[CmdletBinding()]
param(
    # Baked in as the default server address. Can still be changed in the app.
    [string]$BaseUrl = ''
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'exhibition_companion'

if (-not (Test-Path $project)) {
    throw "Cannot find the Flutter project at $project"
}

Write-Host 'Checking for Flutter...' -ForegroundColor Cyan
$flutter = Get-Command flutter -ErrorAction SilentlyContinue
if (-not $flutter) {
    Write-Host ''
    Write-Host 'Flutter is not on the PATH.' -ForegroundColor Red
    Write-Host 'Install it from https://docs.flutter.dev/get-started/install/windows'
    Write-Host 'then run this script again.'
    exit 1
}

& flutter --version

Push-Location $project
try {
    Write-Host ''
    Write-Host 'Generating Android and iOS platform folders...' -ForegroundColor Cyan

    # --platforms limits this to the two we ship. The org becomes the bundle id
    # prefix (com.smatech.exhibitionCompanion) and cannot be changed later
    # without the stores treating it as a different app.
    & flutter create --platforms=android,ios --org com.smatech --project-name exhibition_companion .
    if ($LASTEXITCODE -ne 0) { throw 'flutter create failed.' }

    # --- Android: camera permission ---------------------------------------
    $manifest = Join-Path $project 'android/app/src/main/AndroidManifest.xml'
    if (Test-Path $manifest) {
        $content = Get-Content $manifest -Raw

        if ($content -notmatch 'android\.permission\.CAMERA') {
            $permissions = @'
    <!-- The e-catalogue scanner reads the QR code printed on each stand. -->
    <uses-permission android:name="android.permission.CAMERA" />
    <uses-permission android:name="android.permission.INTERNET" />

    <!-- Declared not-required so the app still installs on the handful of
         devices without a camera; the scanner screen explains itself there. -->
    <uses-feature android:name="android.hardware.camera" android:required="false" />

'@
            $content = $content -replace '(?m)^(\s*)<application', ($permissions + '$1<application')
            Set-Content $manifest -Value $content -Encoding utf8
            Write-Host '  Android: camera permission added.' -ForegroundColor Green
        }
        else {
            Write-Host '  Android: camera permission already present.' -ForegroundColor DarkGray
        }
    }

    # --- iOS: usage strings ------------------------------------------------
    # App Store review rejects a build that opens the camera without one, and
    # the prompt the visitor sees is this exact sentence.
    $plist = Join-Path $project 'ios/Runner/Info.plist'
    if (Test-Path $plist) {
        $content = Get-Content $plist -Raw

        if ($content -notmatch 'NSCameraUsageDescription') {
            $keys = @'
	<key>NSCameraUsageDescription</key>
	<string>Used to scan the QR code on an exhibitor's stand so their e-catalogue can be added to your list.</string>
'@
            $content = $content -replace '(?s)(</dict>\s*</plist>\s*)$', ($keys + "`n</dict>`n</plist>`n")
            Set-Content $plist -Value $content -Encoding utf8
            Write-Host '  iOS: camera usage description added.' -ForegroundColor Green
        }
        else {
            Write-Host '  iOS: camera usage description already present.' -ForegroundColor DarkGray
        }
    }

    Write-Host ''
    # --- Android: the name under the icon ----------------------------------
    # flutter create writes the project name here, so the home screen would
    # read "exhibition_companion". Regenerating android/ puts it back, which is
    # why this is applied every run rather than fixed once by hand.
    if (Test-Path $manifest) {
        $content = Get-Content $manifest -Raw
        if ($content -match 'android:label="exhibition_companion"') {
            $content = $content -replace 'android:label="exhibition_companion"', 'android:label="Exhibition Companion"'
            Set-Content $manifest -Value $content -Encoding utf8
            Write-Host '  Android: app name set to Exhibition Companion.' -ForegroundColor Green
        }
        else {
            Write-Host '  Android: app name already set.' -ForegroundColor DarkGray
        }
    }

    # --- the template test flutter create leaves behind --------------------
    # It tests the default counter app, which this project does not have, so it
    # fails the moment anyone runs flutter test.
    $templateTest = Join-Path $project 'test/widget_test.dart'
    if ((Test-Path $templateTest) -and (Select-String -Path $templateTest -Pattern 'Counter increments smoke test' -Quiet)) {
        Remove-Item $templateTest
        Write-Host '  Removed the generated counter-app test.' -ForegroundColor Green
    }

    Write-Host 'Fetching packages...' -ForegroundColor Cyan
    & flutter pub get
    if ($LASTEXITCODE -ne 0) { throw 'flutter pub get failed.' }

    Write-Host ''
    Write-Host 'Ready.' -ForegroundColor Green
    Write-Host ''

    if ($BaseUrl) {
        Write-Host "Run against $BaseUrl with:"
        Write-Host "  flutter run --dart-define=EXB_BASE_URL=$BaseUrl"
    }
    else {
        Write-Host 'Run it with:'
        Write-Host '  flutter run --dart-define=EXB_BASE_URL=http://YOUR-SERVER:5080'
        Write-Host ''
        Write-Host 'On the Android emulator the host machine is 10.0.2.2, which is the default.'
    }
}
finally {
    Pop-Location
}
