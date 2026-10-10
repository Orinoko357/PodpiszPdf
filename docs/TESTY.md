# Lista kroków — test z prawdziwą kartą kwalifikowaną

Przed testem:

- [ ] Sterownik producenta karty jest zainstalowany (np. proCertum CardManager, SimplySign Desktop, Szafir), a certyfikat jest widoczny w Windows:
      `Win+R` → `certmgr.msc` → *Osobisty* → *Certyfikaty*.
- [ ] W *Ustawieniach* programu jest adres serwera znacznika czasu (domyślnie `http://time.certum.pl`); przycisk **Sprawdź połączenie** pokazuje „Połączenie … działa”.
- [ ] Przygotuj testowy plik PDF, np. wniosek z SZUP.

## 1. Lista certyfikatów

- [ ] Po uruchomieniu `PodpisPdf.exe` na liście jest certyfikat z karty: imię i nazwisko, wystawca, data ważności.
- [ ] Certyfikat kwalifikowany ma gwiazdkę ★ i pogrubienie.
- [ ] Na liście **nie ma** certyfikatów bez klucza prywatnego, przeterminowanych ani służących tylko do logowania/szyfrowania.
- [ ] Wyjmij kartę, kliknij **Odśwież listę**: certyfikat znika albo podpis zakończy się komunikatem o braku karty (niektóre sterowniki zostawiają certyfikat w magazynie).

## 2. Podpis

- [ ] Przeciągnij plik PDF do okna. Wybierz certyfikat, kliknij **Podpisz**.
- [ ] Otwiera się podgląd wszystkich stron; podpis jest wstępnie w położeniu z ustawień. Przewiń, kliknij inne
      miejsce (także na innej stronie) — prostokąt podpisu przenosi się tam. Kliknij **Podpisz**.
- [ ] Przeciągnij myszą na stronie — rysuje się prostokąt o stałych proporcjach z rozmiarem w mm; po puszczeniu
      przycisku podpis ma ten rozmiar. Kolejne kliknięcie (i następny dokument) używa ostatniego rozmiaru.
- [ ] W pliku wynikowym podpis jest dokładnie w wskazanym miejscu, bez ramki; logo jest ostre przy dużym powiększeniu.
- [ ] Pojawia się okno PIN-u **sterownika karty** (nie programu PodpisPdf), na wierzchu.
- [ ] Po wpisaniu PIN-u: komunikat o powodzeniu, obok oryginału powstaje `…_podpisany.pdf`; oryginał nie zmienia się (data modyfikacji).
- [ ] Wizualizacja na stronie: imię i nazwisko, stanowisko (jeśli ustawione), logo (jeśli ustawione), data i godzina.
- [ ] Przy kolejnym uruchomieniu zaznaczony jest ostatnio użyty certyfikat.

## 3. Menu kontekstowe i zasobnik

- [ ] Prawy przycisk na pliku PDF w Eksploratorze → **Podpisz w PodpisPdf** (Windows 11: *Pokaż więcej opcji*) —
      program otwiera podgląd tego pliku.
- [ ] Zminimalizuj program — znika z paska zadań, ikona jest obok zegara. Podpisz plik z menu kontekstowego —
      otwiera się działające okno (nie druga kopia programu).
- [ ] Dwuklik na ikonie w zasobniku przywraca okno; *Zakończ* w menu ikony zamyka program.
- [ ] Wyłączenie opcji menu kontekstowego w ustawieniach usuwa pozycję z menu.

## 4. Autostart i aktualizacja

- [ ] W *Ustawieniach* zaznacz „Uruchamiaj przy logowaniu do Windows” → OK. W `%APPDATA%\PodpisPdf` jest
      `PodpisPdf.exe`; w *Menedżerze zadań → Autostart* jest PodpisPdf.
- [ ] Wyloguj się i zaloguj — program działa w zasobniku (bez okna). Menu kontekstowe pliku PDF otwiera go.
- [ ] Gdy program działa w zasobniku, uruchom pobrany plik **nowszej** wersji — pojawia się ostrzeżenie
      z pytaniem o zamknięcie działającego programu. *Tak* → otwiera się nowa wersja (numer w tytule okna),
      a plik w `%APPDATA%\PodpisPdf` ma nową wersję (*Właściwości → Szczegóły*).
- [ ] Uruchom **starszą** wersję, gdy działa nowsza — ostrzeżenie, że działa nowsza wersja; plik w AppData się nie zmienia.
- [ ] Odznacz autostart → wpis znika z *Menedżera zadań → Autostart*.

## 5. Błędy

- [ ] Zły PIN → komunikat „Nieprawidłowy PIN…” (nie wpisuj złego PIN-u więcej niż raz — grozi blokadą karty).
- [ ] *Anuluj* w oknie PIN-u → „Podpisywanie zostało anulowane”.
- [ ] Brak karty w czytniku → komunikat o braku karty.
- [ ] Wyłączona sieć (lub zły adres TSA) → komunikat o niedostępnym serwerze znacznika czasu; plik `_podpisany.pdf` nie powstaje.
- [ ] Plik wynikowy otwarty w Adobe Reader podczas ponownego podpisu → komunikat, że plik jest otwarty w innym programie.

## 6. Weryfikacja podpisanego pliku

- [ ] **Adobe Acrobat Reader**: panel *Podpisy* — „Podpis jest ważny”, „Podpis zawiera osadzony znacznik czasu”.
      (Jeśli Reader pisze, że tożsamość jest nieznana: *Edycja → Preferencje → Podpisy → Weryfikacja → Więcej…* →
      zaznacz *Sprawdzanie poprawności przy użyciu certyfikatów Windows* oraz zaufane listy EUTL.)
- [ ] **Weryfikator gov.pl** (https://www.gov.pl/web/gov/zweryfikuj-podpisany-dokument) lub **EU DSS**
      (https://ec.europa.eu/digital-building-blocks/DSS/webapp-demo/validation): format **PAdES-BASELINE-T**
      (lub **PAdES-BASELINE-LT**, gdy włączono tę opcję), podpis kwalifikowany (QES), wynik *TOTAL_PASSED*.
- [ ] Podpisz już podpisany plik drugi raz (np. drugą osobą): oba podpisy są ważne.

Wyniki (zrzuty ekranu z Adobe i z raportu weryfikatora) prosimy odesłać wraz z nazwą karty/usługi i wersją sterownika.
