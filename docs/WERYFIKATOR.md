# Weryfikator

Osobny, mały program dla Windows 10/11: sprawdza, czy podpisy elektroniczne w grupie plików są ważne
i czy numery PESEL podpisujących są na liście z arkusza `.xlsx`.

Jeden plik `Weryfikator.exe` (ok. 120 KB), **bez instalacji** — wystarczy go skopiować i uruchomić.
Korzysta z .NET Framework 4.8, który jest częścią Windows 10 i 11, więc niczego nie trzeba doinstalowywać.
(Instalator nie dawałby tu żadnej korzyści: program nie ma bibliotek, usług ani wpisów w rejestrze.)

## Jak używać

1. **Dodaj pliki…** / **Dodaj folder…** (z podfolderami) albo przeciągnij pliki na okno.
2. **Lista PESEL (xlsx)…** — arkusz z numerami PESEL. Program czyta wszystkie arkusze i wszystkie komórki,
   więc układ kolumn jest dowolny; radzi sobie z numerami zapisanymi jako liczby (zgubione zero na początku).
   Numery z błędną sumą kontrolną są pomijane i zgłaszane. Ostatnio użyta lista jest pamiętana.
   **Podgląd listy…** pokazuje zawartość arkusza i pozwala wskazać arkusz oraz kolumnę z numerami PESEL
   (lista rozwijana albo kliknięcie nagłówka kolumny) — wtedy numery z innych kolumn nie są brane pod uwagę.
   Zielone komórki to numery użyte do porównania, czerwone — 11 cyfr z błędną sumą kontrolną.
3. **Sprawdź podpisy** — każdy podpis to jeden wiersz: ważność, rodzaj podpisu, podpisujący, PESEL,
   czy PESEL jest na liście, czas podpisu i uwagi. Kolumna „Wynik”: **OK** = podpis ważny i PESEL na liście.
4. **Zapisz raport…** — plik CSV otwierany w Excelu.

Dwuklik na wierszu otwiera plik. Bez listy PESEL program sprawdza samą ważność podpisów.

## Co jest sprawdzane

| Rodzaj podpisu | Jak rozpoznawany | Skąd PESEL |
|---|---|---|
| **Kwalifikowany** | certyfikat kwalifikowany wystawiony przez urząd z listy zaufania UE | pole `serialNumber` certyfikatu (`PNOPL-…`, dawniej `PESEL: …`) |
| **Zaufany** (profil zaufany) | kwalifikowana pieczęć „…pieczęć podpisu zaufanego” (urząd z listy zaufania UE) | PDF: wpis „Powód” podpisu („…w imieniu: IMIĘ NAZWISKO, PESEL: …”); XML: dane osoby w podpisanej części podpisu |
| **Osobisty** (e-dowód) | certyfikat z urzędu e-dowodu (pl.ID), zaufany certyfikat główny | pole `serialNumber` certyfikatu |

Formaty: **PDF** (PAdES — także wiele podpisów w jednym pliku), **XML/XAdES** (`.xml`, `.xades`, również podpis
pliku leżącego obok), **CAdES** (`.sig`, `.p7s`, `.p7m` — podpis wewnętrzny albo zewnętrzny pliku obok).

Dla każdego podpisu program sprawdza:

- integralność — czy dokument nie został zmieniony po podpisaniu (w PDF także dopisanie danych po podpisie),
- ścieżkę certyfikacji do urzędu z listy zaufania UE i status usługi tego urzędu w czasie podpisu,
- ważność certyfikatu w czasie podpisu (z kwalifikowanego znacznika czasu, a gdy go nie ma — z czasu podanego
  w podpisie; jeśli certyfikat już wygasł, a znacznika nie ma, wynik to „DO WYJAŚNIENIA”),
- unieważnienie certyfikatu (CRL/OCSP, przez mechanizmy Windows; można wyłączyć).

