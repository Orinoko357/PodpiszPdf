using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Weryfikator.Logika
{
    internal static class Pesel
    {
        private static readonly int[] Wagi = { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };
        private static readonly Regex ZEtykieta = new Regex(@"(?:PNOPL-|PESEL[^0-9]{0,30}?)(\d{11})(?!\d)", RegexOptions.IgnoreCase);
        private static readonly Regex Samodzielny = new Regex(@"(?<![0-9A-Za-z\-])(\d{11})(?![0-9])");

        public static bool Poprawny(string? s)
        {
            if (s == null || s.Length != 11) return false;
            int suma = 0;
            for (int i = 0; i < 11; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
                if (i < 10) suma += (s[i] - '0') * Wagi[i];
            }
            if ((10 - suma % 10) % 10 != s[10] - '0') return false;
            int m = ((s[2] - '0') * 10 + (s[3] - '0')) % 20, d = (s[4] - '0') * 10 + (s[5] - '0');
            return m >= 1 && m <= 12 && d >= 1 && d <= 31;
        }

        /// <summary>PESEL poprzedzony etykietą („PNOPL-”, „PESEL: ”, element XML PESEL).</summary>
        public static string? ZEtykiety(string tekst)
        {
            foreach (Match m in ZEtykieta.Matches(tekst))
                if (Poprawny(m.Groups[1].Value)) return m.Groups[1].Value;
            return null;
        }

        /// <summary>Wszystkie samodzielne ciągi 11 cyfr z poprawną sumą kontrolną.</summary>
        public static List<string> Samodzielne(string tekst)
        {
            var l = new List<string>();
            foreach (Match m in Samodzielny.Matches(tekst))
                if (Poprawny(m.Groups[1].Value) && !l.Contains(m.Groups[1].Value)) l.Add(m.Groups[1].Value);
            return l;
        }
    }
}
