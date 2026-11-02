using System;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Spielwerte für Werkzeuge, Upgrades, Rakete und Wirtschaft (Balancing). Liegt als Datei unter
    /// Assets/Daten/SpielWerte.asset – Werte dort ändern, nicht im Code.
    /// Ausgangswerte aus „Ozean - Balancing.py“ im Vault.
    /// </summary>
    [CreateAssetMenu(menuName = "Ocean/Spiel-Werte", fileName = "SpielWerte")]
    public class SpielWerte : ScriptableObject
    {
        [Serializable]
        public class Behaelter
        {
            public string name;
            public float liter;
            [Tooltip("Lauftempo, wenn er voll ist (1 = normal).")]
            [Range(0.2f, 1f)] public float tempoVoll;
            [Tooltip("Preis zum Freischalten (der erste ist gratis).")]
            public float preis;
        }

        [Header("Behälter (der Reihe nach freischaltbar)")]
        public Behaelter[] behaelter =
        {
            new Behaelter { name = "Eimer",      liter = 10f,  tempoVoll = 0.9f, preis = 0f },
            new Behaelter { name = "Fass",       liter = 60f,  tempoVoll = 0.6f, preis = 60f },
            new Behaelter { name = "Fass-Karre", liter = 240f, tempoVoll = 0.5f, preis = 600f },
        };
        [Tooltip("Sekunden, bis der Behälter voll ist.")]
        public float schoepfZeit = 1f;
        [Tooltip("Wie weit man zum Schöpfen/Benutzen greifen kann (m).")]
        public float reichweite = 4f;

        [Header("Größerer Behälter (je Behälter neu, Preis × Liter/10)")]
        public float groesseBonus = 0.2f;
        public float groessePreis = 20f;
        public float groesseFaktor = 1.6f;
        public int groesseMax = 10;

        [Header("Schuhe")]
        public float schuheBonus = 0.1f;
        public float schuhePreis = 40f;
        public float schuheFaktor = 2.2f;
        public int schuheMax = 5;

        [Header("Handpumpe (pumpt, solange man an ihr steht)")]
        public float handpumpeLeistung = 30f;
        public float handpumpePreis = 2000f;
        public float handpumpeFaktor = 1.5f;

        [Header("Motorpumpe (läuft von allein)")]
        public float motorpumpeLeistung = 10f;
        public float motorpumpePreis = 5000f;
        public float motorpumpeFaktor = 1.12f;

        [Header("Saugschlauch und Rohre")]
        [Tooltip("Länge des Saugschlauchs jeder Pumpe (m).")]
        public float schlauchLaenge = 8f;
        [Tooltip("Preis je Meter Rohr.")]
        public float rohrPreisProMeter = 4f;
        [Tooltip("Längstes Rohrstück, das man auf einmal legen kann (m).")]
        public float rohrMaxLaenge = 20f;

        [Header("Rakete")]
        public float raketeLadung = 200f;
        [Tooltip("Sekunden für Hin- und Rückflug.")]
        public float raketeFlugzeit = 60f;
        public float ladungMultiplikator = 1.5f;
        public float ladungPreis = 100f;
        public float ladungFaktor = 1.7f;
        public float flugMultiplikator = 0.85f;
        public float flugPreis = 250f;
        public float flugFaktor = 2.2f;
        public int flugMax = 8;

        [Header("Wirtschaft")]
        public float preisProLiter = 1f;
    }
}
