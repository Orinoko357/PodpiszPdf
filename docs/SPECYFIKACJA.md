# PodpisPdf — specyfikacja

Mała aplikacja na Windows do składania **kwalifikowanego podpisu elektronicznego** pod dokumentami **PDF**.
Podpis zawiera **znacznik czasu** i **widoczną wizualizację** na stronie dokumentu.

Typowe zastosowanie w SZUP: podpisywanie wniosków PDF z polem „Podpis kwalifikowany złożony poza systemem”.

---

## 1. Zakres

| Jest w zakresie | Poza zakresem |
|---|---|
| Podpisywanie plików PDF (PAdES) | Inne formaty (XAdES, CAdES, ASiC) |
| Znacznik czasu, zawsze dodawany | Podpis bez znacznika czasu |
| Widoczna wizualizacja podpisu | Weryfikacja podpisów |
| Certyfikaty z karty lub chmury widoczne w Windows | Obsługa kluczy z plików (.pfx) w wersji produkcyjnej |

**Ważne:** aplikacja sama nie sprawia, że podpis jest kwalifikowany. Kwalifikowany jest wtedy, gdy klucz prywatny leży na certyfikowanym urządzeniu (karta kryptograficzna albo usługa chmurowa, np. Certum, SimplySign, Szafir), a certyfikat jest kwalifikowany. Aplikacja ma zapisać podpis w formacie zgodnym z normami UE, żeby przeszedł weryfikację (weryfikator na gov.pl, Adobe Reader, EU DSS).

---

## 2. Jak to działa dla użytkownika

1. Użytkownik uruchamia `PodpisPdf.exe` i przeciąga plik PDF do okna (albo wybiera go przyciskiem).
2. Wybiera certyfikat z listy.
3. Klika **Podpisz**; otwiera się podgląd wszystkich stron, na którym kliknięciem wskazuje miejsce podpisu.
4. Sterownik karty pokazuje własne okno do wpisania PIN-u. Aplikacja nie widzi PIN-u.
5. Aplikacja pobiera znacznik czasu i zapisuje wynik obok oryginału: `wniosek.pdf` → `wniosek_podpisany.pdf`. Oryginał zostaje bez zmian.
6. Na koniec wyświetla komunikat o powodzeniu albo zrozumiały opis błędu, np. brak karty, zły PIN, serwer znacznika czasu niedostępny.

---

## 3. Wybór certyfikatu

- Źródłem jest **osobisty magazyn certyfikatów Windows** („Osobisty”). Trafiają tam certyfikaty z kart i z SimplySign Desktop, gdy sterownik producenta jest zainstalowany. Nie trzeba konfigurować bibliotek PKCS#11.
- Lista pokazuje tylko certyfikaty, które:
  - mają klucz prywatny,
  - są ważne w dniu podpisu,
  - służą do podpisu (użycie klucza *nonRepudiation*).
- Certyfikaty kwalifikowane (oznaczenie *QcCompliance* w certyfikacie) są wyróżnione na liście.
- Przy każdej pozycji widać imię i nazwisko, wystawcę i datę ważności.
- Aplikacja zapamiętuje ostatnio użyty certyfikat.

---

## 4. Podpis

| Parametr | Wartość |
|---|---|
| Format | **PAdES Baseline B-T** (ETSI EN 319 142), czyli podpis ze znacznikiem czasu |
| Skrót | SHA-256 |
| Algorytmy klucza | RSA i ECDSA (zależnie od karty) |
| Znacznik czasu | RFC 3161. Adres serwera (TSA) jest konfigurowalny, opcjonalnie z loginem i hasłem (kwalifikowane TSA są zwykle płatne) |
| Zapis | Przyrostowy, więc wcześniejsze podpisy w pliku pozostają ważne |

Opcjonalnie, do decyzji: poziom **B-LT**, czyli dołączenie do pliku danych OCSP/CRL, żeby podpis dało się zweryfikować także po latach.

---

## 5. Wizualizacja podpisu

Prostokąt o stałym rozmiarze (75 × 22 mm), bez ramki i z przezroczystym tłem, w miejscu wskazanym kliknięciem na podglądzie. Zawiera:

