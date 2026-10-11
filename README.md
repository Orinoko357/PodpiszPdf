# PodpiszPdf

Mała aplikacja na Windows do składania **kwalifikowanego podpisu elektronicznego** pod dokumentami PDF
(format **PAdES Baseline B-T**: podpis ze znacznikiem czasu i widoczną wizualizacją na stronie).
Typowe zastosowanie: podpisywanie wniosków z SZUP („Podpis kwalifikowany złożony poza systemem”).

Specyfikacja: [docs/SPECYFIKACJA.md](docs/SPECYFIKACJA.md).

## Jak używać

1. Zainstaluj program instalatorem `PodpiszPdf.msi` (z zakładki *Releases*; dla całego komputera, do
   `C:\Program Files\PodpiszPdf`, skrót w menu Start). Uruchom PodpiszPdf i przeciągnij plik PDF do okna (albo kliknij **Wybierz plik…**).
2. Wybierz certyfikat z listy (★ — certyfikat kwalifikowany).
3. Kliknij **Podpisz**. Otworzy się podgląd wszystkich stron dokumentu — **kliknij miejsce**, w którym ma
   się znaleźć podpis, albo **przeciągnij myszą**, aby narysować go w wybranym rozmiarze (proporcje są zawsze
   zachowane, rozmiar 51 × 15 mm – 255 × 75 mm, program zapamiętuje ostatni). Zatwierdź przyciskiem **Podpisz**.
4. Wpisz PIN w oknie sterownika karty (program nie widzi PIN-u).
5. Podpisany dokument zastępuje oryginał (podpis dopisany do pliku). Opcjonalnie, w ustawieniach: zapis jako nowy plik obok oryginału, `wniosek.pdf` → `wniosek_podpisany.pdf`.

**Z Eksploratora Windows:** prawy przycisk myszy na pliku PDF → **Podpisz w PodpiszPdf** (w Windows 11:
*Pokaż więcej opcji*). Program otwiera od razu podgląd do wskazania miejsca podpisu. Jeśli program już działa
(np. w zasobniku), plik trafia do działającego okna.

**Nowe menu Windows 11:** pozycja **Podpisz w PodpiszPdf** może być od razu w menu (bez *Pokaż więcej opcji*) —
*Ustawienia → Program → Nowe menu kontekstowe Windows 11 → Dodaj do menu*. Wymaga to certyfikatu PodpiszPdf
w magazynie *Zaufane osoby* komputera — dodaje go instalator MSI (w sieci firmowej można go też rozesłać zasadami
grupy — plik `PodpiszPdf.cer` jest w każdym wydaniu).
Technicznie: mały pakiet MSIX z „zawartością zewnętrzną” wskazujący na folder programu
oraz polecenie Eksploratora (IExplorerCommand) w samym `PodpiszPdf.exe`.

**Domyślna aplikacja dla PDF:** *Ustawienia → Program → Ustaw jako domyślną…* rejestruje PodpiszPdf jako aplikację
do PDF i otwiera *Ustawienia Windows → Aplikacje domyślne* (Windows nie pozwala programom ustawić się samodzielnie).
Dwuklik pliku PDF pokazuje wtedy okno **Podpisz / Przeglądaj** — *Przeglądaj* otwiera plik w Adobe Acrobat
(Reader), a gdy go nie ma — w aplikacji, która otwierała PDF wcześniej, albo w Microsoft Edge. Wybór można
zapamiętać (zmiana: *Ustawienia → Program → Po otwarciu pliku PDF*).

