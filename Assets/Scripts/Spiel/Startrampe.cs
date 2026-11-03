using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Startrampe mit Tank und Rakete. Tank nimmt beliebig viel Wasser an. Per Startknopf fliegt die
    /// Rakete mit bis zu so vielen Litern zum Mars, wie ihre Ladung (Upgrade) erlaubt; beim Start wird bezahlt. Bis sie zurück ist,
    /// kann nicht erneut gestartet werden (Engpass, siehe Balancing).
    /// Der Host rechnet, alle anderen bekommen Tankinhalt und Restflugzeit übertragen und zeigen
    /// den Flug daraus selbst an.
    /// </summary>
    public class Startrampe : NetworkBehaviour
    {
        public static Startrampe Instanz { get; private set; }

        public SpielWerte werte;
        [Tooltip("Das Raketen-Modell (bewegt sich beim Flug).")]
        public Transform rakete;
        [Tooltip("Hierauf klicken, um den Eimer zu leeren.")]
        public Collider tankZiel;
        [Tooltip("Hierauf klicken, um die Rakete zu starten.")]
        public Collider startknopf;

        public readonly NetworkVariable<float> tankLiter = new NetworkVariable<float>();
        /// <summary>0 = Rakete steht startbereit auf der Rampe.</summary>
        public readonly NetworkVariable<float> restFlugzeit = new NetworkVariable<float>();
        /// <summary>Gesamtdauer des aktuellen Flugs (für die Anzeige des Flugs).</summary>
        public readonly NetworkVariable<float> flugDauer = new NetworkVariable<float>();

        Vector3 raketeRuhe;
        float ladungUnterwegs;   // nur Host: Liter der fliegenden Rakete, kommen bei der Landung auf dem Mars an
        bool gelandet = true;
        const float Steigzeit = 8f;          // so lange sieht man Start bzw. Landung (höchstens die halbe Flugzeit)
        const float Beschleunigung = 12f;    // m/s² beim Aufstieg

        public bool RaketeBereit => restFlugzeit.Value <= 0f;

        void Awake()
        {
            Instanz = this;
            if (rakete != null) raketeRuhe = rakete.localPosition;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void AbgebenRpc(float liter) => TankFuellen(liter);

        /// <summary>Nur auf dem Host: Wasser in den Tank (Eimer, Pumpen).</summary>
        public void TankFuellen(float liter)
        {
            if (IsServer && liter > 0f) tankLiter.Value += liter;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void StartenRpc()
        {
            if (!RaketeBereit || tankLiter.Value <= 0f) return;
            var stand = Spielstand.Instanz;
            float ladung = Mathf.Min(tankLiter.Value, stand != null ? stand.RaketenLadung : werte.raketeLadung);
            tankLiter.Value -= ladung;
            ladungUnterwegs = ladung;
            gelandet = false;
            flugDauer.Value = stand != null ? stand.RaketenFlugzeit : werte.raketeFlugzeit;
            restFlugzeit.Value = flugDauer.Value;
            if (Spielstand.Instanz != null) Spielstand.Instanz.Einnahme(ladung * werte.preisProLiter);
        }

        void Update()
        {
            // Spielzeit (Time.deltaTime) – steht im Einzelspieler-Pausemenü still
            if (IsSpawned && IsServer && restFlugzeit.Value > 0f)
                restFlugzeit.Value = Mathf.Max(0f, restFlugzeit.Value - Time.deltaTime);
            // Halbe Flugzeit vorbei: Rakete landet auf dem Mars, das Wasser kommt an
            if (IsSpawned && IsServer && !gelandet && restFlugzeit.Value <= flugDauer.Value * 0.5f)
            {
                gelandet = true;
                if (Spielstand.Instanz != null) Spielstand.Instanz.MarsAnkunft(ladungUnterwegs);
            }
            RaketeAnzeigen();
        }

        void RaketeAnzeigen()
        {
            if (rakete == null || werte == null) return;
            float rest = restFlugzeit.Value;
            float vergangen = flugDauer.Value - rest;
            float hoehe = 0f;
            bool sichtbar = true;

            float steig = Mathf.Min(Steigzeit, flugDauer.Value * 0.5f);
            if (rest > 0f)
            {
                if (vergangen < steig) hoehe = 0.5f * Beschleunigung * vergangen * vergangen;   // Start
                else if (rest < steig) hoehe = 0.5f * Beschleunigung * rest * rest;          // Landung
                else sichtbar = false;                                                            // unterwegs
            }
            rakete.localPosition = raketeRuhe + Vector3.up * hoehe;
            if (rakete.gameObject.activeSelf != sichtbar) rakete.gameObject.SetActive(sichtbar);
        }
    }
}
