# Builds Installer\MonHub-Setup.exe: MonHub (with its randomizer) + Java + melonDS + DeSmuME + mGBA + Azahar, all below one folder.
# Needs: .NET 10 SDK, JDK 17 (javac, jar, jlink), Python 3 with Pillow, Inno Setup 6, PokeRandoZX.jar (Universal Pokemon
# Randomizer ZX 4.6.1) next to the source folders, and the emulators at D:\Pokemon\Melonds and D:\Pokemon\desmume-0.9.13-win64.
# The pictures are not in the repository: on the first build they are downloaded from PMD Sprite Collab (see README).
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true # a failing dotnet/jlink/ISCC stops the build – never ship a half-built installer
$hubSrc  = Split-Path $PSScriptRoot -Parent
$root    = Split-Path $hubSrc -Parent
$build   = Join-Path $root "Hub-Build"
$app     = Join-Path $build "System\App"
$emu     = Join-Path $build "System\Emulatoren"

if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force $app, "$emu\melonDS", "$emu\DeSmuME", "$emu\mGBA", "$emu\Azahar" | Out-Null

# Downloads (and the randomizer) are checked against these SHA-256 sums – a changed file on the server never ends up in
# the installer. A new version: download, check it, put its sum here.
$sha256 = @{
    "PokeRandoZX.jar"                    = "380DC1E6C704A9A4ED8433E8B7892A149390F8912B9107A2A0E7263CFD71C7D8"
    "mGBA-0.10.5-win64.7z"               = "B497A57C7D9093834DADC64F33A90F7C411439C21FDB8A0143255A45EA37563A"
    "azahar-windows-msvc-2126.1.2.zip"   = "BBFF556D1736301DEBDEC2872A5E9F3678C52E84261AABAA4F708E55DB9AE139"
    "azahar-license-2126.1.2.txt"        = "A77E81713DDFE05F9F7F3A3C46DE4147F82B5FE9D6E5E24AF96E36A6DE53F613"
}
function Assert-Hash($file) {
    $want = $sha256[(Split-Path $file -Leaf)]
    $have = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($have -ne $want) { throw "Prüfsumme stimmt nicht: $file`n  erwartet $want`n  ist      $have" }
}

# 0a. Pictures and credits (PMD Sprite Collab, CC BY-NC 4.0) and fonts – downloaded once by the tools, kept afterwards
$tools = Join-Path $hubSrc "tools"
if (-not (Test-Path (Join-Path $hubSrc "Assets\Species\0001.png"))) { python (Join-Path $tools "build_species.py") }
if (-not (Test-Path (Join-Path $hubSrc "Assets\Sprites\0025-idle.png"))) { python (Join-Path $tools "build_sprites.py") }
if (-not (Test-Path (Join-Path $hubSrc "Assets\Portraits\0137-Normal.png")) -or -not (Test-Path (Join-Path $hubSrc "pokeball.ico"))) { python (Join-Path $tools "build_assets.py") }
if (-not (Test-Path (Join-Path $hubSrc "CREDITS.md"))) { python (Join-Path $tools "build_credits.py") }
if (-not (Test-Path (Join-Path $PSScriptRoot "art\wizard-164x314.bmp"))) { python (Join-Path $PSScriptRoot "art\make_art.py") }
if (-not (Test-Path (Join-Path $root "PokeRandoZX.jar"))) {
    throw "PokeRandoZX.jar fehlt – Universal Pokemon Randomizer ZX 4.6.1 von https://github.com/Ajarmar/universal-pokemon-randomizer-zx/releases herunterladen und die .jar nach $root legen."
}

Assert-Hash (Join-Path $root "PokeRandoZX.jar")

# 0b. Java helper for the randomizer (wraps its CLI, see java\RandoHelper.java)
$jar     = Join-Path $root "PokeRandoZX.jar"
$classes = Join-Path $hubSrc "java\build"
New-Item -ItemType Directory -Force $classes | Out-Null
javac --release 8 -nowarn -cp $jar -d $classes (Join-Path $hubSrc "java\RandoHelper.java")
jar cf (Join-Path $hubSrc "java\RandoHelper.jar") -C $classes .
Remove-Item -Recurse -Force $classes

# 1. PokéHub self-contained (friends don't need .NET installed)
dotnet publish (Join-Path $hubSrc "PokeHub.csproj") -c Release --self-contained true -o $app
Remove-Item -Recurse -Force (Join-Path $hubSrc "bin"), (Join-Path $hubSrc "obj") -ErrorAction SilentlyContinue

