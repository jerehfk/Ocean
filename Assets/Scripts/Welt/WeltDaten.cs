using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Alle Zahlen, aus denen die Welt gebaut wird. Liegt als Datei unter Assets/Daten,
    /// damit Größen geändert werden können, ohne Code anzufassen.
    /// Längen in Metern.
    /// </summary>
    [CreateAssetMenu(menuName = "Ocean/Welt-Daten", fileName = "WeltDaten")]
    public class WeltDaten : ScriptableObject
    {
        [Header("Planet")]
        [Tooltip("Radius der Wasseroberfläche zu Spielbeginn (= Start-Pegel).")]
        public float wasserRadius = 2000f;
        [Tooltip("Wie tief das Meer außerhalb der Insel ist.")]
        public float wassertiefe = 80f;

        [Header("Insel")]
        [Tooltip("Abstand von der Inselmitte bis zur Uferlinie.")]
        public float inselRadius = 20f;
        [Tooltip("Höchster Punkt der Insel über dem Wasser (flache Kuppe in der Mitte).")]
        public float huegelHoehe = 1.2f;
        [Tooltip("Wie unregelmäßig die Uferlinie ist (0 = kreisrund, 0,3 = deutlich länglich/buchtig).")]
        [Range(0f, 0.5f)] public float uferUnregelmaessigkeit = 0.3f;
        [Tooltip("Breite des flachen Sandstrands bis zum Ufer.")]
        public float strandBreite = 8f;
        [Tooltip("Höhe des Strands direkt hinter dem Strandstreifen.")]
        public float strandHoehe = 0.5f;
        [Tooltip("Breite des flachen Schelfs unter Wasser vor dem Abfall in die Tiefe.")]
        public float schelfBreite = 10f;
        [Tooltip("Tiefe am Ende des flachen Schelfs.")]
        public float schelfTiefe = 1.5f;
        [Tooltip("Strecke, auf der der Boden vom Schelf bis zum Meeresgrund abfällt.")]
        public float abhangBreite = 60f;

        [Header("Auflösung der Modelle")]
        [Tooltip("Unterteilungen je Würfelseite für den Meeresboden.")]
        public int planetAufloesung = 64;
        [Tooltip("Unterteilungen je Würfelseite für die Wasserkugel.")]
        public int wasserAufloesung = 128;
        [Tooltip("Abstand der Gitterpunkte im Inselstück.")]
        public float inselGitterAbstand = 1.6f;

        /// <summary>Radius des Meeresgrunds.</summary>
        public float BodenRadius => wasserRadius - wassertiefe;

        /// <summary>Ende des Inselstücks: dahinter liegt nur noch Meeresgrund.</summary>
        public float InselRand => inselRadius + schelfBreite + abhangBreite;

        /// <summary>
        /// Formfaktor der Insel je Richtung (Winkel um die Inselmitte): &gt;1 = Insel reicht dort weiter hinaus.
        /// Überlagerte Wellen ergeben eine längliche, leicht buchtige Form statt eines Kreises.
        /// </summary>
        public float Form(float winkel)
        {
            float u = uferUnregelmaessigkeit;
            return 1f + u * (0.6f * Mathf.Sin(2f * winkel + 0.8f) + 0.25f * Mathf.Sin(3f * winkel + 2.1f) + 0.15f * Mathf.Sin(5f * winkel + 4.0f));
        }

        /// <summary>Bodenhöhe an einem Punkt (x, z = Abstände von der Inselmitte entlang der Oberfläche).</summary>
        public float Bodenhoehe(float x, float z)
        {
            float abstand = Mathf.Sqrt(x * x + z * z);
            float winkel = Mathf.Atan2(z, x);
            // Die Inselform verzerrt nur den Bereich der Insel; weit draußen bleibt der Meeresgrund rund.
            float faktor = Form(winkel);
            float inselAbstand = abstand / faktor;
            return Bodenhoehe(inselAbstand);
        }

        /// <summary>
        /// Höhe des Bodens relativ zur Wasseroberfläche (positiv = über Wasser),
        /// abhängig vom Abstand zur Inselmitte (entlang der Oberfläche gemessen).
        /// </summary>
        public float Bodenhoehe(float abstand)
        {
            float strandBeginn = inselRadius - strandBreite;

            if (abstand <= strandBeginn)
            {
                // Hügel: von der Strandhöhe weich bis zur Hügelspitze
                float t = Mathf.InverseLerp(strandBeginn, 0f, abstand);
                return strandHoehe + (huegelHoehe - strandHoehe) * Glatt(t);
            }
            if (abstand <= inselRadius)
            {
                // Strand: gleichmäßig flach bis zur Uferlinie
                float t = Mathf.InverseLerp(strandBeginn, inselRadius, abstand);
                return Mathf.Lerp(strandHoehe, 0f, t);
            }
            float schelfEnde = inselRadius + schelfBreite;
            if (abstand <= schelfEnde)
            {
                // Flacher Schelf: das Wasser wird langsam tiefer
                float t = Mathf.InverseLerp(inselRadius, schelfEnde, abstand);
                return Mathf.Lerp(0f, -schelfTiefe, t);
            }
            // Abhang bis knapp unter den Meeresgrund (0,5 m tiefer, damit sich
            // Inselstück und Meeresboden am Rand nicht flackernd überlagern)
            float s = Mathf.InverseLerp(schelfEnde, InselRand, abstand);
            return Mathf.Lerp(-schelfTiefe, -wassertiefe - 0.5f, Glatt(s));
        }

        /// <summary>
        /// Helligkeit des Bodens je nach Höhe: trockener Sand hell, nasser Sand am Ufer etwas dunkler,
        /// unter Wasser mit zunehmender Tiefe dunkler und blauer. Zusammen mit dem durchsichtigen
        /// Wasser ergibt das hell-türkises Flachwasser und dunkelblaue Tiefe.
        /// </summary>
        public static Color BodenFarbe(float hoehe)
        {
            var trocken = new Color(1f, 1f, 1f);
            var nass = new Color(0.86f, 0.86f, 0.84f);
            var tief = new Color(0.10f, 0.20f, 0.32f);
            if (hoehe > 0.2f) return trocken;
            if (hoehe > -0.5f) return Color.Lerp(nass, trocken, Mathf.InverseLerp(-0.5f, 0.2f, hoehe));
            return Color.Lerp(nass, tief, Glatt(Mathf.InverseLerp(-0.5f, -30f, hoehe)));
        }

        static float Glatt(float t) => t * t * (3f - 2f * t);
    }
}
