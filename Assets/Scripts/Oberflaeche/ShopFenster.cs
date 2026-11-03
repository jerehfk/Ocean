using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ocean
{
    /// <summary>
    /// Shop im Shop-Gebäude (E am Tresen öffnet, Esc schließt; zu weit weg schließt ihn auch).
    /// Reiter „Kaufen“: alle Upgrades nach Bereichen mit Wirkung, Stufe und Preis; zu teure sind
    /// ausgegraut, noch nicht erforschte gesperrt. Pumpen, Rohre und Kisten landen im Inventar –
    /// ist dort kein Platz, geht der Kauf nicht. Reiter „Forschung“: der Forschungsbaum.
    /// Das Spiel läuft weiter, auch allein.
    /// </summary>
    public class ShopFenster : MonoBehaviour
    {
        public UIDocument oberflaeche;

        static ShopFenster instanz;
        static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

        public static bool Offen => instanz != null && instanz.offen;

        bool offen;
        VisualElement fenster;
        ScrollView liste, forschungListe;
        Button reiterKaufen, reiterForschung;
        bool forschungSichtbar;
        Label geld, meldung;
        readonly List<(Upgrade upgrade, Label titel, Label text, Button knopf)> zeilen = new();
        readonly List<(Forschung forschung, Label titel, Label text, Button knopf)> forschungen = new();

        void Awake() => instanz = this;

        void Start()
        {
            var wurzel = oberflaeche.rootVisualElement;
            fenster = wurzel.Q("shop");
            liste = wurzel.Q<ScrollView>("shopListe");
            geld = wurzel.Q<Label>("shopGeld");
            meldung = wurzel.Q<Label>("shopMeldung");
            wurzel.Q<Button>("shopSchliessen").clicked += Schliessen;
            ReiterAufbauen();

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
            Bereich("Mars");
            Zeile(Upgrade.Bildschirm);
            Zeile(Upgrade.Schuessel);
            Bereich("Rakete");
            Zeile(Upgrade.RaketenLadung);
            Zeile(Upgrade.RaketenFlugzeit);
        }

        /// <summary>Vom Tresen des Shop-Gebäudes aus (E).</summary>
        public static void OeffnenAmTresen()
        {
            if (instanz == null || instanz.offen) return;
            if (Netzwerkmenue.ImSpiel && UnityEngine.Cursor.lockState == CursorLockMode.Locked && !InventarFenster.Offen) instanz.Oeffnen();
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
            if (offen && TresenZuWeit()) Schliessen();             // vom Tresen weggelaufen
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

            reiterKaufen.EnableInClassList("shop-reiter--aktiv", !forschungSichtbar);
            reiterForschung.EnableInClassList("shop-reiter--aktiv", forschungSichtbar);
            liste.style.display = forschungSichtbar ? DisplayStyle.None : DisplayStyle.Flex;
            forschungListe.style.display = forschungSichtbar ? DisplayStyle.Flex : DisplayStyle.None;

            foreach (var (u, titel, text, knopf) in zeilen)
            {
                bool gesperrt = !stand.Freigeschaltet(u);
                titel.text = Titel(stand, u);
                text.text = gesperrt ? $"Gesperrt – erst erforschen: {stand.werte.Knoten(Spielstand.Benoetigt(u)).name}" : Beschreibung(stand, u);
                bool maximal = stand.Maximal(u);
                bool passt = Passt(stand, u);
                knopf.text = gesperrt ? "Gesperrt" : maximal ? "Max." : !passt ? "Kein Platz"
                    : $"{System.Math.Round(stand.Preis(u)).ToString("N0", Deutsch)}";
                knopf.SetEnabled(!gesperrt && !maximal && passt && stand.Leistbar(u));
            }

            foreach (var (f, titel, text, knopf) in forschungen)
            {
                var k = stand.werte.Knoten(f);
                bool fertig = stand.Erforscht(f);
                bool moeglich = stand.ForschungOffen(f);
                titel.text = k.name;
                text.text = fertig ? $"Erforscht · {k.beschreibung}"
                    : moeglich ? k.beschreibung
                    : $"{k.beschreibung} · erst erforschen: {stand.werte.Knoten(k.voraussetzung).name}";
                knopf.text = fertig ? "Erforscht" : !moeglich ? "Gesperrt"
                    : $"{System.Math.Round(k.preis).ToString("N0", Deutsch)}";
                knopf.SetEnabled(stand.ForschungLeistbar(f));
            }
        }

        static bool TresenZuWeit()
        {
            var haus = Shopgebaeude.Instanz;
            var lager = Lager.Lokal;
            var stand = Spielstand.Instanz;
            if (haus == null || lager == null || stand == null) return false;
            return haus.Abstand(lager.transform.position) > stand.werte.shopAbstand;
        }

        void Erforschen(Forschung f)
        {
            var stand = Spielstand.Instanz;
            if (stand == null || !stand.IsSpawned || !stand.ForschungLeistbar(f)) return;
            stand.ForschenRpc((int)f);
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
                case Upgrade.Bildschirm: return s.SatellitGestartet ? "Bildschirm" : "Bildschirm + Satellit";
                case Upgrade.Schuessel: return "Satellitenschüssel";
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
                case Upgrade.Bildschirm:
                    return s.SatellitGestartet
                        ? "Zeigt das Bild des Satelliten am Mars · in die Hand nehmen, Rechtsklick: groß"
                        : "Schickt einen Satelliten zum Mars – danach zeigt der Bildschirm sein Bild · braucht eine Satellitenschüssel";
                case Upgrade.Schuessel:
                    return $"Empfängt das Satellitenbild · eine reicht für alle · aufgestellt: {Schuessel.Alle.Count}";
                case Upgrade.RaketenLadung:
                    return $"{s.RaketenLadung:0} l → {s.RaketenLadung * w.ladungMultiplikator:0} l pro Flug";
                case Upgrade.RaketenFlugzeit:
                    return s.Maximal(u) ? $"{s.RaketenFlugzeit:0} s · schneller geht es nicht"
                        : $"{s.RaketenFlugzeit:0} s → {s.RaketenFlugzeit * w.flugMultiplikator:0} s Hin- und Rückflug";
            }
            return "";
        }

        // ---------- Aufbau der Liste ----------

        /// <summary>Reiter „Kaufen“ / „Forschung“ über der Liste und die Forschungsliste selbst.</summary>
        void ReiterAufbauen()
        {
            var karte = liste.parent;
            var leiste = new VisualElement();
            leiste.AddToClassList("shop-reiter");
            reiterKaufen = new Button(() => forschungSichtbar = false) { text = "Kaufen" };
            reiterForschung = new Button(() => forschungSichtbar = true) { text = "Forschung" };
            foreach (var r in new[] { reiterKaufen, reiterForschung })
            {
                r.AddToClassList("knopf");
                r.AddToClassList("knopf--leise");
                r.AddToClassList("shop-reiter-knopf");
                leiste.Add(r);
            }
            karte.Insert(karte.IndexOf(liste), leiste);

            forschungListe = new ScrollView();
            forschungListe.AddToClassList("shop-liste");
            karte.Insert(karte.IndexOf(liste) + 1, forschungListe);

            var hinweis = new Label("Forschungsbaum – schaltet neue Waren frei. Bezahlt aus der gemeinsamen Kasse.");
            hinweis.AddToClassList("shop-text");
            forschungListe.Add(hinweis);
            ForschungsZeile(Forschung.Lagerbau);
            ForschungsZeile(Forschung.Raumfahrt);
            ForschungsZeile(Forschung.Pumpentechnik);
            ForschungsZeile(Forschung.Motorantrieb);
        }

        void ForschungsZeile(Forschung f)
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
            var knopf = new Button(() => Erforschen(f));
            knopf.AddToClassList("knopf");
            knopf.AddToClassList("knopf--haupt");
            knopf.AddToClassList("shop-knopf");
            zeile.Add(texte);
            zeile.Add(knopf);
            forschungListe.Add(zeile);
            forschungen.Add((f, titel, text, knopf));
        }

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
