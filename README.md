# PodpisPdf

Mała aplikacja na Windows do składania **kwalifikowanego podpisu elektronicznego** pod dokumentami PDF
(format **PAdES Baseline B-T**: podpis ze znacznikiem czasu i widoczną wizualizacją na stronie).
Typowe zastosowanie: podpisywanie wniosków z SZUP („Podpis kwalifikowany złożony poza systemem”).

Specyfikacja: [docs/SPECYFIKACJA.md](docs/SPECYFIKACJA.md).

## Jak używać

1. Uruchom `PodpisPdf.exe` i przeciągnij plik PDF do okna (albo kliknij **Wybierz plik…**).
   Plik można też przeciągnąć na ikonę `PodpisPdf.exe`.
2. Wybierz certyfikat z listy (★ — certyfikat kwalifikowany).
3. Kliknij **Podpisz**. Otworzy się podgląd wszystkich stron dokumentu — **kliknij miejsce**, w którym ma
   się znaleźć podpis, albo **przeciągnij myszą**, aby narysować go w wybranym rozmiarze (proporcje są zawsze
   zachowane, rozmiar 51 × 15 mm – 255 × 75 mm, program zapamiętuje ostatni). Zatwierdź przyciskiem **Podpisz**.
4. Wpisz PIN w oknie sterownika karty (program nie widzi PIN-u).
5. Wynik zapisuje się obok oryginału: `wniosek.pdf` → `wniosek_podpisany.pdf`. Oryginał zostaje bez zmian.

**Z Eksploratora Windows:** prawy przycisk myszy na pliku PDF → **Podpisz w PodpisPdf** (w Windows 11:
*Pokaż więcej opcji*). Program otwiera od razu podgląd do wskazania miejsca podpisu. Jeśli program już działa
(np. w zasobniku), plik trafia do działającego okna.

**Nowe menu Windows 11:** pozycja **Podpisz w PodpisPdf** może być od razu w menu (bez *Pokaż więcej opcji*) —
*Ustawienia → Program → Nowe menu kontekstowe Windows 11 → Dodaj do menu*. Wymaga to jednorazowej instalacji
certyfikatu PodpisPdf na komputerze (przycisk *Zainstaluj certyfikat…*, okno administratora; w sieci firmowej
można go rozesłać zasadami grupy do magazynu *Zaufane osoby* komputera — plik `PodpisPdf.cer` jest w każdym wydaniu).
Technicznie: mały pakiet MSIX z „zawartością zewnętrzną” wskazujący na kopię programu w `%APPDATA%\PodpisPdf`
oraz polecenie Eksploratora (IExplorerCommand) w samym `PodpisPdf.exe`.

**Domyślna aplikacja dla PDF:** *Ustawienia → Program → Ustaw jako domyślną…* rejestruje PodpisPdf jako aplikację
do PDF i otwiera *Ustawienia Windows → Aplikacje domyślne* (Windows nie pozwala programom ustawić się samodzielnie).
Dwuklik pliku PDF pokazuje wtedy okno **Podpisz / Przeglądaj** — *Przeglądaj* otwiera plik w Adobe Acrobat
(Reader), a gdy go nie ma — w aplikacji, która otwierała PDF wcześniej, albo w Microsoft Edge. Wybór można
zapamiętać (zmiana: *Ustawienia → Program → Po otwarciu pliku PDF*).

