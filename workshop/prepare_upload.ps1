<#
.SYNOPSIS
    Prepares the Steam Workshop upload folder for TG3 ModLoader.

.DESCRIPTION
    Builds the program and creates workshop\upload\ in the layout of the official Mod Launcher's
    "Create new mod" (without adding anything to the launcher's mod list):

        content\                what subscribers download: TG3ModLoader.exe, TG3ModLoader.exe.config,
                                TG3ModLoader-README.txt, TG3ModLoader-LICENSE.txt
        preview.jpg             Workshop preview image (square, below the 1 MB limit)
        Guild3ModUploader.exe   the game's official uploader, with steam_api.dll and steam_appid.txt
        publish_mod.bat         first upload - creates the Workshop item (only visible to you)
        update_mod.bat          later uploads - written by the uploader after the first upload; this
                                script then keeps the item id and adds a change note

    Run it again for every new version, then double-click update_mod.bat.

.PARAMETER GameDir
    The Guild 3 game folder. Default: found through Steam.

.PARAMETER ChangeNote
    Text for the "Change Notes" tab of the Workshop item (updates only).
    Default: "Version <x.y.z>" and a link to the change log.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File workshop\prepare_upload.ps1
#>
param([string]$GameDir, [string]$ChangeNote)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $PSScriptRoot 'upload'
$content = Join-Path $out 'content'
$title = 'TG3 ModLoader - Play multiple mods together'
$shortDescription = 'Combines several gameplay mods for The Guild 3 into one, so that they work together.'

[xml]$csproj = Get-Content (Join-Path $root 'src\TG3ModLoader.csproj')
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "TG3 ModLoader $version"

# --- 1. Build -----------------------------------------------------------------------------------
& dotnet build (Join-Path $root 'src') -c Release -o (Join-Path $root 'build') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# --- 2. Game folder (for the official uploader) -------------------------------------------------
function Find-GameDir {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $libraries += $m.Groups[1].Value.Replace('\\', '\')
        }
    }
    foreach ($lib in $libraries) {
        $dir = Join-Path $lib 'steamapps\common\The Guild 3'
        if (Test-Path (Join-Path $dir 'Modding\Guild3ModUploader.exe')) { return $dir }
    }
    return $null
}
if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'Modding\Guild3ModUploader.exe'))) {
    throw 'The Guild 3 was not found - pass the game folder with -GameDir "<...\steamapps\common\The Guild 3>".'
}
$modding = Join-Path $GameDir 'Modding'

# --- 3. Content (exactly what subscribers get) --------------------------------------------------
New-Item -ItemType Directory -Force $out | Out-Null
if (Test-Path $content) { Remove-Item $content -Recurse -Force }
New-Item -ItemType Directory -Force $content | Out-Null
Copy-Item (Join-Path $root 'build\TG3ModLoader.exe') $content
Copy-Item (Join-Path $root 'build\TG3ModLoader.exe.config') $content
Copy-Item (Join-Path $root 'README.txt') (Join-Path $content 'TG3ModLoader-README.txt')
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $content 'TG3ModLoader-LICENSE.txt')

# --- 4. Preview image: square, at most 640 px, JPEG below 1 MB ------------------------------------
Add-Type -AssemblyName System.Drawing
$source = Join-Path $root 'Workshop-Icon.png'
if (-not (Test-Path $source)) { $source = Join-Path $root 'assets\icon.png' }
$image = [System.Drawing.Image]::FromFile($source)
try {
    $side = [Math]::Min($image.Width, $image.Height)
    $size = [Math]::Min(640, $side)
    $crop = New-Object System.Drawing.Rectangle ([int](($image.Width - $side) / 2)), ([int](($image.Height - $side) / 2)), $side, $side
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Black)
    $g.DrawImage($image, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
} finally { $image.Dispose() }
$jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$preview = Join-Path $out 'preview.jpg'
foreach ($quality in 92, 85, 75, 65) {
    $params = New-Object System.Drawing.Imaging.EncoderParameters 1
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), ([long]$quality)
    $bitmap.Save($preview, $jpeg, $params)
    if ((Get-Item $preview).Length -lt 1MB) { break }
}
$bitmap.Dispose()
Write-Host ("Preview: {0} ({1:N0} KB, from {2})" -f $preview, ((Get-Item $preview).Length / 1KB), (Split-Path $source -Leaf))

# --- 5. Official uploader -------------------------------------------------------------------------
Copy-Item (Join-Path $modding 'Guild3ModUploader.exe') $out -Force
Copy-Item (Join-Path $modding 'steam_api.dll') $out -Force
Set-Content (Join-Path $out 'steam_appid.txt') '311260' -NoNewline -Encoding Ascii

# --- 6. publish_mod.bat / update_mod.bat ----------------------------------------------------------
$update = Join-Path $out 'update_mod.bat'
$publish = Join-Path $out 'publish_mod.bat'
$itemId = $null
if (Test-Path $update) {
    $m = [regex]::Match((Get-Content $update -Raw), '--mod\s+"?(\d+)')
    if ($m.Success) { $itemId = $m.Groups[1].Value }
}
if ($itemId) {
    if (-not $ChangeNote) { $ChangeNote = "Version $version - changes: https://github.com/Kleator/TG3ModLoader/blob/master/CHANGELOG.md" }
    $bat = "@echo off`r`ncd /d `"%~dp0`"`r`nGuild3ModUploader.exe update --mod $itemId --content `".\content`" --preview `".\preview.jpg`" --changenote `"$($ChangeNote.Replace('"', "'"))`"`r`npause`r`n"
    Set-Content $update $bat -NoNewline -Encoding Ascii
    if (Test-Path $publish) { Remove-Item $publish }   # the item exists - never publish a second one
    Write-Host ""
    Write-Host "Ready to update Workshop item $itemId (https://steamcommunity.com/sharedfiles/filedetails/?id=$itemId)."
    Write-Host "Double-click: $update"
} else {
    $bat = "@echo off`r`ncd /d `"%~dp0`"`r`nGuild3ModUploader.exe publish --title `"$title`" --description `"$shortDescription`" --content `".\content`" --preview `".\preview.jpg`"`r`npause`r`n"
    Set-Content $publish $bat -NoNewline -Encoding Ascii
    Write-Host ""
    Write-Host "Ready for the first upload. Double-click: $publish"
    Write-Host "(The item is only visible to you until you make it public on its Workshop page.)"
}
