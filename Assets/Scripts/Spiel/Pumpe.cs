using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Aufgestellte Pumpe. Sie liefert nur, wenn
    /// 1. ihr Saugschlauch im Wasser liegt (legt der Spieler selbst; fällt er trocken, stoppt sie) und
    /// 2. ihr Auslass über Rohre mit dem Tank verbunden ist.
    /// Handpumpe: nur solange ein Spieler pumpt (Maustaste halten). Motorpumpe: von allein.
    /// Der Host rechnet; jeder Spieler zeichnet den Saugschlauch selbst.
    /// </summary>
    public class Pumpe : NetworkBehaviour
    {
        public enum Art { Hand, Motor }

        public static readonly List<Pumpe> Alle = new List<Pumpe>();

        public Art art;
        public SpielWerte werte;
        [Tooltip("Bewegliches Teil: Hebel (Hand) oder Schwungrad (Motor).")]
        public Transform beweglich;
        [Tooltip("Anschluss für Rohre (Auslass).")]
        public Anschluss auslass;
        [Tooltip("Saugschlauch vom Einlass ins Wasser.")]
        public MeshFilter saugschlauch;
        [Tooltip("Stelle, an der der Saugschlauch beginnt.")]
        public Transform einlass;

        readonly NetworkVariable<float> aktiv = new NetworkVariable<float>();       // Restzeit der Hebel-Animation
        readonly NetworkVariable<bool> imWasser = new NetworkVariable<bool>();      // Saugschlauch liegt
        readonly NetworkVariable<Vector3> schlauchEnde = new NetworkVariable<Vector3>();
        const ulong Niemand = ulong.MaxValue;
        readonly NetworkVariable<ulong> traeger = new NetworkVariable<ulong>(Niemand); // Netzwerk-Objekt des tragenden Spielers
        readonly NetworkVariable<bool> trocken = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> verbunden = new NetworkVariable<bool>();

        public Anschluss AuslassAnschluss => auslass;
        public float Leistung => art == Art.Hand ? werte.handpumpeLeistung : werte.motorpumpeLeistung;
        public string Name => art == Art.Hand ? "Handpumpe" : "Motorpumpe";
        public bool SchlauchImWasser => imWasser.Value;
        public bool Trocken => trocken.Value;
        public bool Verbunden => verbunden.Value;
        public bool WirdGetragen => traeger.Value != Niemand;
        public bool Bereit => imWasser.Value && !trocken.Value && verbunden.Value;
        public Vector3 EinlassPosition => einlass != null ? einlass.position : transform.position;

        float naechstePruefung;
        Vector3 letzteTraegerPosition;
        bool formVeraltet = true;

        public override void OnNetworkSpawn()
        {
            Alle.Add(this);
            imWasser.OnValueChanged += (_, __) => formVeraltet = true;
            schlauchEnde.OnValueChanged += (_, __) => formVeraltet = true;
            traeger.OnValueChanged += (_, __) => formVeraltet = true;
        }

        public override void OnNetworkDespawn() => Alle.Remove(this);

        // ---------- Anfragen der Spieler ----------

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void PumpenRpc(float sekunden)
        {
            if (art != Art.Hand || !Bereit) return;
            Liefern(Leistung * Mathf.Clamp(sekunden, 0f, 0.5f));
            aktiv.Value = 0.4f;
        }

        /// <summary>Spieler nimmt das Ende des Saugschlauchs in die Hand.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void SchlauchNehmenRpc(ulong spielerObjekt)
        {
            if (traeger.Value != Niemand) return;   // trägt schon jemand
            traeger.Value = spielerObjekt;
            imWasser.Value = false;
            trocken.Value = false;
        }

        /// <summary>Spieler legt das Schlauchende ins Wasser.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void SchlauchLegenRpc(Vector3 ende)
        {
            traeger.Value = Niemand;
            schlauchEnde.Value = ende;
            imWasser.Value = true;
            trocken.Value = WasserTiefe(ende) < 0.1f;
        }

        /// <summary>Spieler lässt den Schlauch fallen, ohne ihn ins Wasser zu legen.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void SchlauchLoslassenRpc() => traeger.Value = Niemand;

        // ---------- Ablauf ----------

        void Update()
        {
            if (IsSpawned && IsServer)
            {
                if (art == Art.Motor && Bereit) Liefern(Leistung * Time.deltaTime);   // Spielzeit: steht in der Einzelspieler-Pause
                if (aktiv.Value > 0f) aktiv.Value = Mathf.Max(0f, aktiv.Value - Time.deltaTime);

                // Jede Sekunde: Liegt das Schlauchende noch im Wasser? (Der Pegel sinkt.)
                if (Time.time >= naechstePruefung)
                {
                    naechstePruefung = Time.time + 1f;
                    if (imWasser.Value) trocken.Value = WasserTiefe(schlauchEnde.Value) < 0.1f;
                }
            }
            Animieren();
            SaugschlauchZeichnen();
        }

        /// <summary>Nur auf dem Host, aus dem Rohrnetz.</summary>
        public void VerbindungSetzen(bool istVerbunden)
        {
            if (IsServer && verbunden.Value != istVerbunden) verbunden.Value = istVerbunden;
        }

        void Liefern(float liter)
        {
            if (liter <= 0f) return;
            if (Startrampe.Instanz != null) Startrampe.Instanz.TankFuellen(liter);
            if (Spielstand.Instanz != null) Spielstand.Instanz.Geschoepft(liter);
        }

        void Animieren()
        {
            if (beweglich == null) return;
            if (art == Art.Motor)
            {
                if (Bereit) beweglich.Rotate(Vector3.up, 360f * Time.deltaTime, Space.Self);
            }
            else
                beweglich.localRotation = Quaternion.Euler(aktiv.Value > 0f ? Mathf.Sin(Time.time * 12f) * 30f : 0f, 0f, 0f);
        }

        /// <summary>Wassertiefe an einer Stelle (≤ 0 = trocken).</summary>
        public static float WasserTiefe(Vector3 punkt)
        {
            var schwerkraft = PlanetSchwerkraft.Instanz;
            var wasser = Wasser.Instanz;
            if (schwerkraft == null || wasser == null) return 0f;
            if (!Baugrund.BodenUnter(punkt, schwerkraft.Oben(punkt), out Vector3 boden)) return 100f;
            return wasser.Radius - schwerkraft.Hoehe(boden);
        }

        // ---------- Saugschlauch zeichnen ----------

        /// <summary>
        /// Drei Zustände: liegt im Wasser (bis zum Schlauchende, taucht ein), wird getragen
        /// (bis zur Hand des Spielers) oder liegt aufgerollt neben der Pumpe.
        /// </summary>
        void SaugschlauchZeichnen()
        {
            if (saugschlauch == null) return;
            Transform traegerTransform = null;
            if (traeger.Value != Niemand && NetworkManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(traeger.Value, out NetworkObject spieler))
                traegerTransform = spieler.transform;

            if (traegerTransform != null)
            {
                if ((traegerTransform.position - letzteTraegerPosition).sqrMagnitude < 0.0025f && !formVeraltet) return;
                letzteTraegerPosition = traegerTransform.position;
            }
            else if (!formVeraltet) return;
            formVeraltet = false;

            var schwerkraft = PlanetSchwerkraft.Instanz;
            var wasser = Wasser.Instanz;
            if (schwerkraft == null || wasser == null) return;
            Vector3 mitte = schwerkraft.transform.position;
            Vector3 anfang = EinlassPosition;
            Vector3 heraus = anfang + transform.forward * 0.35f;
            var punkte = new List<Vector3> { anfang, heraus };

            if (traegerTransform != null)
            {
                // Bis zur Hand: vor dem Körper, etwa auf Hüfthöhe
                Vector3 oben = schwerkraft.Oben(traegerTransform.position);
                Vector3 hand = traegerTransform.position + oben * 0.1f + traegerTransform.forward * 0.45f;
                AmBoden(punkte, heraus, hand, mitte, wasser.Radius);
                punkte.Add(hand);
            }
            else if (imWasser.Value)
            {
                // Schlauch sinkt: liegt am Grund bis unter die Stelle, an der er ins Wasser gelegt wurde,
                // und noch ein Stück weiter in dieselbe Richtung
                Vector3 ende = schlauchEnde.Value;
                Vector3 weiter = Vector3.ProjectOnPlane(ende - heraus, (ende - mitte).normalized).normalized;
                Vector3 ganzAussen = ende + weiter * 0.6f;
                AmBoden(punkte, heraus, ganzAussen, mitte, wasser.Radius);
                punkte.Add(AufDenBoden(ganzAussen, mitte));
            }
            else
            {
                // Aufgerollt: kurzes Stück liegt vor der Pumpe auf dem Boden
                Vector3 liegend = heraus + transform.forward * 0.5f + transform.right * 0.2f;
                AmBoden(punkte, heraus, liegend, mitte, wasser.Radius);
                punkte.Add(AufDenBoden(liegend, mitte));
            }

            for (int i = 0; i < punkte.Count; i++) punkte[i] = saugschlauch.transform.InverseTransformPoint(punkte[i]);
            saugschlauch.sharedMesh = SchlauchModell.Bauen(punkte, 0.07f);
        }

        /// <summary>
        /// Zwischenpunkte von A nach B (ohne B), jeweils auf den Boden gelegt – an Land wie unter Wasser
        /// (ein Schlauch schwimmt nicht, er sinkt auf den Grund).
        /// </summary>
        static void AmBoden(List<Vector3> punkte, Vector3 a, Vector3 b, Vector3 mitte, float wasserRadius)
        {
            int abschnitte = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / 0.4f), 2, 60);
            for (int i = 1; i < abschnitte; i++)   // ohne Endpunkt – den setzt der Aufrufer
                punkte.Add(AufDenBoden(Vector3.Lerp(a, b, (float)i / abschnitte), mitte));
        }

        /// <summary>Punkt senkrecht auf den Boden darunter legen (Schlauchdicke darüber).</summary>
        static Vector3 AufDenBoden(Vector3 punkt, Vector3 mitte)
        {
            Vector3 oben = (punkt - mitte).normalized;
            return Baugrund.BodenUnter(punkt, oben, out Vector3 boden) ? boden + oben * 0.07f : punkt;
        }
    }
}
