using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ocean
{
    /// <summary>
    /// Shop (B öffnet/schließt, Esc schließt). Zeigt alle Upgrades nach Bereichen mit Wirkung,
    /// Stufe und Preis; zu teure sind ausgegraut. Pumpen, Rohre und Kisten landen im Inventar –
    /// ist dort kein Platz, geht der Kauf nicht. Das Spiel läuft weiter, auch allein.
    /// (Übergangslösung bis zum Shop-Gebäude in Etappe 7.)
    /// </summary>
    public class ShopFenster : MonoBehaviour
    {
        public UIDocument oberflaeche;

        static ShopFenster instanz;
        static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

        public static bool Offen => instanz != null && instanz.offen;

        bool offen;
        VisualElement fenster;
        ScrollView liste;
        Label geld, meldung;
        readonly List<(Upgrade upgrade, Label titel, Label text, Button knopf)> zeilen = new();

        void Awake() => instanz = this;

        void Start()
        {
            var wurzel = oberflaeche.rootVisualElement;
            fenster = wurzel.Q("shop");
            liste = wurzel.Q<ScrollView>("shopListe");
            geld = wurzel.Q<Label>("shopGeld");
            meldung = wurzel.Q<Label>("shopMeldung");
            wurzel.Q<Button>("shopSchliessen").clicked += Schliessen;

            Bereich("Behälter");
            Zeile(Upgrade.Behaelter);
            Zeile(Upgrade.Groesse);
            Bereich("Ausrüstung");
            Zeile(Upgrade.Schuhe);
            Bereich("Pumpen");
            Zeile(Upgrade.Handpumpe);
            Zeile(Upgrade.Motorpumpe);
            Zeile(Upgrade.Rohr);
            Bereich("Lager");
            Zeile(Upgrade.Kiste);
            Bereich("Rakete");
            Zeile(Upgrade.RaketenLadung);
            Zeile(Upgrade.RaketenFlugzeit);
        }

        public static void Umschalten()
        {
            if (instanz == null) return;
            if (instanz.offen) Schliessen();
            else if (Netzwerkmenue.ImSpiel && UnityEngine.Cursor.lockState == CursorLockMode.Locked && !InventarFenster.Offen) instanz.Oeffnen();
        }

        void Oeffnen()
        {
            offen = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
        }

        public static void Schliessen()
        {
            if (instanz == null || !instanz.offen) return;
            instanz.offen = false;
            if (Netzwerkmenue.ImSpiel) UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        }

        void Update()
        {
            if (fenster == null) return;
            if (offen && !Netzwerkmenue.ImSpiel) offen = false;   // Spiel verlassen → Shop zu
            fenster.style.display = offen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!offen) return;

            var stand = Spielstand.Instanz;
            if (stand == null) return;
            geld.text = $"{stand.kasse.Value.ToString("N0", Deutsch)} Münzen";
            var lager = Lager.Lokal;
            bool voll = lager != null && lager.FreiePlaetze == 0;
            if (meldung != null)
            {
                meldung.text = voll ? "Inventar voll – leg erst etwas in eine Kiste (Tab), sonst passt nur noch etwas auf angefangene Stapel." : "";
                meldung.style.display = voll ? DisplayStyle.Flex : DisplayStyle.None;
            }

            foreach (var (u, titel, text, knopf) in zeilen)
            {
                titel.text = Titel(stand, u);
                text.text = Beschreibung(stand, u);
                bool maximal = stand.Maximal(u);
                bool passt = Passt(stand, u);
                knopf.text = maximal ? "Max." : !passt ? "Kein Platz"
                    : $"{System.Math.Round(stand.Preis(u)).ToString("N0", Deutsch)}";
                knopf.SetEnabled(!maximal && passt && stand.Leistbar(u));
            }
        }

        void Kaufen(Upgrade u)
        {
            var stand = Spielstand.Instanz;
            if (stand == null || !stand.IsSpawned || !stand.Leistbar(u) || !Passt(stand, u)) return;
            if (GegenstandInfo.AusUpgrade(u) != Gegenstand.Nichts) stand.GegenstandKaufenRpc((int)u);
            else stand.KaufenRpc((int)u);
        }

        /// <summary>Gegenstände brauchen Platz im Inventar; Upgrades immer.</summary>
        static bool Passt(Spielstand stand, Upgrade u)
        {
            var g = GegenstandInfo.AusUpgrade(u);
            if (g == Gegenstand.Nichts) return true;
            var lager = Lager.Lokal;
            return lager != null && lager.Passt(g, g == Gegenstand.Rohr ? stand.werte.rohrPaket : 1);
        }

        // ---------- Texte ----------

        static string Titel(Spielstand s, Upgrade u)
        {
            var w = s.werte;
            switch (u)
            {
                case Upgrade.Behaelter:
                    return s.Maximal(u) ? $"Behälter: {s.AktuellerBehaelter.name}" : w.behaelter[s.BehaelterNummer + 1].name;
                case Upgrade.Groesse: return $"Größerer {s.AktuellerBehaelter.name}";
                case Upgrade.Schuhe: return "Laufschuhe";
                case Upgrade.Handpumpe: return "Handpumpe";
                case Upgrade.Motorpumpe: return "Motorpumpe";
                case Upgrade.RaketenLadung: return "Größerer Raketentank";
                case Upgrade.RaketenFlugzeit: return "Schnellere Rakete";
                case Upgrade.Rohr: return $"Rohr ({s.werte.rohrPaket} m)";
                case Upgrade.Kiste: return "Kiste";
            }
            return u.ToString();
        }

        static string Beschreibung(Spielstand s, Upgrade u)
        {
            var w = s.werte;
            switch (u)
            {
                case Upgrade.Behaelter:
                    if (s.Maximal(u)) return "Bester Behälter freigeschaltet";
                    var naechster = w.behaelter[s.BehaelterNummer + 1];
                    return $"{naechster.liter:0} l statt {s.AktuellerBehaelter.liter:0} l · voll {Mathf.RoundToInt((1f - naechster.tempoVoll) * 100)} % langsamer";
                case Upgrade.Groesse:
                    return $"+{w.groesseBonus * 100:0} % Volumen · jetzt {s.Kapazitaet:0} l · Stufe {s.Stufe(u)}/{w.groesseMax}";
                case Upgrade.Schuhe:
                    return $"+{w.schuheBonus * 100:0} % Lauftempo · Stufe {s.Stufe(u)}/{w.schuheMax}";
                case Upgrade.Handpumpe:
                    return $"{w.handpumpeLeistung:0} l/s, solange du pumpst · braucht Saugschlauch im Wasser und Rohr zum Tank · gekauft: {s.Stufe(u)}";
                case Upgrade.Motorpumpe:
                    return $"{w.motorpumpeLeistung:0} l/s, läuft von allein · braucht Saugschlauch im Wasser und Rohr zum Tank · gekauft: {s.Stufe(u)}";
                case Upgrade.Rohr:
                    return $"Verbindet Pumpen mit dem Tank · {w.rohrPreisProMeter:0} Münzen pro Meter · bis {w.rohrMaxLaenge:0} m am Stück · im Inventar: {(Lager.Lokal != null ? Lager.Lokal.Anzahl(Gegenstand.Rohr) : 0)} m";
                case Upgrade.Kiste:
                    return $"Lager mit {w.kistenPlaetze} Plätzen · aufstellen wie eine Pumpe, öffnen mit E";
                case Upgrade.RaketenLadung:
                    return $"{s.RaketenLadung:0} l → {s.RaketenLadung * w.ladungMultiplikator:0} l pro Flug";
                case Upgrade.RaketenFlugzeit:
                    return s.Maximal(u) ? $"{s.RaketenFlugzeit:0} s · schneller geht es nicht"
                        : $"{s.RaketenFlugzeit:0} s → {s.RaketenFlugzeit * w.flugMultiplikator:0} s Hin- und Rückflug";
            }
            return "";
        }

        // ---------- Aufbau der Liste ----------

        void Bereich(string name)
        {
            var titel = new Label(name);
            titel.AddToClassList("shop-bereich");
            liste.Add(titel);
        }

        void Zeile(Upgrade u)
        {
            var zeile = new VisualElement();
            zeile.AddToClassList("shop-zeile");
            var texte = new VisualElement();
            texte.AddToClassList("shop-texte");
            var titel = new Label();
            titel.AddToClassList("shop-titel");
            var text = new Label();
            text.AddToClassList("shop-text");
            texte.Add(titel);
            texte.Add(text);
            var knopf = new Button(() => Kaufen(u));
            knopf.AddToClassList("knopf");
            knopf.AddToClassList("knopf--haupt");
            knopf.AddToClassList("shop-knopf");
            zeile.Add(texte);
            zeile.Add(knopf);
            liste.Add(zeile);
            zeilen.Add((u, titel, text, knopf));
        }
    }
}