**Aktualizacje:** program raz dziennie sprawdza publiczne repozytorium wydań
[Orinoko357/PodpiszPdf-wydania](https://github.com/Orinoko357/PodpiszPdf-wydania/releases) i informuje o nowej wersji
(pasek w oknie, powiadomienie z zasobnika). *Zaktualizuj teraz* pobiera instalator, sprawdza podpis wydania
(klucz PodpiszPdf spoza GitHuba — bez ważnego podpisu aktualizacja jest blokowana) i sumę SHA-256, instaluje
nową wersję i ją uruchamia. Ręcznie: *Ustawienia → Informacje → Sprawdź teraz*.

**Zasobnik systemowy:** zminimalizowane okno chowa się do ikony obok zegara. Dwuklik otwiera okno;
menu ikony: *Otwórz*, *Podpisz plik PDF…*, *Zakończ*.

Lista pokazuje certyfikaty z osobistego magazynu Windows, które mają klucz prywatny, są ważne
i służą do podpisu (*nonRepudiation*). Certyfikaty z kart i z SimplySign Desktop trafiają tam same,
gdy zainstalowany jest sterownik producenta.

> Program nie sprawia sam, że podpis jest kwalifikowany — kwalifikowany jest wtedy, gdy klucz leży na
> certyfikowanym urządzeniu (karta, SimplySign), a certyfikat jest kwalifikowany. Program zapisuje podpis
> w formacie zgodnym z normami UE (ETSI EN 319 142).

## Ustawienia

Okno **Ustawienia…**; zapis w `%APPDATA%\PodpiszPdf\config.json`:

| Ustawienie | Domyślnie |
|---|---|
| Stanowisko (na wizualizacji) | brak |
| Logo SVG/PDF (wektorowe) albo PNG/JPG (na wizualizacji); kopia zapisywana w `%APPDATA%\PodpiszPdf\logo.*` | brak |
| Położenie początkowe na podglądzie: strona (pierwsza / ostatnia / numer), narożnik, odległość od krawędzi [mm] | ostatnia strona, prawy dolny róg, 15 × 15 mm |
| Serwer znacznika czasu (TSA), login, hasło | `http://time.certum.pl`, bez logowania |
| PAdES B-LT (dołączenie OCSP/CRL do pliku) | wyłączone |
| Zapis jako nowy plik z przyrostkiem (zamiast zastąpienia oryginału) | wyłączony |
| Przyrostek nazwy pliku wynikowego | `_podpisany` |
| Minimalizacja do zasobnika systemowego | włączona |
| Uruchamianie przy logowaniu do Windows (autostart) | wyłączone |
| „Podpisz w PodpiszPdf” w menu kontekstowym plików PDF | włączone |
| Pozycja w nowym menu kontekstowym Windows 11 | wyłączona |
| Po otwarciu pliku PDF (gdy PodpiszPdf jest aplikacją domyślną): pytaj / podpisuj / przeglądaj | pytaj |
| Codzienne sprawdzanie aktualizacji | włączone |
| Minimalizuj po podpisaniu (bez okienka z podsumowaniem; dymek w zasobniku) | wyłączone |

Wizualizacja jest pisana **Arialem**: imię i nazwisko 11 pt (pogrubione), nagłówek i data 8 pt, stanowisko 7–9 pt
(jak największe, byle nie szersze od pozostałych wierszy). Rozmiar prostokąta wynika z treści; rysując podpis
na podglądzie, można go powiększyć lub pomniejszyć (0,6–3×) — całość skaluje się proporcjonalnie. Bez ramki
i z przezroczystym tłem (nie zasłania formularza).
Logo wektorowe (SVG lub PDF — pierwsza strona) trafia do podpisu jako grafika wektorowa, ostra przy każdym
powiększeniu. Napisy w SVG najlepiej zamienić na krzywe (w SVG bez osadzonych czcionek użyta zostanie czcionka zastępcza).
Logo PNG/JPG jest osadzane w oryginalnej rozdzielczości, bez stratnej kompresji. Białe lub przezroczyste marginesy
w pliku logo są pomijane (przycięcie w PDF, obraz pozostaje nienaruszony), więc logo zajmuje całe dostępne miejsce.
Stanowisko można podzielić na wiersze klawiszem Enter; bez tego zawija się samo (najwyżej do dwóch wierszy).

**Instalacja i autostart.** Instalator MSI (dla całego komputera) umieszcza program w `C:\Program Files\PodpiszPdf`,
dodaje skrót w menu Start i certyfikat PodpiszPdf do magazynu *Zaufane osoby* komputera (dla nowego menu Windows 11).
Cicha instalacja, np. przez zasady grupy: `msiexec /i PodpiszPdf.msi /qn`; odinstalowanie w *Ustawieniach Windows → Aplikacje*
(usuwa też — dla odinstalowującego użytkownika — pozycje w menu kontekstowym, rejestrację aplikacji do PDF i autostart;
ustawienia w `%APPDATA%\PodpiszPdf` zostają).
Autostart to wpis `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — program startuje zminimalizowany do zasobnika.
Kopia programu ze starszych wersji (`%APPDATA%\PodpiszPdf\PodpiszPdf.exe`) jest przy starcie usuwana.

Menu kontekstowe jest rejestrowane dla bieżącego użytkownika (`HKCU\Software\Classes\SystemFileAssociations\.pdf\shell\PodpiszPdf`),
bez uprawnień administratora; wpis wskazuje na zainstalowany program.

Hasło do TSA jest szyfrowane mechanizmem Windows DPAPI — odczyta je tylko ten sam użytkownik Windows.

**Data na wizualizacji.** Wizualizacja jest częścią podpisywanych danych, a znacznik czasu powstaje
dopiero po podpisie, więc tuż przed podpisem program pobiera aktualny czas z serwera znacznika czasu
(dodatkowe żądanie do TSA) i to on trafia na wizualizację; gdy serwer nie odpowie — czas komputera.
Wiążący czas zawsze zawiera znacznik czasu w podpisie.

## Budowanie

Wymagane .NET 10 SDK (i .NET 8 dla testów) na Windows.

```
dotnet test tests/PodpiszPdf.Tests
dotnet publish src/PodpiszPdf -c Release -r win-x64 -p:Platform=x64 -o publish
```

Wynik: folder `publish` z programem (nie wymaga instalacji .NET ani Windows App SDK; Windows 10 1809+/11, 64-bit).
GitHub Actions buduje z niego instalator `PodpiszPdf.msi` (WiX 5, `instalator/PodpiszPdf.wxs`) przy każdym pushu
(artefakt **PodpiszPdf** w zakładce *Actions*). Publikacja pomija nieużywane moduły Windows App SDK (AI/ML, widżety).

## Struktura

| Katalog | Zawartość |
|---|---|
| `src/PodpiszPdf.Core` | Logika: wybór certyfikatów, podpis PAdES (iText 9 + BouncyCastle), wizualizacja, znacznik czasu, podgląd stron (PDFium), komunikaty błędów |
| `src/PodpiszPdf` | Interfejs WinUI 3 (Windows App SDK, .NET 10): strona główna, podgląd z wyborem miejsca, ustawienia, zasobnik, menu kontekstowe |
| `tests/PodpiszPdf.Tests` | Testy na certyfikatach testowych (RSA i ECDSA) z lokalnym serwerem znacznika czasu — bez karty i bez sieci |
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

## Licencja

PodpiszPdf jest oprogramowaniem otwartym na licencji **GNU Affero General Public License v3.0** (AGPL-3.0),
pełny tekst w pliku [`LICENSE`](LICENSE). Copyright © 2026 Orinoko357.
Kod źródłowy: https://github.com/Orinoko357/PodpiszPdf

Program jest dostarczany „tak jak jest”, bez żadnej gwarancji (zob. punkty 15–16 licencji).

### Biblioteki i zasoby

- iText 9 — AGPL-3.0 (stąd licencja całego programu).
- PDFium (BSD/Apache 2.0, kompilacje bblanchon/pdfium-binaries, pakiety bblanchon.PDFium.*, Apache 2.0) — podgląd stron.
- Windows App SDK / WinUI 3, Windows Community Toolkit, H.NotifyIcon — MIT.
- Czcionka Arial z systemu Windows; gdy jej brak — wbudowana Liberation Sans (metrycznie zgodna z Arialem), licencja SIL OFL 1.1, zob. `src/PodpiszPdf.Core/Zasoby/LiberationSans-LICENSE.txt`.

## Wydania

Instalator `PodpiszPdf.msi` do pobrania jest w zakładce **Releases** repozytorium. Nowe wydanie powstaje:

*Actions → Wydanie → Run workflow*: wybrać gałąź i wpisać numer wersji (równy `<Version>`). Wydanie bierze gotowy,
przetestowany instalator z udanego przebiegu *Build* dla tego commita (bez ponownej budowy, ok. minuty).

Numer wersji programu ustawia się w `src/PodpiszPdf/PodpiszPdf.csproj` (`<Version>`).

Wydanie powstaje w zadaniu `wydanie`, które czeka na zatwierdzenie w środowisku GitHub **`wydanie`**
(*Settings → Environments*, wymagany zatwierdzający: właściciel). Sekrety:

| Sekret | Gdzie | Do czego |
|---|---|---|
| `CERTYFIKAT_PFX` | repozytorium | Certyfikat CN=PodpisPdf (PFX w Base64, hasło `PodpisPdf`) do podpisu pakietu menu Windows 11. Bez niego wydanie nie zawiera pakietu menu. |
| `KLUCZ_AKTUALIZACJI` | środowisko `wydanie` | Klucz prywatny ECDSA P-256 (PEM) do podpisu aktualizacji. Klucz zapasowy — tylko offline. Bez klucza nie ma wydania. |
| `TOKEN_WYDANIA` | środowisko `wydanie` | Token z prawem zapisu (*Contents: Read and write*) tylko do `Orinoko357/PodpiszPdf-wydania`. Bez niego wydanie nie trafia do repozytorium aktualizacji. |

Każde wydanie zawiera `PodpiszPdf.msi`, `PodpiszPdf.msi.sha256`, `PodpiszPdf.msi.sig` (podpis sprawdzany przez
aktualizację) i `PodpiszPdf.cer`.
