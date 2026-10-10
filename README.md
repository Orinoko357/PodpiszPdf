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
   się znaleźć podpis (rozmiar wizualizacji jest stały), i zatwierdź przyciskiem **Podpisz**.
4. Wpisz PIN w oknie sterownika karty (program nie widzi PIN-u).
5. Wynik zapisuje się obok oryginału: `wniosek.pdf` → `wniosek_podpisany.pdf`. Oryginał zostaje bez zmian.

**Z Eksploratora Windows:** prawy przycisk myszy na pliku PDF → **Podpisz w PodpisPdf** (w Windows 11:
*Pokaż więcej opcji*). Program otwiera od razu podgląd do wskazania miejsca podpisu. Jeśli program już działa
(np. w zasobniku), plik trafia do działającego okna.

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

Wizualizacja ma stały rozmiar **85 × 25 mm**, bez ramki i z przezroczystym tłem (nie zasłania formularza).
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

Wymagane .NET 8 SDK na Windows.

```
dotnet test tests/PodpisPdf.Tests
dotnet publish src/PodpisPdf -c Release -r win-x64 -o publish
```

Wynik: `publish\PodpisPdf.exe` — jeden plik, nie wymaga instalacji .NET (Windows 10/11, 64-bit).
GitHub Actions buduje ten plik przy każdym pushu (artefakt **PodpisPdf** w zakładce *Actions*).

## Struktura

| Katalog | Zawartość |
|---|---|
| `src/PodpisPdf.Core` | Logika: wybór certyfikatów, podpis PAdES (iText 9 + BouncyCastle), wizualizacja, znacznik czasu, podgląd stron (PDFium), komunikaty błędów |
| `src/PodpisPdf` | Okno programu (WinForms): podgląd z wyborem miejsca, ustawienia, zasobnik, menu kontekstowe |
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
- Docnet.Core (MIT) z silnikiem PDFium (BSD/Apache 2.0) — podgląd stron.
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