# licences: MonHub itself and the randomizer (GPL-3.0), the pixel fonts inside MonHub.exe (SIL OFL), the picture credits
New-Item -ItemType Directory -Force (Join-Path $app "Lizenzen") | Out-Null
Copy-Item (Join-Path $hubSrc "Assets\Fonts\*-OFL.txt") (Join-Path $app "Lizenzen")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $app "Lizenzen\GPL-3.0.txt")
Copy-Item (Join-Path $hubSrc "CREDITS.md") (Join-Path $app "Lizenzen\CREDITS.md")

# 2. Randomizer + Java runtime + the presets MonHub ships (presets\, see toolsuild_presets.ps1)
Copy-Item (Join-Path $root "PokeRandoZX.jar") $app
jlink --add-modules java.base,java.desktop,java.logging --strip-debug --no-man-pages --no-header-files --compress=2 --output (Join-Path $app "jre")
New-Item -ItemType Directory -Force (Join-Path $app "Settings") | Out-Null
Copy-Item (Join-Path $hubSrc "presets\*.rnqs") (Join-Path $app "Settings")

# 3. Emulators: melonDS with the MonHub default config, DeSmuME without personal settings
Copy-Item "D:\Pokemon\Melonds\melonDS.exe" "$emu\melonDS"
Copy-Item (Join-Path $hubSrc "emulator-config\melonDS.toml") "$emu\melonDS"
Copy-Item (Join-Path $hubSrc "emulator-config\melonDS-LIZENZ.txt") "$emu\melonDS"
$ds = "D:\Pokemon\desmume-0.9.13-win64"
Copy-Item "$ds\DeSmuME_0.9.13_x64.exe", "$ds\desmume.ddb", "$ds\COPYING", "$ds\AUTHORS", "$ds\README" "$emu\DeSmuME"

# mGBA (Game Boy, Game Boy Color, GBA) – the official portable build, MPL-2.0; downloaded once into Hub-Deps
$mgbaVersion = "0.10.5"
$deps = Join-Path $root "Hub-Deps"
$mgbaArchive = Join-Path $deps "mGBA-$mgbaVersion-win64.7z"
if (-not (Test-Path $mgbaArchive)) {
    New-Item -ItemType Directory -Force $deps | Out-Null
    Invoke-WebRequest "https://github.com/mgba-emu/mgba/releases/download/$mgbaVersion/mGBA-$mgbaVersion-win64.7z" -OutFile $mgbaArchive
}
Assert-Hash $mgbaArchive
$mgbaTemp = Join-Path $deps "mgba-extract"
if (Test-Path $mgbaTemp) { Remove-Item -Recurse -Force $mgbaTemp }
& "$env:ProgramFiles\7-Zip\7z.exe" x $mgbaArchive "-o$mgbaTemp" -y | Out-Null
Copy-Item (Join-Path $mgbaTemp "mGBA-$mgbaVersion-win64\*") "$emu\mGBA" -Recurse
Remove-Item "$emu\mGBA\mgba-sdl.exe" -ErrorAction SilentlyContinue # PokéHub starts the normal (Qt) app
New-Item -ItemType File -Force "$emu\mGBA\portable.ini" | Out-Null  # settings stay in the mGBA folder
Remove-Item -Recurse -Force $mgbaTemp

# Azahar (3DS, Gen 6–7) – the official Windows build, GPL-2.0; downloaded once into Hub-Deps. The zip stores its paths
# with backslashes, so it is unpacked with Expand-Archive. PokéHub creates the "user" folder (portable mode) itself.
$azaharVersion = "2126.1.2"
$azaharArchive = Join-Path $deps "azahar-windows-msvc-$azaharVersion.zip"
if (-not (Test-Path $azaharArchive)) {
    Invoke-WebRequest "https://github.com/azahar-emu/azahar/releases/download/$azaharVersion/azahar-windows-msvc-$azaharVersion.zip" -OutFile $azaharArchive
}
Assert-Hash $azaharArchive
$azaharTemp = Join-Path $deps "azahar-extract"
if (Test-Path $azaharTemp) { Remove-Item -Recurse -Force $azaharTemp }
Expand-Archive $azaharArchive $azaharTemp -Force
Copy-Item (Join-Path $azaharTemp "azahar-windows-msvc-$azaharVersion\*") "$emu\Azahar" -Recurse
Remove-Item -Recurse -Force $azaharTemp
$azaharLicense = Join-Path $deps "azahar-license-$azaharVersion.txt"
if (-not (Test-Path $azaharLicense)) {
    Invoke-WebRequest "https://raw.githubusercontent.com/azahar-emu/azahar/$azaharVersion/license.txt" -OutFile $azaharLicense
}
Assert-Hash $azaharLicense
Copy-Item $azaharLicense "$emu\Azahar\LICENSE.txt"
if (-not (Test-Path "$emu\Azahar\azahar.exe")) { throw "Azahar fehlt nach dem Entpacken" }

# 4. Installer
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" (Join-Path $PSScriptRoot "PokeHub.iss")
