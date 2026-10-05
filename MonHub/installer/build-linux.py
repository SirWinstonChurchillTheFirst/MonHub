#!/usr/bin/env python3
"""
Builds Installer/MonHub-linux-x64.tar.gz: MonHub (with its randomizer) + Java + melonDS + mGBA + Azahar, all below one
folder – the Linux twin of build.ps1. Runs on Windows or Linux.

Needs: .NET 10 SDK, Python 3, the built java/RandoHelper.jar (build.ps1 makes it; else a JDK for javac/jar) and the .jar
of Universal Pokemon Randomizer ZX 4.6.1 next to the source folders (found by its checksum, any file name).
The emulators and the Java runtime are downloaded from their official releases into Hub-Deps and checked.

The archive holds a folder "MonHub": unpack it anywhere, run ./install.sh once (menu entry, emulators unpacked), done.
"""
import hashlib, io, os, pathlib, shutil, subprocess, sys, tarfile, urllib.request, zipfile

HERE = pathlib.Path(__file__).resolve().parent
HUB = HERE.parent
ROOT = HUB.parent
DEPS = ROOT / "Hub-Deps"
BUILD = ROOT / "Hub-Build-Linux"
APP = BUILD / "MonHub" / "System" / "App"
EMU = BUILD / "MonHub" / "System" / "Emulatoren"
OUT = ROOT / "Installer" / "MonHub-linux-x64.tar.gz"

RANDOMIZER_SHA = "380dc1e6c704a9a4ed8433e8b7892a149390f8912b9107a2a0e7263cfd71c7d8"  # Universal Pokemon Randomizer ZX 4.6.1

# Downloads are checked against these SHA-256 sums – a changed file on the server never ends up in the package.
# A new version: download, check it, put its sum here.
DOWNLOADS = {
    "melonDS-1.1-appimage-x86_64.zip": (
        "https://github.com/melonDS-emu/melonDS/releases/download/1.1/melonDS-1.1-appimage-x86_64.zip",
        "bf377420a2e95f2cd2cdda17d5372b51c2534f858b038db9ec6d129554875124"),
    "mGBA-0.10.5-appimage-x64.appimage": (
        "https://github.com/mgba-emu/mgba/releases/download/0.10.5/mGBA-0.10.5-appimage-x64.appimage",
        "fdf0a5c1588e1606c38315735cf48a9f9dca3573f32a3947ebc956f8297e85cd"),
    "azahar-2126.1.2.AppImage": (
        "https://github.com/azahar-emu/azahar/releases/download/2126.1.2/azahar.AppImage",
        "1ea15020334ee2e8fd16fbb3911fa7eafa8111e311c54dd4387b61b2ea742df6"),
    "OpenJDK21U-jre_x64_linux_hotspot_21.0.12.1_1.tar.gz": (
        "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_x64_linux_hotspot_21.0.12.1_1.tar.gz",
        "2413149700df0f7d440500a84a8f764c535f21e5a5e87d38328b64eec2c5b500"),
    # libOpenGL.so.0 (libglvnd): melonDS and Azahar need it, and a fresh Ubuntu does not have it
    "libopengl0_1.6.0-1_amd64.deb": (
        "http://deb.debian.org/debian/pool/main/libg/libglvnd/libopengl0_1.6.0-1_amd64.deb",
        "dc71cb5aaddeb8b09b3d63b8426fd651d8c79ad55b23dbe640c1abbc94c85013"),
    "azahar-license-2126.1.2.txt": (
        "https://raw.githubusercontent.com/azahar-emu/azahar/2126.1.2/license.txt",
        "a77e81713ddfe05f9f7f3a3c46de4147f82b5fe9d6e5e24af96e36a6de53f613"),
}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def fetch(name):
    url, want = DOWNLOADS[name]
    path = DEPS / name
    if not path.exists():
        print("  download", name)
        with urllib.request.urlopen(url) as r, open(path, "wb") as f:
            shutil.copyfileobj(r, f)
    have = sha256(path)
    if want == "PIN-ME":
        sys.exit(f"No checksum pinned for {name} yet. Check the download, then put this into DOWNLOADS:\n  {have}")
    if have != want:
        sys.exit(f"Checksum mismatch: {path}\n  expected {want}\n  is       {have}")
    return path


