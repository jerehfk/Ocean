using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ocean
{
    /// <summary>
    /// Anzeige im Spiel (HUD): Geld oben, Fadenkreuz und Hinweis in der Mitte,
    /// Eimer unten, Tank und Rakete rechts, sobald man in der Nähe der Startrampe ist.
    /// Elemente stehen in Assets/UI/Menue.uxml im Block „anzeige“.
    /// </summary>
    public class Anzeige : MonoBehaviour
    {
        public UIDocument oberflaeche;
        [Tooltip("Ab dieser Entfernung zur Startrampe (m) werden Tank und Rakete angezeigt.")]
        public float rampenNaehe = 18f;

        static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

        VisualElement anzeige, eimerFuellung, rampeInfo;
        Label geld, hinweis, eimerText, tankText, raketeText;

        void Start()
        {
            var wurzel = oberflaeche.rootVisualElement;
            anzeige = wurzel.Q("anzeige");
            eimerFuellung = wurzel.Q("eimerFuellung");
            rampeInfo = wurzel.Q("rampeInfo");
            geld = wurzel.Q<Label>("geld");
            hinweis = wurzel.Q<Label>("hinweis");
            eimerText = wurzel.Q<Label>("eimerText");
            tankText = wurzel.Q<Label>("tankText");
            raketeText = wurzel.Q<Label>("raketeText");
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
            hinweis.text = spieler.Hinweis;

            eimerFuellung.style.width = Length.Percent(Mathf.Clamp01(spieler.Fuellstand) * 100f);
            eimerText.text = $"{spieler.eimerLiter.Value:0} / {spieler.werte.eimerLiter:0} l";

            var rampe = Startrampe.Instanz;
            bool nah = rampe != null && Vector3.Distance(spieler.transform.position, rampe.transform.position) < rampenNaehe;
            rampeInfo.style.display = nah ? DisplayStyle.Flex : DisplayStyle.None;
            if (nah)
            {
                tankText.text = $"Tank: {rampe.tankLiter.Value.ToString("N0", Deutsch)} l";
                raketeText.text = rampe.RaketeBereit
                    ? $"Rakete startbereit · nimmt bis {rampe.werte.raketeLadung:0} l"
                    : $"Rakete zurück in {Mathf.CeilToInt(rampe.restFlugzeit.Value)} s";
            }
        }
    }
}
