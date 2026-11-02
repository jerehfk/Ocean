using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Spielwerte für Werkzeuge, Rakete und Wirtschaft (Balancing). Liegt als Datei unter
    /// Assets/Daten/SpielWerte.asset – Werte dort ändern, nicht im Code.
    /// Ausgangswerte aus „Ozean - Balancing.py“ im Vault.
    /// </summary>
    [CreateAssetMenu(menuName = "Ocean/Spiel-Werte", fileName = "SpielWerte")]
    public class SpielWerte : ScriptableObject
    {
        [Header("Eimer")]
        public float eimerLiter = 10f;
        [Tooltip("Sekunden, bis der Eimer voll ist.")]
        public float schoepfZeit = 1f;
        [Tooltip("Wie weit man zum Schöpfen/Benutzen greifen kann (m).")]
        public float reichweite = 4f;
        [Tooltip("Lauftempo mit vollem Behälter (1 = normal).")]
        [Range(0.3f, 1f)] public float tempoBeladen = 0.9f;

        [Header("Rakete")]
        public float raketeLadung = 200f;
        [Tooltip("Sekunden für Hin- und Rückflug.")]
        public float raketeFlugzeit = 60f;

        [Header("Wirtschaft")]
        public float preisProLiter = 1f;
    }
}