def deb_files(deb):
    """The files inside a .deb (an 'ar' archive holding data.tar.xz) as {path: bytes}."""
    import lzma
    raw = pathlib.Path(deb).read_bytes()
    assert raw[:8] == b"!<arch>\n", "not a .deb"
    pos = 8
    while pos < len(raw):
        name = raw[pos:pos + 16].decode().strip().rstrip("/")
        size = int(raw[pos + 48:pos + 58].decode().strip())
        body = raw[pos + 60:pos + 60 + size]
        if name.startswith("data.tar"):
            assert name.endswith(".xz"), f"unexpected packing: {name}"
            with tarfile.open(fileobj=io.BytesIO(lzma.decompress(body))) as tar:
                links = {m.name.lstrip("./"): m.linkname for m in tar if m.issym()}
                files = {m.name.lstrip("./"): tar.extractfile(m).read() for m in tar if m.isfile()}
            return files, links
        pos += 60 + size + (size & 1)
    raise ValueError("no data in " + str(deb))


def run(*cmd):
    print(" ", " ".join(str(c) for c in cmd))
    subprocess.run(cmd, check=True)


def main():
    DEPS.mkdir(exist_ok=True)
    if BUILD.exists():
        shutil.rmtree(BUILD)
    for d in (APP, EMU / "melonDS", EMU / "mGBA", EMU / "Azahar"):
        d.mkdir(parents=True)

    jar = next((p for p in ROOT.glob("*.jar") if sha256(p) == RANDOMIZER_SHA), None)
    if jar is None:
        sys.exit(f"Randomizer missing – put the .jar of Universal Pokemon Randomizer ZX 4.6.1 "
                 f"(https://github.com/Ajarmar/universal-pokemon-randomizer-zx/releases) into {ROOT}.")
    helper = HUB / "java" / "RandoHelper.jar"
    if not helper.exists():
        classes = HUB / "java" / "build"
        classes.mkdir(exist_ok=True)
        run("javac", "--release", "8", "-nowarn", "-cp", jar, "-d", classes, HUB / "java" / "RandoHelper.java")
        run("jar", "cf", helper, "-C", classes, ".")
        shutil.rmtree(classes)

    print("1. MonHub (self-contained: nobody needs .NET installed)")
    run("dotnet", "publish", HUB / "MonHub.csproj", "-c", "Release", "-r", "linux-x64", "--self-contained", "true", "-o", APP, "--nologo", "-v", "q")
    for junk in ("bin", "obj"):
        shutil.rmtree(HUB / junk, ignore_errors=True)

    licences = APP / "Lizenzen"
    licences.mkdir()
    for f in (HUB / "Assets" / "Fonts").glob("*-OFL.txt"):
        shutil.copy(f, licences)
    shutil.copy(ROOT / "LICENSE", licences / "GPL-3.0.txt")
    shutil.copy(HUB / "CREDITS.md", licences / "CREDITS.md")
    # the menu icon: Porygon's face (PMD Sprite Collab), enlarged with hard pixels
    try:
        from PIL import Image
        Image.open(HUB / "Assets" / "Portraits" / "0137-Normal.png").resize((256, 256), Image.NEAREST).save(APP / "monhub.png")
    except ImportError:
        shutil.copy(HUB / "Assets" / "Portraits" / "0137-Normal.png", APP / "monhub.png")

    print("2. Randomizer and the presets MonHub ships")
    shutil.copy(jar, APP / "randomizer.jar")
    (APP / "Settings").mkdir()
    for f in (HUB / "presets").glob("*.rnqs"):
        shutil.copy(f, APP / "Settings")

    print("3. Emulators – the official Linux builds (AppImages; install.sh unpacks them, so no FUSE is needed)")
    with zipfile.ZipFile(fetch("melonDS-1.1-appimage-x86_64.zip")) as z:
        name = next(n for n in z.namelist() if n.lower().endswith(".appimage"))
        (EMU / "melonDS" / "melonDS.AppImage").write_bytes(z.read(name))
    # melonDS' starting settings: MonHub copies them to "config/melonDS" on the first start (an update unpacked over an
    # install must not replace the player's own settings)
    shutil.copy(HUB / "emulator-config" / "melonDS.toml", EMU / "melonDS" / "melonDS.default.toml")
    shutil.copy(HUB / "emulator-config" / "melonDS-LIZENZ.txt", EMU / "melonDS")
    shutil.copy(fetch("mGBA-0.10.5-appimage-x64.appimage"), EMU / "mGBA" / "mGBA.AppImage")
    shutil.copy(fetch("azahar-2126.1.2.AppImage"), EMU / "Azahar" / "azahar.AppImage")
    shutil.copy(fetch("azahar-license-2126.1.2.txt"), EMU / "Azahar" / "license.txt")

    # a reserve copy of libOpenGL.so.0, used only when the system has none (see Os.Start)
    files, links = deb_files(fetch("libopengl0_1.6.0-1_amd64.deb"))
    reserve = EMU / "lib"
    reserve.mkdir()
    lib = "usr/lib/x86_64-linux-gnu/libOpenGL.so.0"
    real = files[str(pathlib.PurePosixPath(lib).parent / links[lib])] if lib in links else files[lib]
    (reserve / "libOpenGL.so.0").write_bytes(real)
    (reserve / "libOpenGL-LICENSE.txt").write_bytes(files["usr/share/doc/libopengl0/copyright"])

    top = BUILD / "MonHub"
    for d in ("ROMs", "Randomisierte ROMs", "Spielstände", "Fangames"):
        (top / d).mkdir()
    for f in ("install.sh", "uninstall.sh", "LIESMICH-Linux.txt", "README-Linux.txt"):
        data = (HERE / "linux" / f).read_bytes().replace(b"\r\n", b"\n")
        (top / f).write_bytes(data)

    print("4. The archive (with the Java runtime; programs keep their 'may run' bit)")
    OUT.parent.mkdir(exist_ok=True)
    programs = {"MonHub/install.sh", "MonHub/uninstall.sh", "MonHub/System/App/MonHub", "MonHub/System/App/createdump"}
    with tarfile.open(OUT, "w:gz", compresslevel=6) as tar:
        for path in sorted(BUILD.rglob("*")):
            rel = path.relative_to(BUILD).as_posix()
            info = tar.gettarinfo(path, arcname=rel)
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            if path.is_dir():
                info.mode = 0o755
                tar.addfile(info)
                continue
            info.mode = 0o755 if rel in programs or rel.endswith(".AppImage") else 0o644
            with open(path, "rb") as f:
                tar.addfile(info, f)
        # the Java runtime straight from its archive: links and "may run" bits stay as they are
        with tarfile.open(fetch("OpenJDK21U-jre_x64_linux_hotspot_21.0.12.1_1.tar.gz")) as jre:
            for member in jre:
                parts = member.name.split("/", 1)
                if len(parts) < 2 or parts[1].startswith(("man/", "legal/")):
                    continue
                member.name = "MonHub/System/App/jre/" + parts[1]
                member.uid = member.gid = 0
                member.uname = member.gname = ""
                tar.addfile(member, jre.extractfile(member) if member.isfile() else None)
    print(f"done: {OUT} ({OUT.stat().st_size / 1e6:.0f} MB)\n  sha256 {sha256(OUT)}")


if __name__ == "__main__":
    main()
