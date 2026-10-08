#!/usr/bin/env bash
# Bootstrap the existing checkout; each cloud task is already isolated.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/cloud-env.sh"
cd "$VACATIONAPP_REPO_ROOT"

if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
  printf 'Este bootstrap requiere Linux x64. Usa las herramientas MAUI de tu plataforma.\n' >&2
  exit 1
fi
for command_name in curl python3 tar sha512sum sha256sum; do
  command -v "$command_name" >/dev/null || { printf 'Falta %s.\n' "$command_name" >&2; exit 1; }
done

download_dir="$VACATIONAPP_TOOLS_DIR/downloads"
log_dir="$VACATIONAPP_TOOLS_DIR/logs"
mkdir -p "$download_dir" "$log_dir" "$DOTNET_ROOT" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$XDG_DATA_HOME"

download() {
  curl --fail --location --retry 3 --proto '=https' --tlsv1.2 --silent --show-error \
    "$1" --output "$2.part"
  mv "$2.part" "$2"
}

sdk_version=10.0.401
sdk_sha512=51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b
if [[ ! -x "$DOTNET_ROOT/dotnet" || "$("$DOTNET_ROOT/dotnet" --version)" != "$sdk_version" ]]; then
  printf 'Instalando .NET SDK %s con SHA512 verificado...\n' "$sdk_version"
  sdk_archive="$download_dir/dotnet-sdk-$sdk_version-linux-x64.tar.gz"
  sdk_metadata="$download_dir/dotnet-10.0-releases.json"
  download https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json "$sdk_metadata"
  sdk_url="$(python3 "$script_dir/setup-helpers.py" dotnet-url "$sdk_metadata" "$sdk_version" "$sdk_sha512")"
  if [[ ! -f "$sdk_archive" ]]; then download "$sdk_url" "$sdk_archive"; fi
  printf '%s  %s\n' "$sdk_sha512" "$sdk_archive" | sha512sum --check --status
  tar -xzf "$sdk_archive" -C "$DOTNET_ROOT"
fi
[[ "$(dotnet --version)" == "$sdk_version" ]]

# Workload installation on Linux does not verify NuGet signatures. Verify the
# archives ourselves before any build, including packs reused from an earlier run.
printf 'Preparando workload maui-android...\n'
dotnet workload install maui-android --skip-manifest-update --verbosity minimal
mapfile -d '' workload_archives < <(find "$DOTNET_ROOT/packs" "$DOTNET_ROOT/library-packs" "$DOTNET_ROOT/template-packs" -type f -name '*.nupkg' -print0)
if (( ${#workload_archives[@]} == 0 )); then
  printf 'No se encontraron paquetes para verificar el workload.\n' >&2
  exit 1
fi
signature_log="$log_dir/workload-signatures.log"
if ! dotnet nuget verify --all "${workload_archives[@]}" >"$signature_log" 2>&1; then
  tail -n 80 "$signature_log" >&2
  printf 'Falló la verificación de firmas; no se compilará con estos paquetes.\n' >&2
  exit 1
fi
python3 "$script_dir/setup-helpers.py" verify-packs "$DOTNET_ROOT/packs"
printf 'Firmas verificadas: %s paquetes. Registro: %s\n' "${#workload_archives[@]}" "$signature_log"

jdk_version=21.0.12.1
jdk_sha256=ce79869e1307ed8ee1e2baa86a412b1eb5b75d10a01006d788a6f968bcfaee94
if [[ ! -x "$JAVA_HOME/bin/javac" || "$("$JAVA_HOME/bin/javac" -version 2>&1)" != "javac $jdk_version" ]]; then
  printf 'Instalando Temurin JDK %s con SHA256 verificado...\n' "$jdk_version"
  jdk_archive="$download_dir/OpenJDK21U-jdk_x64_linux_hotspot_21.0.12.1_1.tar.gz"
  jdk_url='https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jdk_x64_linux_hotspot_21.0.12.1_1.tar.gz'
  if [[ ! -f "$jdk_archive" ]]; then download "$jdk_url" "$jdk_archive"; fi
  printf '%s  %s\n' "$jdk_sha256" "$jdk_archive" | sha256sum --check --status
  mkdir -p "$JAVA_HOME"
  tar -xzf "$jdk_archive" -C "$JAVA_HOME" --strip-components=1
fi
"$JAVA_HOME/bin/javac" -version

# Google supplies the archive checksums in its HTTPS repository manifest.
# Install only compile/deploy prerequisites; emulator and SDK Manager are optional.
android_metadata="$download_dir/android-repository.xml"
download https://dl.google.com/android/repository/repository2-3.xml "$android_metadata"
python3 "$script_dir/setup-helpers.py" install-android "$android_metadata" "$ANDROID_HOME" "$download_dir"

printf 'Restaurando dependencias sin modificar lockfiles...\n'
dotnet restore tests/VacationApp.Core.Tests/VacationApp.Core.Tests.csproj --locked-mode
dotnet restore src/VacationApp/VacationApp.csproj --locked-mode
printf 'Entorno listo. Ejecuta scripts/check.sh para pruebas y APK Android.\n'
