using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ocean
{
    /// <summary>Ist gerade ein Spiel-Fenster offen (Inventar, Kiste, Shop)? Dann kein Pausemenü.</summary>
    public static class Fenster
    {
        public static bool Offen => ShopFenster.Offen || InventarFenster.Offen;

        public static void AlleSchliessen()
        {
            ShopFenster.Schliessen();
            InventarFenster.Schliessen();
        }
    }

    /// <summary>
    /// Inventar (Tab) und Kiste (E an einer Kiste): Plätze per Maus ziehen, Umschalt + Klick
    /// verschiebt einen Stapel schnell auf die andere Seite. Zeigt außerdem die Schnellleiste unten im Bild.
    /// Der Inhalt kommt vom Host; jede Änderung ist eine Anfrage an ihn.
    /// </summary>
    public class InventarFenster : MonoBehaviour
    {
        public UIDocument oberflaeche;

        static InventarFenster instanz;
        public static bool Offen => instanz != null && instanz.offen;

        const float Kistenweite = 7f;   // weiter weg → Kiste schließt sich

        bool offen;
        bool geradeGeoeffnet;
        Lager kiste;
        VisualElement fenster, kisteKarte, rucksack, schnellRaster, kisteRaster, leiste, geist;

        // Ein Platz in der Oberfläche: welches Lager, welcher Platz, welche Elemente
        class PlatzAnsicht
        {
            public bool vonKiste;
            public int platz;
            public VisualElement element, farbe;
            public Label name, menge;
        }
        readonly List<PlatzAnsicht> inventarPlaetze = new();
        readonly List<PlatzAnsicht> kistenPlaetze = new();
        readonly List<PlatzAnsicht> leistenPlaetze = new();
        VisualElement behaelterFuellung;
        Label behaelterName, behaelterText;
        VisualElement behaelterPlatz;

        PlatzAnsicht gezogen;

        void Awake() => instanz = this;

        void Start()
        {
            var wurzel = oberflaeche.rootVisualElement;
            fenster = wurzel.Q("inventar");
            kisteKarte = wurzel.Q("kisteKarte");
            rucksack = wurzel.Q("rucksack");
            schnellRaster = wurzel.Q("schnellRaster");
            kisteRaster = wurzel.Q("kisteRaster");
            leiste = wurzel.Q("schnellleiste");
            geist = wurzel.Q("geist");
            if (fenster == null) { Debug.LogWarning("Ocean: Inventar fehlt in Menue.uxml."); return; }

            // Inventar: Schnellleiste (Platz 0–6) und Rucksack (ab 7). Anzahl folgt den Spiel-Werten.
            var werte = FindAnyObjectByType<Spielstand>() is { } s ? s.werte : null;
            int rucksackPlaetze = werte != null ? werte.rucksackPlaetze : 20;
            int kistenGroesse = werte != null ? werte.kistenPlaetze : 24;
            for (int i = 0; i < Lager.SchnellPlaetze; i++) inventarPlaetze.Add(PlatzBauen(schnellRaster, false, i, $"{i + 2}"));
            for (int i = 0; i < rucksackPlaetze; i++) inventarPlaetze.Add(PlatzBauen(rucksack, false, Lager.SchnellPlaetze + i, null));
            for (int i = 0; i < kistenGroesse; i++) kistenPlaetze.Add(PlatzBauen(kisteRaster, true, i, null));

            // Ziehen: Loslassen irgendwo im Fenster
            fenster.RegisterCallback<PointerMoveEvent>(e => { if (gezogen != null) GeistSetzen(e.position); });
            fenster.RegisterCallback<PointerUpEvent>(e => Loslassen(e.position));

            // Schnellleiste im Bild
            if (leiste != null)
            {
                behaelterPlatz = new VisualElement();
                behaelterPlatz.AddToClassList("leiste-platz");
                behaelterPlatz.AddToClassList("leiste-platz--behaelter");
                behaelterPlatz.pickingMode = PickingMode.Ignore;
                behaelterPlatz.Add(Text("1", "leiste-taste"));
                behaelterName = Text("Eimer", "leiste-name");
                behaelterPlatz.Add(behaelterName);
                var balken = new VisualElement { pickingMode = PickingMode.Ignore };
                balken.AddToClassList("eimer-balken");
                behaelterFuellung = new VisualElement { pickingMode = PickingMode.Ignore };
                behaelterFuellung.AddToClassList("eimer-fuellung");
                balken.Add(behaelterFuellung);
                behaelterPlatz.Add(balken);
                behaelterText = Text("", "leiste-menge");
                behaelterPlatz.Add(behaelterText);
                leiste.Add(behaelterPlatz);

                for (int i = 0; i < Lager.SchnellPlaetze; i++)
                {
                    var p = new PlatzAnsicht { platz = i, element = new VisualElement { pickingMode = PickingMode.Ignore } };
                    p.element.AddToClassList("leiste-platz");
                    p.farbe = new VisualElement { pickingMode = PickingMode.Ignore };
                    p.farbe.AddToClassList("platz-farbe");
                    p.element.Add(p.farbe);
                    p.element.Add(Text($"{i + 2}", "leiste-taste"));
                    p.name = Text("", "leiste-name");
                    p.menge = Text("", "leiste-menge");
                    p.element.Add(p.name);
                    p.element.Add(p.menge);
                    leiste.Add(p.element);
                    leistenPlaetze.Add(p);
                }
            }
        }

        static Label Text(string text, string klasse)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(klasse);
            return l;
        }

        PlatzAnsicht PlatzBauen(VisualElement raster, bool vonKiste, int platz, string taste)
        {
            var p = new PlatzAnsicht { vonKiste = vonKiste, platz = platz, element = new VisualElement() };
            p.element.AddToClassList("platz");
            p.element.userData = p;
            p.farbe = new VisualElement { pickingMode = PickingMode.Ignore };
            p.farbe.AddToClassList("platz-farbe");
            p.element.Add(p.farbe);
            if (taste != null) p.element.Add(Text(taste, "platz-taste"));
            p.name = Text("", "platz-name");
            p.menge = Text("", "platz-menge");
            p.element.Add(p.name);
            p.element.Add(p.menge);
            p.element.RegisterCallback<PointerDownEvent>(e => Druecken(p, e));
            raster.Add(p.element);
            return p;
        }

        // ---------- Öffnen und Schließen ----------

        public static void Umschalten()
        {
            if (instanz == null) return;
            if (instanz.offen) Schliessen();
            else if (Netzwerkmenue.ImSpiel && UnityEngine.Cursor.lockState == CursorLockMode.Locked && !ShopFenster.Offen)
                instanz.Oeffnen(null);
        }

        public static void KisteOeffnen(Lager kiste)
        {
            if (instanz == null || instanz.offen || !Netzwerkmenue.ImSpiel) return;
            instanz.Oeffnen(kiste);
        }

        void Oeffnen(Lager neueKiste)
        {
            kiste = neueKiste;
            offen = true;
            geradeGeoeffnet = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
        }

        public static void Schliessen()
        {
            if (instanz == null || !instanz.offen) return;
            instanz.offen = false;
            instanz.kiste = null;
            instanz.ZiehenBeenden();
            if (Netzwerkmenue.ImSpiel) UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        }

        // ---------- Jedes Bild ----------

        void Update()
        {
            if (fenster == null) return;
            if (offen && !Netzwerkmenue.ImSpiel) { offen = false; kiste = null; }

            var lager = Lager.Lokal;
            if (offen)
            {
                // E schließt die Kiste wieder (nicht im selben Bild, in dem sie aufging)
                bool e = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
                if (e && !geradeGeoeffnet && kiste != null) Schliessen();
                // Kiste weg oder zu weit entfernt → zu
                else if (kiste != null && (!kiste.IsSpawned || lager == null
                         || Vector3.Distance(lager.transform.position, kiste.transform.position) > Kistenweite))
                    Schliessen();
            }
            geradeGeoeffnet = false;

            fenster.style.display = offen ? DisplayStyle.Flex : DisplayStyle.None;
            kisteKarte.style.display = kiste != null ? DisplayStyle.Flex : DisplayStyle.None;

            if (offen && lager != null)
            {
                foreach (var p in inventarPlaetze) Zeigen(p, lager.Platz(p.platz));
                if (kiste != null) foreach (var p in kistenPlaetze) Zeigen(p, kiste.Platz(p.platz));
            }
            LeisteZeigen(lager);
        }

        static readonly Color Leer = new Color(0, 0, 0, 0);

        static Color Farbe(Gegenstand g) => g switch
        {
            Gegenstand.Handpumpe => new Color(0.06f, 0.66f, 0.72f),
            Gegenstand.Motorpumpe => new Color(0.95f, 0.62f, 0.18f),
            Gegenstand.Rohr => new Color(0.62f, 0.66f, 0.70f),
            Gegenstand.Kiste => new Color(0.55f, 0.38f, 0.22f),
            _ => Leer,
        };

        static void Zeigen(PlatzAnsicht p, Stapel s)
        {
            p.name.text = s.Leer ? "" : GegenstandInfo.Name(s.art);
            p.menge.text = GegenstandInfo.Menge(s);
            p.farbe.style.backgroundColor = s.Leer ? Leer : Farbe(s.art);
        }

        void LeisteZeigen(Lager lager)
        {
            if (leiste == null) return;
            var spieler = SpielerAktionen.Lokal;
            var schnell = Schnellleiste.Lokal;
            int gewaehlt = schnell != null ? schnell.Gewaehlt : 0;

            if (spieler != null)
            {
                behaelterName.text = spieler.BehaelterName;
                behaelterFuellung.style.width = Length.Percent(Mathf.Clamp01(spieler.Fuellstand) * 100f);
                behaelterText.text = $"{spieler.eimerLiter.Value:0} / {spieler.Kapazitaet:0} l";
            }
            behaelterPlatz.EnableInClassList("leiste-platz--gewaehlt", gewaehlt == 0);

            foreach (var p in leistenPlaetze)
            {
                Zeigen(p, lager != null ? lager.Platz(p.platz) : default);
                p.element.EnableInClassList("leiste-platz--gewaehlt", gewaehlt == p.platz + 1);
            }
        }

        // ---------- Ziehen und Schnell-Verschieben ----------

        Lager LagerVon(PlatzAnsicht p) => p.vonKiste ? kiste : Lager.Lokal;

        void Druecken(PlatzAnsicht p, PointerDownEvent e)
        {
            if (e.button != 0) return;
            var lager = LagerVon(p);
            if (lager == null || !lager.IsSpawned || lager.Platz(p.platz).Leer) return;

            if (e.shiftKey) { Schnell(p, lager); return; }

            gezogen = p;
            p.element.AddToClassList("platz--gezogen");
            var s = lager.Platz(p.platz);
            ((Label)geist).text = $"{GegenstandInfo.Name(s.art)}\n{GegenstandInfo.Menge(s)}";
            geist.style.display = DisplayStyle.Flex;
            GeistSetzen(e.position);
        }

        void GeistSetzen(Vector2 position)
        {
            geist.style.left = position.x - 48f;
            geist.style.top = position.y - 48f;
        }

        void Loslassen(Vector2 position)
        {
            if (gezogen == null) return;
            var quelle = gezogen;
            ZiehenBeenden();

            // Welcher Platz liegt unter der Maus?
            var getroffen = fenster.panel.Pick(position);
            while (getroffen != null && getroffen.userData is not PlatzAnsicht) getroffen = getroffen.parent;
            if (getroffen?.userData is not PlatzAnsicht ziel || ziel == quelle) return;

            var von = LagerVon(quelle);
            var nach = LagerVon(ziel);
            if (von == null || nach == null || !von.IsSpawned) return;
            von.VerschiebenRpc(quelle.platz, new NetworkObjectReference(nach.NetworkObject), ziel.platz);
        }

        void ZiehenBeenden()
        {
            if (gezogen != null) gezogen.element.RemoveFromClassList("platz--gezogen");
            gezogen = null;
            if (geist != null) geist.style.display = DisplayStyle.None;
        }

        /// <summary>Kiste offen: ins andere Lager. Sonst: Rucksack ↔ Schnellleiste.</summary>
        void Schnell(PlatzAnsicht p, Lager lager)
        {
            var eigenes = Lager.Lokal;
            if (eigenes == null) return;
            if (kiste != null)
            {
                var ziel = p.vonKiste ? eigenes : kiste;
                lager.SchnellRpc(p.platz, new NetworkObjectReference(ziel.NetworkObject), 0, -1);
            }
            else if (p.platz < Lager.SchnellPlaetze)
                lager.SchnellRpc(p.platz, new NetworkObjectReference(eigenes.NetworkObject), Lager.SchnellPlaetze, -1);
            else
                lager.SchnellRpc(p.platz, new NetworkObjectReference(eigenes.NetworkObject), 0, Lager.SchnellPlaetze);
        }
    }
}
