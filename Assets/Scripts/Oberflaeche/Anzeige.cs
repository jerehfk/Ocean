using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ocean
{
    /// <summary>
    /// Anzeige im Spiel (HUD): Geld oben, Fadenkreuz und Hinweis in der Mitte,
    /// Tank und Rakete rechts, sobald man in der Nähe der Startrampe ist.
    /// (Die Schnellleiste unten mit dem Behälter zeichnet das InventarFenster.)
    /// Elemente stehen in Assets/UI/Menue.uxml im Block „anzeige“.
    /// </summary>
    public class Anzeige : MonoBehaviour
    {
        public UIDocument oberflaeche;
        [Tooltip("Ab dieser Entfernung zur Startrampe (m) werden Tank und Rakete angezeigt.")]
        public float rampenNaehe = 18f;

        static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

        VisualElement anzeige, rampeInfo;
        Label geld, hinweis, tankText, raketeText, satellitStatus, satellitBanner;
        bool satellitBekannt, warGestartet, warAngekommen;
        float bannerBis;

        void Start()
        {
            var wurzel = oberflaeche.rootVisualElement;
            anzeige = wurzel.Q("anzeige");
            rampeInfo = wurzel.Q("rampeInfo");
            geld = wurzel.Q<Label>("geld");
            hinweis = wurzel.Q<Label>("hinweis");
            tankText = wurzel.Q<Label>("tankText");
            raketeText = wurzel.Q<Label>("raketeText");
            satellitStatus = wurzel.Q<Label>("satellitStatus");
            satellitBanner = wurzel.Q<Label>("satellitBanner");
        }

        /// <summary>
        /// Satellit: Start und Ankunft als Banner (der Start passiert beim ersten Bildschirmkauf von allein),
        /// dazwischen eine Zeile mit der Restflugzeit.
        /// </summary>
        void SatellitAnzeigen(Spielstand stand)
        {
            if (stand == null || !stand.IsSpawned) return;
            bool gestartet = stand.SatellitGestartet, angekommen = stand.SatellitAngekommen;
            if (!satellitBekannt)
            {
                // Erster Blick (z. B. Beitritt mitten im Spiel): nur merken, kein Banner
                satellitBekannt = true;
                warGestartet = gestartet;
                warAngekommen = angekommen;
            }
            if (gestartet && !warGestartet)
                Banner($"Satellit gestartet! Er fliegt von allein zum Mars und braucht etwa {Mathf.CeilToInt(stand.satellitFlugdauer.Value)} s. Stell inzwischen eine Satellitenschüssel auf.", 9f);
            if (angekommen && !warAngekommen)
                Banner(stand.Empfang ? "Satellit am Mars angekommen! Bildschirm in die Hand nehmen." : "Satellit am Mars angekommen! Stell eine Satellitenschüssel auf, dann zeigt der Bildschirm das Bild.", 9f);
            warGestartet = gestartet;
            warAngekommen = angekommen;

            bool unterwegs = gestartet && !angekommen;
            satellitStatus.style.display = unterwegs ? DisplayStyle.Flex : DisplayStyle.None;
            if (unterwegs) satellitStatus.text = $"Satellit unterwegs zum Mars · noch {Mathf.CeilToInt(stand.satellitRest.Value)} s";
            satellitBanner.style.display = Time.unscaledTime < bannerBis ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Banner(string text, float sekunden)
        {
            satellitBanner.text = text;
            bannerBis = Time.unscaledTime + sekunden;
        }

        void Update()
        {
            if (anzeige == null) return;
            var spieler = SpielerAktionen.Lokal;
            bool zeigen = spieler != null && Netzwerkmenue.ImSpiel && UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            anzeige.style.display = zeigen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!zeigen) return;

            var stand = Spielstand.Instanz;
            geld.text = stand != null ? stand.kasse.Value.ToString("N0", Deutsch) : "0";
            SatellitAnzeigen(stand);
            hinweis.text = Bauen.Aktiv ? Bauen.Lokal.Hinweis : spieler.Hinweis;

            var rampe = Startrampe.Instanz;
            bool nah = rampe != null && Vector3.Distance(spieler.transform.position, rampe.transform.position) < rampenNaehe;
            rampeInfo.style.display = nah ? DisplayStyle.Flex : DisplayStyle.None;
            if (nah)
            {
                tankText.text = $"Tank: {rampe.tankLiter.Value.ToString("N0", Deutsch)} l";
                raketeText.text = rampe.RaketeBereit
                    ? $"Rakete startbereit · nimmt bis {(stand != null ? stand.RaketenLadung : rampe.werte.raketeLadung):0} l"
                    : $"Rakete zurück in {Mathf.CeilToInt(rampe.restFlugzeit.Value)} s";
            }
        }
    }
}
