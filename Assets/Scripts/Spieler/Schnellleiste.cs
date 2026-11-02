using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ocean
{
    /// <summary>
    /// Schnellleiste unten (nur für die eigene Figur): Platz 1 = Behälter (fest), Plätze 2–8 =
    /// die ersten 7 Plätze des Inventars. Wechseln mit den Tasten 1–8 oder dem Mausrad.
    /// Liegt auf dem gewählten Platz eine Pumpe, Kiste oder Rohr, läuft der Baumodus dafür.
    /// </summary>
    public class Schnellleiste : NetworkBehaviour
    {
        public const int Plaetze = Lager.SchnellPlaetze + 1;
        public static Schnellleiste Lokal { get; private set; }

        public Bauen bauen;

        static readonly Key[] Tasten = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8 };

        /// <summary>0 = Behälter, 1–7 = Inventarplatz 0–6.</summary>
        public int Gewaehlt { get; private set; }
        public bool BehaelterInDerHand => Gewaehlt == 0;

        /// <summary>Was auf dem gewählten Platz liegt (beim Behälter: nichts).</summary>
        public Stapel GewaehlterStapel => Gewaehlt > 0 && Lager.Lokal != null ? Lager.Lokal.Platz(Gewaehlt - 1) : default;

        public override void OnNetworkSpawn() { if (IsOwner) Lokal = this; }
        public override void OnNetworkDespawn() { if (Lokal == this) Lokal = null; }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var tastatur = Keyboard.current;
                if (tastatur != null)
                    for (int i = 0; i < Tasten.Length; i++)
                        if (tastatur[Tasten[i]].wasPressedThisFrame) Gewaehlt = i;

                float rad = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
                if (rad < -0.01f) Gewaehlt = (Gewaehlt + 1) % Plaetze;
                else if (rad > 0.01f) Gewaehlt = (Gewaehlt + Plaetze - 1) % Plaetze;
            }

            // Baumodus passend zum gewählten Platz
            if (bauen == null) return;
            var stapel = GewaehlterStapel;
            if (!stapel.Leer && GegenstandInfo.Platzierbar(stapel.art)) bauen.Sicherstellen(stapel.art, Gewaehlt - 1);
            else if (Bauen.Aktiv) bauen.Abbrechen();
        }
    }
}
