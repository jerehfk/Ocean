using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ocean
{
    /// <summary>
    /// Netzwerk-Teil der Spielfigur. Beim Erscheinen im Spiel wird entschieden:
    /// eigene Figur → Steuerung und Kamera an, eigener Körper nur als Schatten sichtbar;
    /// fremde Figur → Kamera, Ton und Steuerung aus, Körper sichtbar.
    /// Jeder Spieler bekommt eine eigene Akzentfarbe (aus seiner Spielernummer).
    /// </summary>
    public class SpielerNetz : NetworkBehaviour
    {
        public SpielerSteuerung steuerung;
        public Camera kamera;
        public AudioListener hoerer;
        public UnterwasserEffekt unterwasser;
        [Tooltip("Teile des Körpers, die der Spieler selbst nicht sieht (nur ihren Schatten).")]
        public Renderer[] koerper;
        [Tooltip("Teile, die die Spielerfarbe bekommen (z. B. Gürtel, Visier-Rand).")]
        public Renderer[] akzentTeile;

        public static readonly Color[] Farben =
        {
            new Color(0.10f, 0.75f, 0.80f), // Türkis
            new Color(1.00f, 0.80f, 0.15f), // Gelb
            new Color(0.95f, 0.35f, 0.60f), // Pink
            new Color(0.45f, 0.85f, 0.30f), // Grün
            new Color(0.95f, 0.50f, 0.15f), // Orange
            new Color(0.55f, 0.45f, 0.95f), // Lila
        };

        public Color Farbe => Farben[(int)(OwnerClientId % (ulong)Farben.Length)];

        public override void OnNetworkSpawn()
        {
            bool eigene = IsOwner;

            steuerung.enabled = eigene;
            kamera.enabled = eigene;
            hoerer.enabled = eigene;
            unterwasser.enabled = eigene;
            if (eigene)
            {
                ZumStartplatz();
                Netzwerkmenue.EigenerSpielerErschienen();
            }

            foreach (var teil in koerper)
                teil.shadowCastingMode = eigene ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", Farbe);
            foreach (var teil in akzentTeile) teil.SetPropertyBlock(block);

            name = eigene ? "Spieler (ich)" : $"Spieler {OwnerClientId}";
        }

        /// <summary>
        /// Eigene Figur an den Startplatz setzen (Platz = Spielernummer). Nötig, weil die Figur sonst
        /// im Planetenmittelpunkt entsteht – der Startplatz aus der Verbindungsfreigabe kommt
        /// beim Host nicht zuverlässig an.
        /// </summary>
        void ZumStartplatz()
        {
            var startpunkte = FindAnyObjectByType<Startpunkte>();
            if (startpunkte == null) return;
            var punkt = startpunkte.Punkt((int)OwnerClientId);

            var rb = GetComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.position = punkt.position;
            rb.rotation = punkt.rotation;
            transform.SetPositionAndRotation(punkt.position, punkt.rotation);

            var netzTransform = GetComponent<BesitzerNetzwerkTransform>();
            if (netzTransform != null) netzTransform.Teleport(punkt.position, punkt.rotation, transform.localScale);
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner) Netzwerkmenue.EigenerSpielerVerschwunden();
        }
    }
}
