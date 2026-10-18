#!/bin/bash
# Paczka PodpiszPdf.app i obraz DMG z folderu publikacji (dotnet publish src/PodpiszPdf.Mac -r osx-arm64|osx-x64).
# Użycie: zbuduj-app.sh <folder publikacji> <wersja, np. 4.3.0.0> <plik .dmg>
# Podpis „ad hoc” (bez certyfikatu Apple): wymagany dla kodu arm64; przy pierwszym otwarciu macOS prosi o potwierdzenie.
set -euo pipefail
zrodlo="$1"; wersja="$2"; dmg="$3"
tu="$(cd "$(dirname "$0")" && pwd)"
robocze="$(mktemp -d)"
app="$robocze/PodpiszPdf/PodpiszPdf.app"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$zrodlo"/. "$app/Contents/MacOS/"
cp "$tu/../../src/PodpiszPdf.Mac/PodpiszPdf.icns" "$app/Contents/Resources/"
krotka="$(echo "$wersja" | cut -d. -f1-3)"
sed -e "s/WERSJA_KROTKA/$krotka/" -e "s/WERSJA/$wersja/" "$tu/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/PodpiszPdf"

codesign --force --deep --sign - --timestamp=none "$app"
codesign --verify --deep --strict "$app"

# Obraz DMG: program i skrót do folderu Aplikacje (instalacja przeciągnięciem).
ln -s /Applications "$robocze/PodpiszPdf/Aplikacje"
rm -f "$dmg"
hdiutil create -volname "PodpiszPdf $krotka" -srcfolder "$robocze/PodpiszPdf" -fs HFS+ -format UDZO -ov "$dmg"
echo "Paczka: $app"
echo "Obraz: $dmg ($(du -h "$dmg" | cut -f1))"
echo "app=$app" >> "${GITHUB_OUTPUT:-/dev/null}"
