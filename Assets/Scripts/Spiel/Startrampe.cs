using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Startrampe mit Tank und Rakete. Tank nimmt beliebig viel Wasser an. Per Startknopf fliegt die
    /// Rakete mit bis zu „raketeLadung“ Litern zum Mars; beim Start wird bezahlt. Bis sie zurück ist,
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

        Vector3 raketeRuhe;
        const float Steigzeit = 8f;          // so lange sieht man Start bzw. Landung
        const float Beschleunigung = 12f;    // m/s² beim Aufstieg

        public bool RaketeBereit => restFlugzeit.Value <= 0f;

        void Awake()
        {
            Instanz = this;
            if (rakete != null) raketeRuhe = rakete.localPosition;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void AbgebenRpc(float liter)
        {
            if (liter > 0f) tankLiter.Value += liter;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void StartenRpc()
        {
            if (!RaketeBereit || tankLiter.Value <= 0f) return;
            float ladung = Mathf.Min(tankLiter.Value, werte.raketeLadung);
            tankLiter.Value -= ladung;
            restFlugzeit.Value = werte.raketeFlugzeit;
            if (Spielstand.Instanz != null) Spielstand.Instanz.Einnahme(ladung * werte.preisProLiter);
        }

        void Update()
        {
            // Spielzeit (Time.deltaTime) – steht im Einzelspieler-Pausemenü still
            if (IsSpawned && IsServer && restFlugzeit.Value > 0f)
                restFlugzeit.Value = Mathf.Max(0f, restFlugzeit.Value - Time.deltaTime);
            RaketeAnzeigen();
        }

        void RaketeAnzeigen()
        {
            if (rakete == null || werte == null) return;
            float rest = restFlugzeit.Value;
            float vergangen = werte.raketeFlugzeit - rest;
            float hoehe = 0f;
            bool sichtbar = true;

            if (rest > 0f)
            {
                if (vergangen < Steigzeit) hoehe = 0.5f * Beschleunigung * vergangen * vergangen;   // Start
                else if (rest < Steigzeit) hoehe = 0.5f * Beschleunigung * rest * rest;          // Landung
                else sichtbar = false;                                                            // unterwegs
            }
            rakete.localPosition = raketeRuhe + Vector3.up * hoehe;
            if (rakete.gameObject.activeSelf != sichtbar) rakete.gameObject.SetActive(sichtbar);
        }
    }
}
