using System;
using System.Collections.Generic;

namespace Weryfikator.Logika
{
    internal enum Rodzaj { Brak, Kwalifikowany, PieczecKwalifikowana, Zaufany, Osobisty, Inny }
    internal enum Status { Wazny, Nieokreslony, Niewazny, BrakPodpisu, Blad }

    /// <summary>Wynik sprawdzenia jednego podpisu w pliku (albo informacja, że plik nie ma podpisu).</summary>
    internal sealed class WynikPodpisu
    {
        public string Plik = "";
        public int Nr;
        public Rodzaj Rodzaj;
        public Status Status;
        public string Podpisujacy = "";
        public string? Pesel;
        /// <summary>null — nie wczytano listy albo nie odczytano numeru PESEL.</summary>
        public bool? NaLiscie;
        /// <summary>Sprawdzano bez listy PESEL — o wyniku decyduje sama ważność podpisu.</summary>
        public bool BezListyPesel;
        public DateTime? CzasUtc;
        public bool CzasZeZnacznika;
        public string Wystawca = "";
        public List<string> Uwagi = new List<string>();

        public string RodzajTekst
        {
            get
            {
                switch (Rodzaj)
                {
                    case Rodzaj.Kwalifikowany: return "kwalifikowany";
                    case Rodzaj.PieczecKwalifikowana: return "pieczęć kwalifikowana";
                    case Rodzaj.Zaufany: return "zaufany";
                    case Rodzaj.Osobisty: return "osobisty";
                    case Rodzaj.Inny: return "inny";
                    default: return "—";
                }
            }
        }

        public string StatusTekst
        {
            get
            {
                switch (Status)
                {
                    case Status.Wazny: return "WAŻNY";
                    case Status.Nieokreslony: return "DO WYJAŚNIENIA";
                    case Status.Niewazny: return "NIEWAŻNY";
                    case Status.BrakPodpisu: return "BRAK PODPISU";
                    default: return "BŁĄD";
                }
            }
        }

        public string PeselTekst => Pesel ?? (Status == Status.BrakPodpisu || Status == Status.Blad ? "" : "nie odczytano");
        public string NaLiscieTekst => NaLiscie == null ? (Pesel == null ? "" : "brak listy") : NaLiscie.Value ? "TAK" : "NIE";
        public string CzasTekst => CzasUtc == null ? "" : CzasUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") + (CzasZeZnacznika ? " (znacznik czasu)" : "");

        /// <summary>Podpis ważny i PESEL podpisującego jest na liście (gdy listę wczytano).</summary>
        public bool WPorzadku => Status == Status.Wazny && (NaLiscie == true || BezListyPesel);
    }
}
