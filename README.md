# PodpisPdf

Mała aplikacja na Windows do składania **kwalifikowanego podpisu elektronicznego** pod dokumentami PDF
(format **PAdES Baseline B-T**: podpis ze znacznikiem czasu i widoczną wizualizacją na stronie).
Typowe zastosowanie: podpisywanie wniosków z SZUP („Podpis kwalifikowany złożony poza systemem”).

Specyfikacja: [docs/SPECYFIKACJA.md](docs/SPECYFIKACJA.md).

## Jak używać

1. Uruchom `PodpisPdf.exe` i przeciągnij plik PDF do okna (albo kliknij **Wybierz plik…**).
   Plik można też przeciągnąć na ikonę `PodpisPdf.exe`.
2. Wybierz certyfikat z listy (★ — certyfikat kwalifikowany).
3. Kliknij **Podpisz** i wpisz PIN w oknie sterownika karty (program nie widzi PIN-u).
4. Wynik zapisuje się obok oryginału: `wniosek.pdf` → `wniosek_podpisany.pdf`. Oryginał zostaje bez zmian.

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
| Logo PNG/JPG (na wizualizacji) | brak |
| Strona z podpisem: pierwsza / ostatnia / numer | ostatnia |
| Położenie: narożnik strony i odległość od krawędzi [mm] | prawy dolny, 15 × 15 mm |
| Rozmiar wizualizacji [mm] | 75 × 22 mm |
| Serwer znacznika czasu (TSA), login, hasło | `http://time.certum.pl`, bez logowania |
| PAdES B-LT (dołączenie OCSP/CRL do pliku) | wyłączone |
| Przyrostek nazwy pliku wynikowego | `_podpisany` |

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
| `src/PodpisPdf.Core` | Logika: wybór certyfikatów, podpis PAdES (iText 9 + BouncyCastle), wizualizacja, znacznik czasu, komunikaty błędów |
| `src/PodpisPdf` | Okno programu (WinForms) i ustawienia |
| `tests/PodpisPdf.Tests` | Testy na certyfikatach testowych (RSA i ECDSA) z lokalnym serwerem znacznika czasu — bez karty i bez sieci |
| `tools/PodpisTestowy` | Narzędzie deweloperskie: podpis certyfikatem z pliku PFX do sprawdzenia formatu w Adobe/EU DSS |
| `docs/TESTY.md` | Lista kroków testu z prawdziwą kartą |

Podpis próbny z certyfikatem testowym (wymaga dostępu do TSA):

```
dotnet run --project tools/PodpisTestowy -- utworz-pfx test.pfx haslo "Jan Kowalski"
dotnet run --project tools/PodpisTestowy -- podpisz wniosek.pdf test.pfx haslo
```

## Ograniczenia

- Wizualizacja na stronach obróconych (/Rotate) nie jest obracana razem ze stroną.
- Pliki PDF zabezpieczone hasłem trzeba najpierw odbezpieczyć.

## Licencje

- iText 9 — AGPL. Użytek wewnętrzny albo udostępnienie kodu źródłowego nie stanowi problemu.
- Czcionka DejaVu Sans (wbudowana, obsługa polskich znaków) — licencja Bitstream Vera, zob. `src/PodpisPdf.Core/Zasoby/DejaVu-LICENSE.txt`.
