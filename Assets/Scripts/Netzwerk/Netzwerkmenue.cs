using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ocean
{
    /// <summary>
    /// Startmenü und Verbindungsaufbau. Oberfläche: Assets/UI/Menue.uxml (Aufbau) + Menue.uss (Gestaltung).
    /// „Allein spielen“ = Host nur auf diesem Rechner, niemand kann beitreten;
    /// „Spiel öffnen“ = Host, Freunde treten per IP bei; „Beitreten“ = als Gast verbinden.
    /// Teilt neuen Spielern einen Startplatz am Strand zu.
    /// Im Spiel: Esc öffnet/schließt das Pausemenü.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public class Netzwerkmenue : MonoBehaviour
    {
        public const ushort Port = 7777;

        [Tooltip("Spielfigur, die für jeden Spieler erzeugt wird.")]
        public GameObject spielerVorlage;
        [Tooltip("Kamera für das Menü, solange man noch keine Spielfigur hat.")]
        public Camera menueKamera;
        public Startpunkte startpunkte;
        [Tooltip("Oberfläche mit den Menüs.")]
        public UIDocument oberflaeche;

        static Netzwerkmenue instanz;
        NetworkManager netz;
        UnityTransport transport;
        int naechsterStartplatz;
        bool imSpiel;
        string seite = "hauptmenue";   // welche Menüseite außerhalb des Spiels gezeigt wird

        VisualElement hauptmenue, freunde, verbinden, pause;
        TextField ipFeld;
        Label meldungText, pauseInfo;

        string Meldung { set { if (meldungText != null) meldungText.text = value; } }

        void Awake()
        {
            instanz = this;
            netz = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();

            netz.NetworkConfig.PlayerPrefab = spielerVorlage;
            netz.NetworkConfig.ConnectionApproval = true;
            netz.ConnectionApprovalCallback = Zulassen;
            netz.OnClientDisconnectCallback += Getrennt;
        }

        void Start()
        {
            // In Start statt OnEnable: dann hat die Oberfläche ihre Elemente sicher schon aufgebaut
            if (oberflaeche == null) return;
            var wurzel = oberflaeche.rootVisualElement;
            hauptmenue = wurzel.Q("hauptmenue");
            freunde = wurzel.Q("freunde");
            verbinden = wurzel.Q("verbinden");
            pause = wurzel.Q("pause");
            ipFeld = wurzel.Q<TextField>("ip");
            meldungText = wurzel.Q<Label>("meldung");
            pauseInfo = wurzel.Q<Label>("pauseInfo");

            wurzel.Q<Button>("allein").clicked += AlleinSpielen;
            wurzel.Q<Button>("zuFreunden").clicked += () => { Meldung = ""; seite = "freunde"; };
            wurzel.Q<Button>("beenden").clicked += Beenden;
            wurzel.Q<Button>("oeffnen").clicked += SpielOeffnen;
            wurzel.Q<Button>("beitreten").clicked += Beitreten;
            wurzel.Q<Button>("zurueck").clicked += () => seite = "hauptmenue";
            wurzel.Q<Button>("abbrechen").clicked += Verlassen;
            wurzel.Q<Button>("weiter").clicked += () => UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            wurzel.Q<Button>("verlassen").clicked += Verlassen;
        }

        void Update()
        {
            if (hauptmenue == null) return;
            bool verbindet = netz.IsClient && !netz.IsConnectedClient;
            bool pausiert = imSpiel && UnityEngine.Cursor.lockState != CursorLockMode.Locked;

            Zeigen(hauptmenue, !imSpiel && !verbindet && seite == "hauptmenue");
            Zeigen(freunde, !imSpiel && !verbindet && seite == "freunde");
            Zeigen(verbinden, !imSpiel && verbindet);
            Zeigen(pause, pausiert);

            if (pausiert)
                pauseInfo.text = !netz.IsServer ? "Verbunden als Gast"
                    : OffenFuerFreunde() ? $"Spiel offen – deine IP: {EigeneIp()}   ·   Spieler: {netz.ConnectedClientsIds.Count}"
                    : "Allein";
        }

        static void Zeigen(VisualElement element, bool sichtbar) =>
            element.style.display = sichtbar ? DisplayStyle.Flex : DisplayStyle.None;

        void OnDestroy()
        {
            if (netz != null) netz.OnClientDisconnectCallback -= Getrennt;
        }

        // ---------- Starten ----------

        bool VorlageAnmelden()
        {
            if (spielerVorlage == null) { Meldung = "Keine Spielfigur hinterlegt."; return false; }
            if (!netz.NetworkConfig.Prefabs.Contains(spielerVorlage)) netz.AddNetworkPrefab(spielerVorlage);
            naechsterStartplatz = 0;
            return true;
        }

        void AlleinSpielen()
        {
            if (!VorlageAnmelden()) return;
            // Nur auf diesem Rechner lauschen: von außen kann niemand beitreten
            transport.SetConnectionData("127.0.0.1", Port, "127.0.0.1");
            if (!netz.StartHost()) Meldung = "Spiel konnte nicht gestartet werden.";
        }

        void SpielOeffnen()
        {
            if (!VorlageAnmelden()) return;
            // Auf allen Netzwerkkarten lauschen, damit Freunde beitreten können
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            if (!netz.StartHost()) Meldung = $"Spiel konnte nicht geöffnet werden (Port {Port} belegt?).";
        }

        void Beitreten()
        {
            if (!VorlageAnmelden()) return;
            string ip = ipFeld.value.Trim();
            transport.SetConnectionData(ip, Port);
            Meldung = "";
            if (!netz.StartClient()) Meldung = "Verbindung konnte nicht gestartet werden.";
        }

        void Beenden()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Verlassen()
        {
            netz.Shutdown();
            EigenerSpielerVerschwunden();
        }

        // ---------- Netzwerk-Ereignisse ----------

        /// <summary>Läuft beim Host für jeden, der beitreten will (auch für den Host selbst).</summary>
        void Zulassen(NetworkManager.ConnectionApprovalRequest anfrage, NetworkManager.ConnectionApprovalResponse antwort)
        {
            antwort.Approved = true;
            antwort.CreatePlayerObject = true;
            if (startpunkte != null)
            {
                var punkt = startpunkte.Punkt(naechsterStartplatz++);
                antwort.Position = punkt.position;
                antwort.Rotation = punkt.rotation;
            }
        }

        void Getrennt(ulong spielerNummer)
        {
            // Nur reagieren, wenn es um die eigene Verbindung geht (als Gast: Host weg oder abgelehnt)
            if (netz.IsServer && spielerNummer != NetworkManager.ServerClientId) return;
            Meldung = string.IsNullOrEmpty(netz.DisconnectReason) ? "Verbindung getrennt oder Host nicht gefunden." : netz.DisconnectReason;
            seite = "freunde";
            EigenerSpielerVerschwunden();
        }

        public static void EigenerSpielerErschienen()
        {
            if (instanz == null) return;
            instanz.imSpiel = true;
            instanz.Meldung = "";
            if (instanz.menueKamera != null) instanz.menueKamera.gameObject.SetActive(false);
        }

        public static void EigenerSpielerVerschwunden()
        {
            if (instanz == null) return;
            instanz.imSpiel = false;
            if (instanz.menueKamera != null) instanz.menueKamera.gameObject.SetActive(true);
            UnityEngine.Cursor.lockState = CursorLockMode.None;
        }

        // ---------- Hilfen ----------

        bool OffenFuerFreunde() => transport.ConnectionData.ServerListenAddress != "127.0.0.1";

        static string EigeneIp()
        {
            try
            {
                foreach (var adresse in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (adresse.AddressFamily == AddressFamily.InterNetwork) return adresse.ToString();
            }
            catch { }
            return "unbekannt";
        }
    }
}
