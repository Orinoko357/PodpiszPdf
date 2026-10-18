# Lista kroków — test z prawdziwą kartą kwalifikowaną

Przed testem:

- [ ] Sterownik producenta karty jest zainstalowany (np. proCertum CardManager, SimplySign Desktop, Szafir), a certyfikat jest widoczny w Windows:
      `Win+R` → `certmgr.msc` → *Osobisty* → *Certyfikaty*.
- [ ] W *Ustawieniach* programu jest adres serwera znacznika czasu (domyślnie `http://time.certum.pl`); przycisk **Sprawdź połączenie** pokazuje „Połączenie … działa”.
- [ ] Przygotuj testowy plik PDF, np. wniosek z SZUP.

## 1. Lista certyfikatów

- [ ] Po uruchomieniu `PodpiszPdf.exe` na liście jest certyfikat z karty: imię i nazwisko, wystawca, data ważności.
- [ ] Certyfikat kwalifikowany ma gwiazdkę ★ i pogrubienie.
- [ ] Na liście **nie ma** certyfikatów bez klucza prywatnego, przeterminowanych ani służących tylko do logowania/szyfrowania.
- [ ] Wyjmij kartę, kliknij **Odśwież listę**: certyfikat znika albo podpis zakończy się komunikatem o braku karty (niektóre sterowniki zostawiają certyfikat w magazynie).

## 2. Podpis

- [ ] Przeciągnij plik PDF do okna. Wybierz certyfikat, kliknij **Podpisz**.
- [ ] Otwiera się podgląd wszystkich stron; podpis jest wstępnie w położeniu z ustawień. Przewiń, kliknij inne
      miejsce (także na innej stronie) — prostokąt podpisu przenosi się tam. Kliknij **Podpisz**.
- [ ] Podgląd bez zakładek trybu: kliknięcie umieszcza podpis (na start imię i nazwisko 11 pt), przeciągnięcie po stronie rysuje nowy rozmiar; podpis da się przesunąć, a każdy z 8 uchwytów (narożniki i środki boków) zmienia rozmiar z zachowaniem proporcji.
- [ ] Przeciągnij myszą na stronie — rysuje się prostokąt o stałych proporcjach z rozmiarem w mm; po puszczeniu
      przycisku podpis ma ten rozmiar. Kolejne kliknięcie (i następny dokument) używa ostatniego rozmiaru.
- [ ] W pliku wynikowym podpis jest dokładnie w wskazanym miejscu, bez ramki; logo jest ostre przy dużym powiększeniu.
- [ ] Pojawia się okno PIN-u **sterownika karty** (nie programu PodpiszPdf), na wierzchu.
- [ ] Po wpisaniu PIN-u: komunikat o powodzeniu, podpisany dokument zastępuje oryginał (bez dodatkowych plików).
- [ ] Ustawienia → *Zapisuj jako nowy plik z przyrostkiem* włączone: obok oryginału powstaje `…_podpisany.pdf`, oryginał bez zmian.
- [ ] Plik otwarty w Acrobacie podczas podpisu (zastępowanie oryginału) → zrozumiały komunikat, oryginał nienaruszony.
- [ ] Wizualizacja na stronie: imię i nazwisko, stanowisko (jeśli ustawione), logo (jeśli ustawione), data i godzina.
- [ ] Przy kolejnym uruchomieniu zaznaczony jest ostatnio użyty certyfikat.

## 3. Menu kontekstowe i zasobnik

- [ ] Prawy przycisk na pliku PDF w Eksploratorze → **Podpisz w PodpiszPdf** (Windows 11: *Pokaż więcej opcji*) —
      program otwiera podgląd tego pliku.
- [ ] Zminimalizuj program — znika z paska zadań, ikona jest obok zegara. Podpisz plik z menu kontekstowego —
      otwiera się działające okno (nie druga kopia programu).
- [ ] Prawy przycisk na ikonie w zasobniku → *Ustawienia…* otwiera okno na stronie ustawień.
- [ ] Ustawienia → *Wygląd podpisu*: każdy z 6 motywów zmienia podgląd w ustawieniach; podpis w wybranym motywie wygląda jak podgląd.
- [ ] Dwuklik na ikonie w zasobniku przywraca okno; *Zakończ* w menu ikony zamyka program.
- [ ] *Zakończ* (albo X) w trakcie podpisu — komunikat „Trwa praca programu”, program się nie zamyka; przy otwartym
      podglądzie — pytanie, czy zamknąć.
- [ ] Okno schowane w zasobniku przy otwartych ustawieniach: skrót z menu Start pokazuje okno.
- [ ] Zaznacz kilka plików PDF i otwórz je naraz (Enter), gdy program nie działa — jedno okno, pliki podpisują się
      po kolei, bez ostrzeżenia o „starszej wersji”.
- [ ] Ustawienia → *Minimalizuj po podpisaniu* włączone: po podpisie nie ma okienka z podsumowaniem, okno chowa się do zasobnika, a dymek pokazuje nazwę podpisanego pliku.
      Przy wyłączonym *Minimalizuj do zasobnika* opcja jest wyszarzona; gdy serwer znacznika czasu nie odpowiedział,
      okienko z podsumowaniem (czas z zegara komputera) pojawia się zawsze.
- [ ] Certyfikat niekwalifikowany → przed podglądem pytanie „Ten certyfikat nie jest kwalifikowany…”.
- [ ] Wyłączenie opcji menu kontekstowego w ustawieniach usuwa pozycję z menu.

## 4. Autostart i aktualizacja

- [ ] W *Ustawieniach* zaznacz „Uruchamiaj przy logowaniu do Windows” → OK. W `%APPDATA%\PodpiszPdf` jest
      `PodpiszPdf.exe`; w *Menedżerze zadań → Autostart* jest PodpiszPdf.