**Aktualizacje:** program raz dziennie sprawdza publiczne repozytorium wydań
[Orinoko357/PodpisPdf-wydania](https://github.com/Orinoko357/PodpisPdf-wydania/releases) i informuje o nowej wersji
(pasek w oknie, powiadomienie z zasobnika). *Zaktualizuj teraz* pobiera plik, sprawdza jego sumę SHA-256,
podmienia kopię w `%APPDATA%\PodpisPdf` i uruchamia nową wersję. Ręcznie: *Ustawienia → Informacje → Sprawdź teraz*.

**Zasobnik systemowy:** zminimalizowane okno chowa się do ikony obok zegara. Dwuklik otwiera okno;
menu ikony: *Otwórz*, *Podpisz plik PDF…*, *Zakończ*.

Lista pokazuje certyfikaty z osobistego magazynu Windows, które mają klucz prywatny, są ważne
i służą do podpisu (*nonRepudiation*). Certyfikaty z kart i z SimplySign Desktop trafiają tam same,
gdy zainstalowany jest sterownik producenta.

> Program nie sprawia sam, że podpis jest kwalifikowany — kwalifikowany jest wtedy, gdy klucz leży na
> certyfikowanym urządzeniu (karta, SimplySign), a certyfikat jest kwalifikowany. Program zapisuje podpis
> w formacie zgodnym z normami UE (ETSI EN 319 142).

## Ustawienia

Okno **Ustawienia…**; zapis w `%APPDATA%\PodpisPdf\config.json`:

| Ustawienie | Domyślnie |
|---|---|
| Stanowisko (na wizualizacji) | brak |
| Logo SVG/PDF (wektorowe) albo PNG/JPG (na wizualizacji); kopia zapisywana w `%APPDATA%\PodpisPdf\logo.*` | brak |
| Położenie początkowe na podglądzie: strona (pierwsza / ostatnia / numer), narożnik, odległość od krawędzi [mm] | ostatnia strona, prawy dolny róg, 15 × 15 mm |
| Serwer znacznika czasu (TSA), login, hasło | `http://time.certum.pl`, bez logowania |
| PAdES B-LT (dołączenie OCSP/CRL do pliku) | wyłączone |
| Przyrostek nazwy pliku wynikowego | `_podpisany` |
| Minimalizacja do zasobnika systemowego | włączona |
| Uruchamianie przy logowaniu do Windows (autostart) | wyłączone |
| „Podpisz w PodpisPdf” w menu kontekstowym plików PDF | włączone |
| Pozycja w nowym menu kontekstowym Windows 11 | wyłączona |
| Po otwarciu pliku PDF (gdy PodpisPdf jest aplikacją domyślną): pytaj / podpisuj / przeglądaj | pytaj |
| Codzienne sprawdzanie aktualizacji | włączone |

Wizualizacja ma rozmiar podstawowy **85 × 25 mm** (można go zmienić, rysując podpis na podglądzie — całość
skaluje się proporcjonalnie), bez ramki i z przezroczystym tłem (nie zasłania formularza).
Logo wektorowe (SVG lub PDF — pierwsza strona) trafia do podpisu jako grafika wektorowa, ostra przy każdym
powiększeniu. Napisy w SVG najlepiej zamienić na krzywe (w SVG bez osadzonych czcionek użyta zostanie czcionka zastępcza).
Logo PNG/JPG jest osadzane w oryginalnej rozdzielczości, bez stratnej kompresji. Białe lub przezroczyste marginesy
w pliku logo są pomijane (przycięcie w PDF, obraz pozostaje nienaruszony), więc logo zajmuje całe dostępne miejsce.
Stanowisko można podzielić na wiersze klawiszem Enter; bez tego zawija się samo.

**Autostart i kopia programu w AppData.** Po włączeniu autostartu program kopiuje się do
`%APPDATA%\PodpisPdf\PodpisPdf.exe` i stamtąd uruchamia przy logowaniu do Windows (wpis
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) — od razu zminimalizowany do zasobnika, bez pokazywania okna. Z tej kopii korzysta też menu
kontekstowe. Uruchomienie nowszej wersji (np. pobranej z *Releases*) nadpisuje kopię w AppData; starsza wersja
nie nadpisuje nowszej. Jeśli program już działa w innej wersji, pojawia się ostrzeżenie — przy nowszej wersji
można od razu zamknąć działający program i zastąpić go nową wersją.

Menu kontekstowe jest rejestrowane dla bieżącego użytkownika (`HKCU\Software\Classes\SystemFileAssociations\.pdf\shell\PodpisPdf`),
bez uprawnień administratora. Po przeniesieniu `PodpisPdf.exe` w inne miejsce wpis odświeża się przy następnym uruchomieniu.

Hasło do TSA jest szyfrowane mechanizmem Windows DPAPI — odczyta je tylko ten sam użytkownik Windows.

**Data na wizualizacji.** Wizualizacja jest częścią podpisywanych danych, a znacznik czasu powstaje
dopiero po podpisie, więc tuż przed podpisem program pobiera aktualny czas z serwera znacznika czasu
(dodatkowe żądanie do TSA) i to on trafia na wizualizację; gdy serwer nie odpowie — czas komputera.
Wiążący czas zawsze zawiera znacznik czasu w podpisie.

## Budowanie

Wymagane .NET 10 SDK (i .NET 8 dla testów) na Windows.

```
dotnet test tests/PodpisPdf.Tests
dotnet publish src/PodpisPdf -c Release -r win-x64 -p:Platform=x64 -o publish
```

Wynik: `publish\PodpisPdf.exe` — jeden plik, nie wymaga instalacji .NET ani Windows App SDK (Windows 10 1809+/11, 64-bit).
Przy pierwszym uruchomieniu plik wypakowuje biblioteki WinUI do folderu tymczasowego, więc start trwa wtedy dłużej.
GitHub Actions buduje ten plik przy każdym pushu (artefakt **PodpisPdf** w zakładce *Actions*).

## Struktura

| Katalog | Zawartość |
|---|---|
| `src/PodpisPdf.Core` | Logika: wybór certyfikatów, podpis PAdES (iText 9 + BouncyCastle), wizualizacja, znacznik czasu, podgląd stron (PDFium), komunikaty błędów |
| `src/PodpisPdf` | Interfejs WinUI 3 (Windows App SDK, .NET 10): strona główna, podgląd z wyborem miejsca, ustawienia, zasobnik, menu kontekstowe |
| `tests/PodpisPdf.Tests` | Testy na certyfikatach testowych (RSA i ECDSA) z lokalnym serwerem znacznika czasu — bez karty i bez sieci |
| `tools/PodpisTestowy` | Narzędzie deweloperskie: podpis certyfikatem z pliku PFX do sprawdzenia formatu w Adobe/EU DSS |
| `docs/TESTY.md` | Lista kroków testu z prawdziwą kartą |

Podpis próbny z certyfikatem testowym (wymaga dostępu do TSA):

```
dotnet run --project tools/PodpisTestowy -- utworz-pfx test.pfx haslo "Jan Kowalski"
dotnet run --project tools/PodpisTestowy -- podpisz wniosek.pdf test.pfx haslo
```

## Ograniczenia

- Na stronach obróconych (/Rotate) nie można umieścić podpisu — podgląd na to nie pozwala.
- Pliki PDF zabezpieczone hasłem trzeba najpierw odbezpieczyć.

## Licencje

- iText 9 — AGPL. Użytek wewnętrzny albo udostępnienie kodu źródłowego nie stanowi problemu.
- PDFium (BSD/Apache 2.0, biblioteka z pakietu Docnet.Core, MIT) — podgląd stron.
- Windows App SDK / WinUI 3, Windows Community Toolkit, H.NotifyIcon — MIT.
- Czcionka DejaVu Sans (wbudowana, obsługa polskich znaków) — licencja Bitstream Vera, zob. `src/PodpisPdf.Core/Zasoby/DejaVu-LICENSE.txt`.

## Wydania

Plik `PodpisPdf.exe` do pobrania jest w zakładce **Releases** repozytorium. Nowe wydanie powstaje:

- ręcznie: *Actions → Build → Run workflow*, wybrać gałąź i wpisać numer wersji (np. `1.0.0.2`), albo
- po wypchnięciu znacznika wersji, np.:

```
git tag v1.0.0.2
git push origin v1.0.0.2
```

Numer wersji programu ustawia się w `src/PodpisPdf/PodpisPdf.csproj` (`<Version>`).

Sekrety repozytorium (*Settings → Secrets and variables → Actions*), z których korzysta workflow:

| Sekret | Do czego |
|---|---|
| `CERTYFIKAT_PFX` | Certyfikat CN=PodpisPdf (PFX w Base64, hasło `PodpisPdf`) do podpisu pakietu menu Windows 11. Bez niego wydanie nie zawiera pakietu menu. |
| `TOKEN_WYDANIA` | Token z prawem zapisu (*Contents: Read and write*) do `Orinoko357/PodpisPdf-wydania`. Bez niego wydanie nie trafia do repozytorium aktualizacji. |

Każde wydanie zawiera `PodpisPdf.exe`, `PodpisPdf.exe.sha256` (suma sprawdzana przez aktualizację) i `PodpisPdf.cer`.
