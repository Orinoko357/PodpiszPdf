# PodpisPdf — wskazówki dla Claude

Aplikacja WinForms (.NET 8) do kwalifikowanego podpisu PAdES plików PDF. Opis w `README.md`,
wymagania w `docs/SPECYFIKACJA.md`, lista testów z kartą w `docs/TESTY.md`. Rozmowa z użytkownikiem po polsku.

## Wydania — obowiązkowo po każdej zmianie programu

Każda zmiana, która wpływa na `PodpisPdf.exe` (kod w `src/`, zasoby, zależności), kończy się nowym wydaniem
w GitHub **Releases** — bez pytania użytkownika:

1. Podnieś `<Version>` w `src/PodpisPdf/PodpisPdf.csproj` (ostatnia liczba +1, np. `1.0.0.3` → `1.0.0.4`).
   Wyższy numer jest konieczny: kopia programu w `%APPDATA%\PodpisPdf` (autostart) aktualizuje się tylko
   z nowszej wersji.
2. Uruchom testy (`dotnet test tests/PodpisPdf.Tests`), zatwierdź i wypchnij zmiany na gałąź roboczą.
3. Opublikuj wydanie: uruchom workflow `build.yml` (`workflow_dispatch`) na tej gałęzi z wejściem
   `wersja` = nowy numer (bez „v”). Workflow buduje na Windows, uruchamia testy, tworzy znacznik `v<wersja>`
   i wydanie z plikiem `PodpisPdf.exe`. (Wypychanie znaczników z sesji Claude Code bywa zablokowane —
   dlatego wydanie tworzy GitHub Actions.)
4. Sprawdź, że wydanie istnieje i ma plik `PodpisPdf.exe`, i podaj użytkownikowi link.

Zmiany niewpływające na program (tylko dokumentacja, `CLAUDE.md`) nie wymagają wydania.

## Budowanie i testy

- Testy (Linux i Windows): `dotnet test tests/PodpisPdf.Tests` — logika podpisu na certyfikatach testowych
  z lokalnym serwerem znacznika czasu, renderowanie przez PDFium.
- `src/PodpisPdf` (WinForms) buduje się tylko pakietem SDK z obsługą Windows Desktop; na Linuksie z SDK
  z dystrybucji można jedynie sprawdzić kompilację osobnym projektem z
  `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />` i `EnableWindowsTargeting`.
- Publikacja: `dotnet publish src/PodpisPdf -c Release -r win-x64` → jeden plik `PodpisPdf.exe`.

## Wizualizacja podpisu

Stały rozmiar 85 × 25 mm, bez ramki, przezroczyste tło. Kolejność: „Podpisano elektronicznie przez:”,
imię i nazwisko (zawsze pogrubione), stanowisko (Enter = podział wiersza), data bez sekund. Logo po lewej,
wyrównane do góry i dołu bloku tekstu; SVG/PDF zostają wektorowe, PNG/JPG w oryginalnej rozdzielczości.
Zmiany wyglądu sprawdzaj na wyrenderowanym podglądzie (np. `pdftoppm`) przed wypchnięciem.
