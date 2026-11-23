using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Weryfikator.Logika;

namespace Weryfikator
{
    /// <summary>Podgląd listy PESEL z wyborem arkusza i kolumny, w której są numery.</summary>
    internal sealed class OknoListy : Form
    {
        private const int LimitWierszy = 5000;
        private readonly ListaPesel _lista;
        private readonly ComboBox _arkusz = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(3, 4, 12, 3) };
        private readonly ComboBox _kolumna = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, Margin = new Padding(3, 4, 12, 3) };
        private readonly Label _podsumowanie = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
        private readonly DataGridView _siatka = new DataGridView();
        private bool _wypelnianie;

        public int WybranyArkusz => _arkusz.SelectedIndex - 1;
        public int WybranaKolumna => _kolumna.SelectedIndex - 1;

        public OknoListy(ListaPesel lista)
        {
            _lista = lista;
            Text = "Lista PESEL — " + System.IO.Path.GetFileName(lista.Plik);
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(860, 540);
            MinimumSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            ShowIcon = false;

            var gora = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6), WrapContents = true };
            gora.Controls.Add(new Label { Text = "Arkusz:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            gora.Controls.Add(_arkusz);
            gora.Controls.Add(new Label { Text = "Kolumna z numerami PESEL:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            gora.Controls.Add(_kolumna);
            gora.Controls.Add(_podsumowanie);

            _siatka.Dock = DockStyle.Fill;
            _siatka.ReadOnly = true;
            _siatka.AllowUserToAddRows = false;
            _siatka.AllowUserToDeleteRows = false;
            _siatka.AllowUserToResizeRows = false;
            _siatka.AllowUserToOrderColumns = false;
            _siatka.RowHeadersWidth = 60 * DeviceDpi / 96;
            _siatka.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            _siatka.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _siatka.BackgroundColor = SystemColors.Window;
            _siatka.BorderStyle = BorderStyle.None;
            _siatka.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(14, 3, 14, 3) };
            var anuluj = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
            var dol = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            dol.Controls.Add(anuluj);
            dol.Controls.Add(ok);
            dol.Controls.Add(new Label
            {
                AutoSize = true, Margin = new Padding(3, 8, 20, 0), ForeColor = SystemColors.GrayText,
                Text = "Zielone — numery PESEL brane do porównania; czerwone — 11 cyfr z błędną sumą kontrolną. Kliknięcie nagłówka kolumny wybiera ją."
            });
            AcceptButton = ok;
            CancelButton = anuluj;

            Controls.Add(_siatka);
            Controls.Add(gora);
            Controls.Add(dol);

            _wypelnianie = true;
            _arkusz.Items.Add("Wszystkie arkusze");
            foreach (var a in lista.Arkusze) _arkusz.Items.Add(a.Nazwa);
            _arkusz.SelectedIndex = lista.Arkusze.Count == 1 ? 1 : lista.WybranyArkusz + 1;
            _arkusz.Enabled = lista.Arkusze.Count > 1;
            _wypelnianie = false;
            WypelnijKolumny(lista.WybranaKolumna);

            _arkusz.SelectedIndexChanged += (s, e) => { if (!_wypelnianie) WypelnijKolumny(WybranaKolumna); };
            _kolumna.SelectedIndexChanged += (s, e) => { if (!_wypelnianie) Pokaz(); };
            _siatka.ColumnHeaderMouseClick += (s, e) => { if (e.ColumnIndex + 1 < _kolumna.Items.Count) _kolumna.SelectedIndex = e.ColumnIndex + 1; };
        }

        /// <summary>Arkusz pokazywany w podglądzie (przy „Wszystkie arkusze” — pierwszy).</summary>
        private Arkusz? Pokazywany => _lista.Arkusze.Count == 0 ? null : _lista.Arkusze[Math.Max(0, WybranyArkusz)];

        private void WypelnijKolumny(int wybrana)
        {
            _wypelnianie = true;
            _kolumna.Items.Clear();
            _kolumna.Items.Add("Wszystkie kolumny (automatycznie)");
            var a = Pokazywany;
            int n = WybranyArkusz < 0 ? _lista.Arkusze.Select(x => x.LiczbaKolumn).DefaultIfEmpty(0).Max() : a?.LiczbaKolumn ?? 0;
            for (int k = 0; k < n; k++)
            {
                var naglowek = a != null && a.Wiersze.Count > 0 && k < a.Wiersze[0].Count ? a.Wiersze[0][k].Tekst : "";
                if (naglowek.Length > 30) naglowek = naglowek.Substring(0, 30) + "…";
                _kolumna.Items.Add(ListaPesel.NazwaKolumny(k) + (naglowek != "" ? " — " + naglowek : ""));
            }
            _kolumna.SelectedIndex = wybrana + 1 < _kolumna.Items.Count ? wybrana + 1 : 0;
            _wypelnianie = false;
            Pokaz();
        }

        private static readonly Color Zielony = Color.FromArgb(223, 240, 216), Czerwony = Color.FromArgb(248, 215, 218), Szary = Color.FromArgb(245, 245, 245);

        private void Pokaz()
        {
            var a = Pokazywany;
            int kol = WybranaKolumna;
            _siatka.SuspendLayout();
            _siatka.Rows.Clear();
            _siatka.Columns.Clear();
            int n = a?.LiczbaKolumn ?? 0;
            for (int k = 0; k < n; k++)
            {
                var c = _siatka.Columns[_siatka.Columns.Add("k" + k, ListaPesel.NazwaKolumny(k))];
                c.SortMode = DataGridViewColumnSortMode.NotSortable;
                c.Width = 130 * DeviceDpi / 96;
                if (kol >= 0 && k != kol) c.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                if (k == kol) c.HeaderCell.Style.Font = new Font(Font, FontStyle.Bold);
            }
            if (a != null && n > 0)
            {
                var numery = new HashSet<string>(); var bledne = new List<string>();
                int ile = Math.Min(a.Wiersze.Count, LimitWierszy);
                for (int w = 0; w < ile; w++)
                {
                    var wiersz = a.Wiersze[w];
                    var wartosci = new object[n];
                    for (int k = 0; k < n; k++) wartosci[k] = k < wiersz.Count ? wiersz[k].Tekst : "";
                    var r = _siatka.Rows[_siatka.Rows.Add(wartosci)];
                    r.HeaderCell.Value = (w + 1).ToString();
                    for (int k = 0; k < wiersz.Count; k++)
                    {
                        if (kol >= 0 && k != kol) continue;
                        numery.Clear(); bledne.Clear();
                        ListaPesel.Ocen(wiersz[k], numery, bledne);
                        if (numery.Count > 0) r.Cells[k].Style.BackColor = Zielony;
                        else if (bledne.Count > 0) r.Cells[k].Style.BackColor = Czerwony;
                        else if (kol >= 0 && w > 0 && wiersz[k].Tekst != "") r.Cells[k].Style.BackColor = Szary;
                    }
                }
            }
            _siatka.ResumeLayout();
            if (kol >= 0 && kol < _siatka.Columns.Count) _siatka.FirstDisplayedScrollingColumnIndex = Math.Max(0, kol - 1);

            // Liczba numerów przy bieżącym wyborze (liczona na kopii ustawień listy).
            int staryA = _lista.WybranyArkusz, staraK = _lista.WybranaKolumna;
            _lista.Wybierz(WybranyArkusz, kol);
            _podsumowanie.Text = $"Numerów PESEL: {_lista.Numery.Count}" + (_lista.Bledne.Count > 0 ? $", z błędną sumą kontrolną: {_lista.Bledne.Count}" : "") +
                                 (a != null && a.Wiersze.Count > LimitWierszy ? $" (podgląd: pierwsze {LimitWierszy} wierszy)" : "");
            _podsumowanie.ForeColor = _lista.Numery.Count == 0 ? Color.Firebrick : SystemColors.ControlText;
            _lista.Wybierz(staryA, staraK);
        }
    }
}