Wyniki ważności: **WAŻNY**, **NIEWAŻNY**, **DO WYJAŚNIENIA** (nie da się jednoznacznie potwierdzić — powód
jest w uwagach), **BRAK PODPISU**.

## Lista zaufania UE

Przy starcie (i nie częściej niż co 12 godzin) program pobiera listę list zaufania z serwera Komisji Europejskiej
(`https://ec.europa.eu/tools/lotl/eu-lotl.xml`) i wskazane w niej listy krajowe wszystkich państw UE/EOG.
Sprawdza podpis każdej listy (lista krajowa musi być podpisana certyfikatem wskazanym w liście unijnej).
Kopia jest w `%LOCALAPPDATA%\Weryfikator\lista-zaufania.txt` — przy braku internetu używana jest ostatnia pobrana.
Odnośnik „Lista zaufania UE… — odśwież” pobiera ją ponownie.

## Podpis osobisty — zaufane certyfikaty

Podpis osobisty nie jest kwalifikowany, więc urzędu e-dowodu nie ma na liście zaufania UE. Program uznaje go, gdy
certyfikat główny urzędu jest zaufany w Windows albo leży w folderze otwieranym odnośnikiem
**„Zaufane certyfikaty (podpis osobisty)…”** (`%LOCALAPPDATA%\Weryfikator\Zaufane certyfikaty`; pliki `.cer`, `.crt`, `.pem`).
Certyfikaty urzędu e-dowodu (główny i pośrednie) pobierz ze strony <https://www.gov.pl/web/e-dowod>.
Bez nich podpis osobisty dostaje wynik „DO WYJAŚNIENIA” z informacją, którego certyfikatu brakuje.
Wersja przenośna: te same pliki mogą leżeć w folderze `certyfikaty` obok `Weryfikator.exe`.

## Tryb bez okna

```
Weryfikator.exe --sprawdz <folder lub pliki…> [--pesel lista.xlsx] [--kolumna B] [--raport raport.csv] [--bez-crl]
```

Kod wyjścia: 0 — wszystko w porządku, 1 — są pozycje do sprawdzenia, 2 — błąd.

## Ograniczenia

- Program jest pomocą przy wstępnej kontroli dokumentów, nie kwalifikowaną usługą walidacji. Wynik „WAŻNY” oznacza
  pozytywny wynik opisanych wyżej sprawdzeń. W razie sporu rozstrzyga kwalifikowana usługa walidacji.
- Podpis zaufany w PDF potwierdzono na prawdziwym dokumencie. Podpis zaufany w XML oraz **podpis osobisty**
  przetestowano tylko na plikach testowych o przyjętej budowie — przed użyciem warto sprawdzić po jednym
  prawdziwym dokumencie.
- Unieważnienie sprawdzane jest według stanu bieżącego: certyfikat unieważniony po złożeniu podpisu daje wynik
  „DO WYJAŚNIENIA” (ze znacznikiem czasu) albo „NIEWAŻNY” (bez znacznika).
- Podpis LOTL sprawdzany jest względem certyfikatów wskazanych w samej liście (pobranej po HTTPS z serwera Komisji).
- Stary format `.xls` nie jest obsługiwany (zapisz listę jako `.xlsx`).

## Budowanie i testy

- `dotnet test tests/Weryfikator.Tests` — logika (`src/Weryfikator/Logika`) kompilowana dla .NET 8 (Linux/Windows),
  a na Windows także dla .NET Framework 4.8.
- `dotnet build src/Weryfikator -c Release` — `Weryfikator.exe` (kompiluje się też na Linuksie).
- Workflow `.github/workflows/weryfikator.yml`: testy, test gotowego exe na próbkach, pobranie prawdziwej listy
  zaufania UE, zrzut okna na gałąź `zrzuty-weryfikator`; nowa wersja (`<Version>` w `Weryfikator.csproj` bez
  znacznika `weryfikator-v<wersja>`) jest wydawana po zatwierdzeniu w środowisku `wydanie`.
