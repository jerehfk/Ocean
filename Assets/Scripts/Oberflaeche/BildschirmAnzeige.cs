using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ocean
{
    /// <summary>
    /// Bildschirm in der Hand: unten rechts das Livebild des Satelliten am Mars, Rechtsklick
    /// schaltet auf groß. Ohne Satellit am Ziel oder ohne Satellitenschüssel steht dort, was fehlt.
    /// </summary>
    public class BildschirmAnzeige : MonoBehaviour
    {
        public UIDocument oberflaeche;

        static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

        VisualElement rahmen, bild;
        Label status, info;
        bool gross;
        RenderTexture gesetzt;

        void Start()
        {
            var wurzel = oberflaeche.rootVisualElement;
            rahmen = wurzel.Q("bildschirmHand");
            bild = wurzel.Q("bildschirmBild");
            status = wurzel.Q<Label>("bildschirmStatus");
            info = wurzel.Q<Label>("bildschirmInfo");
        }

        void Update()
        {
            if (rahmen == null) return;
            var schnell = Schnellleiste.Lokal;
            bool inDerHand = schnell != null && schnell.GewaehlterStapel.art == Gegenstand.Bildschirm
                             && Netzwerkmenue.ImSpiel && UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            if (!inDerHand) gross = false;
            else if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) gross = !gross;

            rahmen.style.display = inDerHand ? DisplayStyle.Flex : DisplayStyle.None;
            rahmen.EnableInClassList("bildschirm-hand--gross", gross);
            if (!inDerHand) return;

            var stand = Spielstand.Instanz;
            var mars = Mars.Instanz;
            if (stand == null || mars == null) return;

            string meldung = !stand.SatellitGestartet ? "Kein Satellit"
                : !stand.SatellitAngekommen ? $"Satellit fliegt zum Mars · noch {Mathf.CeilToInt(stand.satellitRest.Value)} s"
                : !stand.Empfang ? "Kein Empfang – Satellitenschüssel aufstellen"
                : null;
            bool live = meldung == null;

            if (live)
            {
                mars.Anfordern();
                if (gesetzt != mars.Bild)
                {
                    gesetzt = mars.Bild;
                    bild.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(gesetzt));
                }
            }
            bild.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
            status.text = meldung ?? "";
            status.style.display = live ? DisplayStyle.None : DisplayStyle.Flex;

            // Fortschritt: welcher Krater sich füllt und wie voll er ist
            double gesamt = stand.marsLiter.Value;
            double proKrater = stand.KraterLiter;
            int krater = stand.KraterNummer(gesamt);
            double imKrater = System.Math.Min(proKrater, gesamt - krater * proKrater);
            info.text = $"Krater {krater + 1}: {imKrater / proKrater * 100.0:0} %  ·  Mars gesamt: {(gesamt / 1000.0).ToString("N1", Deutsch)} m³"
                        + (gross ? "  ·  Rechtsklick: klein" : "  ·  Rechtsklick: groß");
        }
    }
}