- [ ] Wyloguj się i zaloguj — program działa w zasobniku (bez okna). Menu kontekstowe pliku PDF otwiera go.
- [ ] Gdy program działa w zasobniku, uruchom pobrany plik **nowszej** wersji — pojawia się ostrzeżenie
      z pytaniem o zamknięcie działającego programu. *Tak* → otwiera się nowa wersja (numer w tytule okna),
      a plik w `%APPDATA%\PodpiszPdf` ma nową wersję (*Właściwości → Szczegóły*).
- [ ] Uruchom **starszą** wersję, gdy działa nowsza — ostrzeżenie, że działa nowsza wersja; plik w AppData się nie zmienia.
- [ ] Odznacz autostart → wpis znika z *Menedżera zadań → Autostart*.

## 4a. Wersja 3.0: aplikacja domyślna, menu Windows 11, aktualizacje

- [ ] *Ustawienia → Program → Ustaw jako domyślną…* → w Ustawieniach Windows wybierz PodpiszPdf dla `.pdf`.
      Pliki PDF zachowują ikonę Acrobata.
- [ ] Dwuklik pliku PDF → okno **Podpisz / Przeglądaj**. *Przeglądaj* otwiera Acrobat Reader; *Podpisz* otwiera podgląd
      do wskazania miejsca podpisu (także gdy program działa w zasobniku).
- [ ] Zaznacz „Zapamiętaj mój wybór” → kolejne otwarcie PDF działa bez pytania; zmiana w *Ustawienia → Program → Po otwarciu pliku PDF*.
- [ ] Zaznacz „Zapamiętaj mój wybór”, gdy program działa w zasobniku, i podpisz plik — wybór nadal jest zapamiętany
      (Ustawienia → Program → Po otwarciu pliku PDF).
- [ ] (Windows 11) *Ustawienia → Program → Nowe menu kontekstowe Windows 11*: *Dodaj do menu* (certyfikat dodaje
      instalator MSI; bez niego opis każe zainstalować program ponownie). Prawy przycisk na PDF → **Podpisz w PodpiszPdf** od razu w menu, bez *Pokaż więcej opcji*;
      działa także, gdy program nie jest uruchomiony i gdy jest w zasobniku. *Usuń z menu* usuwa pozycję.
- [ ] *Ustawienia → Informacje → Sprawdź teraz*: przy najnowszej wersji — „Masz najnowszą wersję”.
- [ ] Ze starszą wersją 3.x w `%APPDATA%\PodpiszPdf`: pasek „Dostępna nowa wersja” → *Zaktualizuj teraz* → program
      uruchamia się ponownie w nowej wersji (numer w tytule), na pasku stanu „Zaktualizowano PodpiszPdf…”.
- [ ] *Zaktualizuj teraz* → *Nie* w oknie UAC: program wraca w dotychczasowej wersji z komunikatem „Aktualizacja
      anulowana”; inny błąd instalatora — „Aktualizacja nie powiodła się (kod …)”. Folder
      `%TEMP%\PodpiszPdf-aktualizacja` znika przy następnym starcie.
- [ ] *Ustawienia → Znacznik czasu*: podany login przy adresie `http://` — zapis odmawia („adres musi zaczynać się od https://”).
- [ ] Odinstaluj PodpiszPdf (*Ustawienia Windows → Aplikacje*): znika „Podpisz w PodpiszPdf” z obu menu kontekstowych,
      PodpiszPdf z listy aplikacji do PDF i autostart; dwuklik na PDF (jeśli PodpiszPdf był domyślny) pyta o aplikację.
      `%APPDATA%\PodpiszPdf` (ustawienia, logo) zostaje — usuń ręcznie, jeśli niepotrzebny. Sprzątanie dotyczy
      użytkownika, który odinstalowuje.

## 5. Błędy

- [ ] Zły PIN → komunikat „Nieprawidłowy PIN…” (nie wpisuj złego PIN-u więcej niż raz — grozi blokadą karty).
- [ ] *Anuluj* w oknie PIN-u → „Podpisywanie zostało anulowane”.
- [ ] Brak karty w czytniku → komunikat o braku karty.
- [ ] Wyłączona sieć (lub zły adres TSA) → komunikat o niedostępnym serwerze znacznika czasu; oryginał się nie zmienia.
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

## 7. macOS (karta i czytnik na Macu)

- [ ] Zainstaluj oprogramowanie karty dla macOS (np. proCertum CardManager), włóż kartę. Otwórz
      `PodpiszPdf-macOS-AppleSilicon.dmg` (albo `-Intel`), przeciągnij PodpiszPdf do Aplikacji, uruchom
      (prawy przycisk → „Otwórz” przy pierwszym razie).
- [ ] Lista certyfikatów: certyfikat kwalifikowany z karty z dopiskiem „karta …”. Gdy pusta — *Ustawienia → Karta*:
      „Znalezione biblioteki” i ewentualnie *Wskaż bibliotekę…* (zanotuj ścieżkę biblioteki — dopiszemy ją do
      wyszukiwania automatycznego).
- [ ] Podpis: podgląd, wskazanie miejsca (kliknięcie, rysowanie, przesuwanie, 8 uchwytów), okno PIN-u, wynik
      „Dokument został podpisany” → *Pokaż w Finderze*.
- [ ] Zły PIN (raz!) → „Błędny PIN.” i ponowne okno PIN-u; *Anuluj* → „Podpisywanie anulowane.”
- [ ] Finder: plik PDF → *Otwórz za pomocą → PodpiszPdf* → od razu podgląd do podpisu.
- [ ] Podpisany plik w weryfikatorze (punkt 6) — PAdES-BASELINE-T, QES.
