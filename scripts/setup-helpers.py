#!/usr/bin/env python3
"""Small standard-library helpers for trusted SDK installation and verification."""

import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import subprocess
import sys
import tempfile
from urllib.parse import urljoin, urlparse
import xml.etree.ElementTree as ET
from zipfile import ZipFile


def digest_file(path: Path, algorithm: str) -> str:
    digest = hashlib.new(algorithm)
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def dotnet_url(metadata: Path, version: str, expected_hash: str) -> None:
    for release in json.loads(metadata.read_text())["releases"]:
        for sdk in release.get("sdks", [release.get("sdk", {})]):
            if sdk.get("version") != version:
                continue
            for archive in sdk.get("files", []):
                if archive.get("rid") == "linux-x64" and archive["name"].endswith(".tar.gz"):
                    url = archive["url"]
                    if archive["hash"].lower() != expected_hash:
                        raise RuntimeError("El SHA512 oficial no coincide con el SDK fijado.")
                    if urlparse(url).scheme != "https" or urlparse(url).hostname not in {
                        "builds.dotnet.microsoft.com", "download.visualstudio.microsoft.com",
                        "dotnetcli.azureedge.net", "dotnetcli.blob.core.windows.net",
                    }:
                        raise RuntimeError("Destino no reconocido en metadatos .NET.")
                    print(url)
                    return
    raise RuntimeError(f"No se encontró .NET {version} Linux x64 en metadatos oficiales.")


def safe_member(name: str) -> PurePosixPath:
    path = PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts or "\\" in name:
        raise RuntimeError(f"Ruta de archivo no segura: {name}")
    return path


def verify_packs(root: Path) -> None:
    # Signature verification is done by dotnet nuget verify before this function.
    # NuGet deliberately omits these ZIP container metadata files when extracting.
    compared = 0
    for package in sorted(root.rglob("*.nupkg")):
        with ZipFile(package) as archive:
            for entry in archive.infolist():
                if entry.is_dir() or entry.filename == "[Content_Types].xml" or entry.filename.startswith(("_rels/", "package/")):
                    continue
                name = safe_member(entry.filename)
                installed = package.parent.joinpath(*name.parts)
                if not installed.is_file():
                    raise RuntimeError(f"Falta archivo de pack firmado: {installed}")
                with archive.open(entry) as stream:
                    digest = hashlib.sha256()
                    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                        digest.update(chunk)
                if digest.hexdigest() != digest_file(installed, "sha256"):
                    raise RuntimeError(f"El pack instalado difiere del archivo firmado: {installed}")
                compared += 1
    if not compared:
        raise RuntimeError("No se pudo comparar ningún archivo de workload.")
    print(f"Integridad comprobada: {compared} archivos de packs firmados.")


def download_archive(url: str, destination: Path) -> None:
    partial = destination.with_name(destination.name + ".part")
    subprocess.run([
        "curl", "--fail", "--location", "--retry", "3", "--proto", "=https",
        "--tlsv1.2", "--silent", "--show-error", url, "--output", str(partial),
    ], check=True)
    partial.replace(destination)


