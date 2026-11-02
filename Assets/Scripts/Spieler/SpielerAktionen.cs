using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ocean
{
    /// <summary>
    /// Was der Spieler mit der Maus tut: Wasser schöpfen, Eimer in den Tank kippen, Rakete starten.
    /// Gezielt wird mit der Bildmitte (Fadenkreuz), geklickt mit der linken Maustaste.
    /// Der Eimer-Inhalt gehört dem Spieler selbst und wird an alle übertragen (damit andere
    /// sehen, ob er voll ist).
    /// </summary>
    public class SpielerAktionen : NetworkBehaviour
    {
        public static SpielerAktionen Lokal { get; private set; }

        public SpielWerte werte;
        public Camera kamera;
        public SpielerSteuerung steuerung;
        [Tooltip("Eimer-Modell (Kind der Kamera).")]
        public Transform eimer;
        [Tooltip("Wasserfläche im Eimer (nur sichtbar, wenn Wasser drin ist).")]
        public GameObject eimerWasser;

        public readonly NetworkVariable<float> eimerLiter = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Text unter dem Fadenkreuz, z. B. „Klick: Wasser schöpfen“.</summary>
        public string Hinweis { get; private set; } = "";
        public float Fuellstand => werte != null && werte.eimerLiter > 0f ? eimerLiter.Value / werte.eimerLiter : 0f;

        float schoepfen = -1f;   // < 0: schöpft gerade nicht; 0…1: Fortschritt
        Vector3 eimerRuhe;

        void Awake()
        {
            if (eimer != null) eimerRuhe = eimer.localPosition;
        }

        public override void OnNetworkSpawn() { if (IsOwner) Lokal = this; }
        public override void OnNetworkDespawn() { if (Lokal == this) Lokal = null; }

        void Update()
        {
            EimerAnzeigen();
            if (!IsSpawned || !IsOwner) return;

            steuerung.tempoFaktor = eimerLiter.Value > 0f ? werte.tempoBeladen : 1f;
            Hinweis = "";

            // Schöpfen läuft gerade (Spielzeit: steht im Einzelspieler-Pausemenü still)
            if (schoepfen >= 0f)
            {
                schoepfen += Time.deltaTime / Mathf.Max(0.05f, werte.schoepfZeit);
                if (schoepfen >= 1f)
                {
                    schoepfen = -1f;
                    eimerLiter.Value = werte.eimerLiter;
                    if (Spielstand.Instanz != null && Spielstand.Instanz.IsSpawned)
                        Spielstand.Instanz.GeschoepftRpc(werte.eimerLiter);
                }
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked) return; // Pausemenü offen
            bool klick = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            var rampe = Startrampe.Instanz;
            var strahl = new Ray(kamera.transform.position, kamera.transform.forward);
            bool trifft = Physics.Raycast(strahl, out RaycastHit treffer, werte.reichweite, ~0, QueryTriggerInteraction.Ignore);

            // 1. Tank an der Startrampe
            if (trifft && rampe != null && treffer.collider == rampe.tankZiel)
            {
                if (eimerLiter.Value > 0f)
                {
                    Hinweis = "Klick: Eimer in den Tank kippen";
                    if (klick && rampe.IsSpawned) { rampe.AbgebenRpc(eimerLiter.Value); eimerLiter.Value = 0f; }
                }
                else Hinweis = "Tank – dein Eimer ist leer";
                return;
            }

            // 2. Startknopf
            if (trifft && rampe != null && treffer.collider == rampe.startknopf)
            {
                if (!rampe.RaketeBereit) Hinweis = "Rakete ist unterwegs";
                else if (rampe.tankLiter.Value <= 0f) Hinweis = "Tank ist leer";
                else
                {
                    Hinweis = "Klick: Rakete starten";
                    if (klick && rampe.IsSpawned) rampe.StartenRpc();
                }
                return;
            }

            // 3. Wasser: liegt die Wasseroberfläche in Reichweite und vor allem anderen?
            if (eimerLiter.Value <= 0f && WasserInReichweite(strahl, trifft ? treffer.distance : float.MaxValue))
            {
                Hinweis = "Klick: Wasser schöpfen";
                if (klick) schoepfen = 0f;
            }
        }

        /// <summary>Schneidet der Blickstrahl die Wasserkugel innerhalb der Reichweite?</summary>
        bool WasserInReichweite(Ray strahl, float hindernis)
        {
            var schwerkraft = PlanetSchwerkraft.Instanz;
            var wasser = Wasser.Instanz;
            if (schwerkraft == null || wasser == null) return false;

            Vector3 m = strahl.origin - schwerkraft.transform.position;
            float r = wasser.Radius;
            float c = Vector3.Dot(m, m) - r * r;
            if (c <= 0f) return true;                 // Kopf unter Wasser: Wasser ist überall
            float b = Vector3.Dot(m, strahl.direction);
            float d = b * b - c;
            if (b >= 0f || d < 0f) return false;      // schaut vom Wasser weg
            float abstand = -b - Mathf.Sqrt(d);
            return abstand <= werte.reichweite && abstand <= hindernis;
        }

        void EimerAnzeigen()
        {
            if (eimer == null) return;
            bool voll = eimerLiter.Value > 0f || schoepfen >= 0f;
            if (eimerWasser != null && eimerWasser.activeSelf != voll) eimerWasser.SetActive(voll);
            // Beim Schöpfen taucht der Eimer kurz nach unten ein
            float eintauchen = schoepfen >= 0f ? Mathf.Sin(Mathf.PI * schoepfen) * 0.35f : 0f;
            eimer.localPosition = eimerRuhe + Vector3.down * eintauchen;
        }
    }
}