| Element | Źródło | Obowiązkowy |
|---|---|---|
| **Nagłówek „Podpisano elektronicznie przez:”** | Stały tekst na samej górze, przed imieniem i nazwiskiem | tak |
| **Imię i nazwisko** (zawsze pogrubione) | Z certyfikatu (pola *GivenName* i *Surname*; jeśli ich brak, pole *CN*) | tak |
| **Stanowisko** | Wpisane w ustawieniach programu; Enter wymusza podział wiersza, bez tego zawija się samo do dwóch wierszy | nie |
| **Logo** | Plik SVG lub PDF (wektorowe) albo PNG/JPG wskazany w ustawieniach; kopia zapisywana w folderze ustawień; grafika wektorowa pozostaje wektorowa, rastrowa w oryginalnej rozdzielczości; puste marginesy pomijane | nie |
| **Data i godzina** | Czas złożenia podpisu bez sekund (RRRR-MM-DD GG:MM), patrz uwaga niżej | tak |

Pusty element (np. brak logo) znika, a układ dopasowuje się do reszty. Czcionka obsługuje polskie znaki.

> **Uwaga o dacie:** wizualizacja jest częścią podpisywanych danych, a znacznik czasu powstaje dopiero po złożeniu podpisu. Dlatego na wizualizacji nie da się pokazać czasu identycznego z czasem znacznika. Proponowane rozwiązanie: tuż przed podpisem aplikacja pobiera aktualny czas z serwera znacznika czasu i to on trafia na wizualizację (jeśli serwer nie odpowie, używa zegara komputera). Różnica względem właściwego znacznika to zwykle kilka sekund, tyle ile trwa wpisanie PIN-u. Wiążący czas zawsze zawiera znacznik czasu w podpisie.

**Docelowy wygląd zostanie dopasowany do wzoru od użytkownika (do dostarczenia).**

---

## 6. Ustawienia programu

Zapisywane w pliku `%APPDATA%\PodpisPdf\config.json` i edytowane w oknie „Ustawienia”:

- stanowisko (tekst, opcjonalny),
- logo (SVG, PDF, PNG lub JPG, opcjonalne) — kopia pliku zapisywana jako `%APPDATA%\PodpisPdf\logo.*`,
- adres serwera znacznika czasu oraz opcjonalnie login i hasło,
- położenie początkowe wizualizacji na podglądzie: strona (pierwsza, ostatnia albo numer), narożnik i odległość od krawędzi,
- minimalizacja do zasobnika systemowego, pozycja „Podpisz w PodpisPdf” w menu kontekstowym plików PDF,
- przyrostek nazwy pliku wynikowego (domyślnie `_podpisany`).

---

## 7. Technologia

- **C# / .NET 8, WinForms.** Wynikiem jest jeden plik `PodpisPdf.exe` (self-contained), który nie wymaga instalacji .NET.
- **iText 9 + BouncyCastle** do podpisu PAdES. Licencja AGPL: przy użytku wewnętrznym albo udostępnieniu kodu źródłowego nie ma problemu.
- Dostęp do klucza przez mechanizmy kryptograficzne Windows (CNG/CSP), czyli przez sterownik producenta karty.
- System: Windows 10 lub 11, 64-bit.

---

## 8. Testy i odbiór

- Logika podpisu jest sprawdzana na **certyfikacie testowym** (plik PFX) bez karty.
- Podpisany plik musi przejść weryfikację w:
  - Adobe Acrobat Reader (podpis i znacznik czasu poprawne),
  - weryfikatorze EU DSS albo na gov.pl (format PAdES-BASELINE-T).
- Test z prawdziwą kartą kwalifikowaną wykonuje użytkownik według dostarczonej listy kroków.

---

## 9. Otwarte kwestie

1. **Wzór wizualizacji** — do dostarczenia przez użytkownika.
2. ~~**Położenie podpisu**~~ — rozstrzygnięte: wybór miejsca kliknięciem na podglądzie wszystkich stron.
3. **Serwer znacznika czasu** — który TSA (darmowy, np. Certum, czy kwalifikowany płatny z kontem)?
4. **Poziom B-LT** — czy jest potrzebny do archiwizacji?
5. **Jakich kart i usług używacie** (Certum, SimplySign, Szafir…)? Od tego zależy lista kroków do testów.