def install_android(metadata: Path, sdk_root: Path, cache: Path) -> None:
    repository = ET.parse(metadata).getroot()
    packages = {package.get("path"): package for package in repository.findall("remotePackage")}
    for identifier, relative_destination in {
        "platforms;android-36": "platforms/android-36",
        "build-tools;36.0.0": "build-tools/36.0.0",
        "platform-tools": "platform-tools",
    }.items():
        package = packages.get(identifier)
        if package is None:
            raise RuntimeError(f"Falta {identifier} en el repositorio oficial Android.")
        selected = [archive for archive in package.findall("archives/archive") if archive.findtext("host-os") in (None, "linux")]
        if len(selected) != 1:
            raise RuntimeError(f"No hay un archivo Linux único para {identifier}.")
        complete = selected[0].find("complete")
        checksum = complete.find("checksum")
        algorithm = checksum.get("type", "sha1").lower()
        if algorithm not in ("sha1", "sha256", "sha512"):
            raise RuntimeError(f"Algoritmo no reconocido: {algorithm}")
        url = urljoin("https://dl.google.com/android/repository/", complete.findtext("url"))
        parsed = urlparse(url)
        if parsed.scheme != "https" or parsed.hostname != "dl.google.com":
            raise RuntimeError("Archivo Android fuera del repositorio oficial.")
        archive_path = cache / Path(parsed.path).name
        if not archive_path.is_file():
            download_archive(url, archive_path)
        if archive_path.stat().st_size != int(complete.findtext("size")):
            raise RuntimeError(f"Tamaño incorrecto de {archive_path}; elimina esta copia y reintenta.")
        if digest_file(archive_path, algorithm) != checksum.text.strip().lower():
            raise RuntimeError(f"Checksum incorrecto de {archive_path}; no se extraerá.")
        destination = sdk_root / relative_destination
        # Verify cached installed contents against the trusted archive before reuse.
        with ZipFile(archive_path) as archive:
            names = [safe_member(entry.filename) for entry in archive.infolist()]
            roots = {name.parts[0] for name in names if name.parts}
            if len(roots) != 1:
                raise RuntimeError(f"Estructura inesperada de {archive_path}.")
            if destination.is_dir() and installed_matches(archive, destination):
                print(f"Android {identifier}: archivo verificado, instalación reutilizada.")
                continue
            sdk_root.mkdir(parents=True, exist_ok=True)
            with tempfile.TemporaryDirectory(prefix="android-extract-", dir=sdk_root) as temporary:
                extracted = Path(temporary) / "package"
                extracted.mkdir()
                for entry in archive.infolist():
                    member = safe_member(entry.filename)
                    output = extracted.joinpath(*member.parts[1:])
                    mode = entry.external_attr >> 16
                    if stat.S_ISLNK(mode):
                        raise RuntimeError(f"Enlace simbólico inesperado en SDK Android: {entry.filename}")
                    if entry.is_dir():
                        output.mkdir(parents=True, exist_ok=True)
                        continue
                    output.parent.mkdir(parents=True, exist_ok=True)
                    with archive.open(entry) as source, output.open("wb") as target:
                        shutil.copyfileobj(source, target)
                    os.chmod(output, stat.S_IMODE(mode) or 0o644)
                if not (extracted / "source.properties").is_file():
                    raise RuntimeError(f"Falta source.properties en {identifier}.")
                destination.parent.mkdir(parents=True, exist_ok=True)
                if destination.exists():
                    shutil.rmtree(destination)
                extracted.replace(destination)
        print(f"Android {identifier}: instalado con {algorithm.upper()} oficial verificado.")


def installed_matches(archive: ZipFile, destination: Path) -> bool:
    for entry in archive.infolist():
        if entry.is_dir():
            continue
        member = safe_member(entry.filename)
        installed = destination.joinpath(*member.parts[1:])
        if not installed.is_file():
            return False
        with archive.open(entry) as stream:
            digest = hashlib.sha256()
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        if digest.hexdigest() != digest_file(installed, "sha256"):
            return False
        # Execute permissions are needed for aapt, aapt2, zipalign, and adb.
        expected_executable = (entry.external_attr >> 16) & 0o111
        if expected_executable and not (installed.stat().st_mode & expected_executable):
            return False
    return True


if __name__ == "__main__":
    try:
        if sys.argv[1] == "dotnet-url":
            dotnet_url(Path(sys.argv[2]), sys.argv[3], sys.argv[4])
        elif sys.argv[1] == "verify-packs":
            verify_packs(Path(sys.argv[2]))
        elif sys.argv[1] == "install-android":
            install_android(Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]))
        else:
            raise RuntimeError("Operación de setup no reconocida.")
    except (RuntimeError, OSError, ET.ParseError, subprocess.CalledProcessError) as error:
        print(f"Error de setup: {error}", file=sys.stderr)
        sys.exit(1)
