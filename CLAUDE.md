# PodpisPdf — wskazówki dla Claude

Aplikacja WinUI 3 (Windows App SDK, .NET 10; logika w `PodpisPdf.Core` na .NET 8) do kwalifikowanego podpisu PAdES plików PDF. Opis w `README.md`,
wymagania w `docs/SPECYFIKACJA.md`, lista testów z kartą w `docs/TESTY.md`. Rozmowa z użytkownikiem po polsku.

## Wydania — obowiązkowo po każdej zmianie programu

Każda zmiana, która wpływa na `PodpisPdf.exe` (kod w `src/`, zasoby, zależności), kończy się nowym wydaniem
w GitHub **Releases** — bez pytania użytkownika:

1. Podnieś `<Version>` w `src/PodpisPdf/PodpisPdf.csproj` (ostatnia liczba +1, np. `1.0.0.3` → `1.0.0.4`).
   Wyższy numer jest konieczny: kopia programu w `%APPDATA%\PodpisPdf` (autostart) aktualizuje się tylko
   z nowszej wersji. Większa nowa funkcja może podnieść numer bardziej (np. użytkownik zażyczył sobie 1.5).
2. Uruchom testy (`dotnet test tests/PodpisPdf.Tests`), zatwierdź i wypchnij zmiany na gałąź roboczą.
3. Przebieg `Build` (po pushu) buduje i testuje instalator, a gdy `<Version>` nie ma jeszcze znacznika
   `v<wersja>`, zadanie `wydanie` (środowisko GitHub `wydanie`) **czeka na zatwierdzenie przez użytkownika**
   („Review deployments” → „Approve”). Poproś go o to po zakończeniu budowania. Po zatwierdzeniu zadanie
   podpisuje aktualizację (`PodpisPdf.msi.sig`, sekret `KLUCZ_AKTUALIZACJI`), tworzy znacznik, wydanie
   z `PodpisPdf.msi` i kopię w repozytorium aktualizacji.
   (Wypychanie znaczników z sesji Claude Code bywa zablokowane — dlatego wydanie tworzy GitHub Actions;
   ręczne `workflow_dispatch` działa tylko dla workflowów z gałęzi `main`.)
4. Sprawdź, że wydanie istnieje i ma plik `PodpisPdf.msi`, i podaj użytkownikowi link.

Zmiany niewpływające na program (tylko dokumentacja, `CLAUDE.md`) nie wymagają wydania.

## Budowanie i testy

- Testy (Linux i Windows): `dotnet test tests/PodpisPdf.Tests` — logika podpisu na certyfikatach testowych
  z lokalnym serwerem znacznika czasu, renderowanie przez PDFium.
- `src/PodpisPdf` (WinUI 3, XAML) kompiluje się tylko na Windows (kompilator XAML) — sprawdzenie w GitHub Actions.
  Jedno okno (`OknoGlowne`) z ramką stron: `StronaGlowna`, `StronaPodgladu`, `StronaUstawien`.
- Publikacja: `dotnet publish src/PodpisPdf -c Release -r win-x64 -p:Platform=x64` → folder programu; z niego
  CI buduje instalator MSI (WiX 5, `instalator/PodpisPdf.wxs`, dla całego komputera, Program Files, certyfikat
  menu Windows 11 do „Zaufanych osób”). Nie wracać do trybu „jeden plik exe” — na części komputerów WinUI
  nie znajdowało wtedy zasobów („XAML parsing failed”). Wersja MSI: X.Y.(Z*1000+W).
- CI testuje instalator (instalacja, `--sprawdz`, zwykły start, certyfikat, odinstalowanie) i zwykły start
  z zainstalowanym w systemie Windows App Runtime.
- CI uruchamia `PodpisPdf.exe --sprawdz` (test startu) i `--zrzuty <folder>` (zrzuty stron w obu motywach);
  zrzuty trafiają na gałąź `zrzuty-ekranu` (pobranie: `git fetch origin zrzuty-ekranu`).

## Integracja z Windows (3.0)

- Aktualizacje: `PodpisPdf.Core/Aktualizacje.cs` czyta `releases/latest` publicznego repo `Orinoko357/PodpisPdf-wydania`
  (wymaga `PodpisPdf.msi`, `PodpisPdf.msi.sha256` i `PodpisPdf.msi.sig` w wydaniu; aktualizacja uruchamia msiexec); workflow publikuje tam z sekretem `TOKEN_WYDANIA`.
- Podpis aktualizacji (`PodpisPdf.Core/PodpisAktualizacji.cs`): ECDSA P-256 nad „wersja + plik + SHA-256”; program
  ma wbudowane klucze publiczne (główny + zapasowy offline) i bez ważnego podpisu nie instaluje aktualizacji.
  Klucz prywatny główny tylko w sekrecie środowiska `wydanie` — nigdy w repozytorium ani w rozmowie.
- Menu Windows 11: pakiet `pakiet/AppxManifest.xml` (sparse, podpis sekretem `CERTYFIKAT_PFX`, CN=PodpisPdf) osadzany
  w programie, rejestrowany automatycznie przy starcie z zewnętrzną lokalizacją = folder programu; serwer COM `-Embedding` (`SerwerMenu.cs`). Okno WinUI **nie może** mieć tożsamości pakietu (nie znajduje
  wtedy zasobów) — dlatego `app.manifest` nie ma elementu `<msix>`, a serwer menu uruchamia program przez Eksplorator.
- Bez PublishTrimmed: przycinanie zepsuło zwykły start (XAML parsing failed). CI testuje zwykły start, nie tylko `--sprawdz`.
- PDFium wywoływane bezpośrednio (`DokumentPdf.Pdfium`), na jednym wątku; Docnet.Core tylko dostarcza bibliotekę natywną.

## Wizualizacja podpisu

Arial (z `C:\Windows\Fonts`, zastępczo wbudowana Liberation Sans): imię i nazwisko 11 pt, nagłówek i data 8 pt,
stanowisko 7–9 pt. Rozmiar prostokąta wynika z treści (`Wizualizacja.Rozmiar(dane)`, bez stałego 85 × 25 mm);
na podglądzie można go narysować w skali 0,6–3× (cała treść skaluje się proporcjonalnie, `MiejscePodpisu.Skala`). Bez ramki, przezroczyste tło. Kolejność: „Podpisano elektronicznie przez:”,
imię i nazwisko (zawsze pogrubione), stanowisko (Enter = podział wiersza), data bez sekund. Logo po lewej,
wyrównane do góry i dołu bloku tekstu; SVG/PDF zostają wektorowe, PNG/JPG w oryginalnej rozdzielczości.
Zmiany wyglądu sprawdzaj na wyrenderowanym podglądzie (np. `pdftoppm`) przed wypchnięciem.
