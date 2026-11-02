using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ocean
{
    /// <summary>
    /// Was der Spieler mit der Maus tut: Wasser schöpfen, Behälter in den Tank kippen, Rakete starten,
    /// an der Handpumpe pumpen (Maustaste halten), Kiste öffnen (E).
    /// Schöpfen und Kippen nur mit dem Behälter in der Hand (Schnellleiste Platz 1).
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
        [Tooltip("Modelle der Behälter in der Hand (Kinder der Kamera), in der Reihenfolge der Spiel-Werte.")]
        public Transform[] behaelterModelle;
        [Tooltip("Wasserfläche im jeweiligen Behälter (nur sichtbar, wenn Wasser drin ist).")]
        public GameObject[] behaelterWasser;

        public readonly NetworkVariable<float> eimerLiter = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Text unter dem Fadenkreuz, z. B. „Klick: Wasser schöpfen“.</summary>
        public string Hinweis { get; private set; } = "";
        public float Kapazitaet => Spielstand.Instanz != null ? Spielstand.Instanz.Kapazitaet : 10f;
        public float Fuellstand => Kapazitaet > 0f ? eimerLiter.Value / Kapazitaet : 0f;
        public string BehaelterName => Spielstand.Instanz != null ? Spielstand.Instanz.AktuellerBehaelter.name : "Eimer";

        const float MindestTiefe = 0.25f;   // so tief muss das Wasser mindestens sein (m)

        float schoepfen = -1f;   // < 0: schöpft gerade nicht; 0…1: Fortschritt
        float gepumpt;           // gesammelte Pump-Sekunden, die noch an den Host gehen
        Pumpe getragen;          // Pumpe, deren Saugschlauch-Ende man gerade in der Hand hält
        Vector3[] ruhe;

        void Awake()
        {
            ruhe = new Vector3[behaelterModelle.Length];
            for (int i = 0; i < ruhe.Length; i++) ruhe[i] = behaelterModelle[i].localPosition;
        }

        public override void OnNetworkSpawn() { if (IsOwner) Lokal = this; }
        public override void OnNetworkDespawn()
        {
            if (Lokal == this) Lokal = null;
            if (getragen != null && getragen.IsSpawned) getragen.SchlauchLoslassenRpc();
        }

        void Update()
        {
            EimerAnzeigen();
            if (!IsSpawned || !IsOwner) return;

            var stand = Spielstand.Instanz;
            float schuhe = stand != null ? stand.SchuhFaktor : 1f;
            float beladen = eimerLiter.Value > 0f && stand != null ? stand.AktuellerBehaelter.tempoVoll : 1f;
            steuerung.tempoFaktor = schuhe * beladen;
            Hinweis = "";
            bool inDerHand = Schnellleiste.Lokal == null || Schnellleiste.Lokal.BehaelterInDerHand;

            // Kiste öffnen geht immer (auch mit einem Gegenstand in der Hand)
            if (Cursor.lockState == CursorLockMode.Locked && getragen == null && schoepfen < 0f)
            {
                var blick = new Ray(kamera.transform.position, kamera.transform.forward);
                if (Physics.Raycast(blick, out RaycastHit kistenTreffer, werte.reichweite, ~0, QueryTriggerInteraction.Ignore))
                {
                    var kiste = kistenTreffer.collider.GetComponentInParent<Lager>();
                    if (kiste != null && kiste.istKiste && kiste.IsSpawned)
                    {
                        if (!Bauen.Aktiv) Hinweis = "E: Kiste öffnen";
                        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) { InventarFenster.KisteOeffnen(kiste); return; }
                        if (!Bauen.Aktiv) return;
                    }
                }
            }
            if (Bauen.Aktiv) return;   // im Baumodus zeigt der Baumodus seine Hinweise

            // Schöpfen läuft gerade (Spielzeit: steht im Einzelspieler-Pausemenü still)
            if (schoepfen >= 0f)
            {
                schoepfen += Time.deltaTime / Mathf.Max(0.05f, werte.schoepfZeit);
                if (schoepfen >= 1f)
                {
                    schoepfen = -1f;
                    float menge = Kapazitaet - eimerLiter.Value;
                    eimerLiter.Value = Kapazitaet;
                    if (stand != null && stand.IsSpawned) stand.GeschoepftRpc(menge);
                }
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked) return; // Pausemenü offen
            bool klick = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool gehalten = Mouse.current != null && Mouse.current.leftButton.isPressed;
            var rampe = Startrampe.Instanz;
            var strahl = new Ray(kamera.transform.position, kamera.transform.forward);
            bool trifft = Physics.Raycast(strahl, out RaycastHit treffer, werte.reichweite, ~0, QueryTriggerInteraction.Ignore);

            // 0. Saugschlauch in der Hand: ins Wasser legen oder fallen lassen
            if (getragen != null)
            {
                SchlauchTragen(strahl, trifft, treffer, klick);
                return;
            }

            // 1. Tank an der Startrampe
            if (trifft && rampe != null && treffer.collider == rampe.tankZiel)
            {
                if (!inDerHand) { Hinweis = "Taste 1: Behälter in die Hand nehmen"; return; }
                if (eimerLiter.Value > 0f)
                {
                    Hinweis = $"Klick: {BehaelterName} in den Tank kippen";
                    if (klick && rampe.IsSpawned) { rampe.AbgebenRpc(eimerLiter.Value); eimerLiter.Value = 0f; }
                }
                else Hinweis = $"Tank – dein {BehaelterName} ist leer";
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

            // 3. Pumpe
            var pumpe = trifft ? treffer.collider.GetComponentInParent<Pumpe>() : null;
            if (pumpe != null && pumpe.IsSpawned)
            {
                bool taste = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
                if (pumpe.WirdGetragen) { Hinweis = "Den Saugschlauch trägt gerade jemand"; return; }
                if (!pumpe.SchlauchImWasser || pumpe.Trocken)
                {
                    Hinweis = pumpe.Trocken ? "Saugschlauch trocken! Klick: Schlauch nehmen und weiter ins Wasser legen"
                                            : $"Klick: Saugschlauch nehmen ({werte.schlauchLaenge:0} m) und ins Wasser legen";
                    if (klick || taste) SchlauchNehmen(pumpe);
                    return;
                }
                if (!pumpe.Verbunden)
                {
                    Hinweis = "Nicht mit dem Tank verbunden – Rohr verlegen (Shop: B) · E: Saugschlauch umlegen";
                    if (taste) SchlauchNehmen(pumpe);
                    return;
                }
                if (pumpe.art == Pumpe.Art.Motor)
                {
                    Hinweis = $"Motorpumpe läuft · {pumpe.Leistung:0} l/s in den Tank · E: Saugschlauch umlegen";
                    if (taste) SchlauchNehmen(pumpe);
                    return;
                }
                Hinweis = $"Maustaste halten: pumpen ({pumpe.Leistung:0} l/s in den Tank) · E: Saugschlauch umlegen";
                if (taste) { SchlauchNehmen(pumpe); return; }
                if (gehalten)
                {
                    gepumpt += Time.deltaTime;
                    if (gepumpt >= 0.25f) { pumpe.PumpenRpc(gepumpt); gepumpt = 0f; }
                }
                return;
            }
            gepumpt = 0f;

            // 4. Wasser: liegt die Wasseroberfläche in Reichweite und vor allem anderen?
            if (inDerHand && eimerLiter.Value < Kapazitaet - 0.01f && WasserInReichweite(strahl, trifft ? treffer.distance : float.MaxValue))
            {
                Hinweis = $"Klick: Wasser schöpfen ({BehaelterName}, {Kapazitaet:0} l)";
                if (klick) schoepfen = 0f;
            }
        }

        void SchlauchNehmen(Pumpe pumpe)
        {
            getragen = pumpe;
            pumpe.SchlauchNehmenRpc(NetworkObjectId);
        }

        /// <summary>Mit dem Schlauchende in der Hand: aufs Wasser zielen und klicken; Rechtsklick lässt los.</summary>
        void SchlauchTragen(Ray strahl, bool trifft, RaycastHit treffer, bool klick)
        {
            float laenge = werte.schlauchLaenge;
            float abstandZurPumpe = Vector3.Distance(transform.position, getragen.EinlassPosition);
            if (!getragen.IsSpawned) { getragen = null; return; }
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame || abstandZurPumpe > laenge + 3f)
            {
                getragen.SchlauchLoslassenRpc();
                getragen = null;
                return;
            }

            if (WasserInReichweite(strahl, trifft ? treffer.distance : float.MaxValue, out Vector3 wasserPunkt))
            {
                float bisWasser = Vector3.Distance(getragen.EinlassPosition, wasserPunkt);
                if (bisWasser > laenge) { Hinweis = $"Schlauch zu kurz ({bisWasser:0.0} m von {laenge:0} m) · Rechtsklick: loslassen"; return; }
                Hinweis = $"Klick: Saugschlauch hier ins Wasser legen ({bisWasser:0.0} m von {laenge:0} m)";
                if (klick)
                {
                    getragen.SchlauchLegenRpc(wasserPunkt);
                    getragen = null;
                }
                return;
            }
            Hinweis = abstandZurPumpe > laenge
                ? $"Schlauch ist zu Ende ({laenge:0} m) – zurückgehen · Rechtsklick: loslassen"
                : "Saugschlauch: aufs Wasser zielen (näher als 4 m) · Rechtsklick: loslassen";
        }

        /// <summary>Schneidet der Blickstrahl die Wasserkugel innerhalb der Reichweite?</summary>
        bool WasserInReichweite(Ray strahl, float hindernis) => WasserInReichweite(strahl, hindernis, out _);

        bool WasserInReichweite(Ray strahl, float hindernis, out Vector3 wasserPunkt)
        {
            wasserPunkt = strahl.origin;
            var schwerkraft = PlanetSchwerkraft.Instanz;
            var wasser = Wasser.Instanz;
            if (schwerkraft == null || wasser == null) return false;

            Vector3 m = strahl.origin - schwerkraft.transform.position;
            float r = wasser.Radius;
            float c = Vector3.Dot(m, m) - r * r;
            if (c <= 0f) { wasserPunkt = strahl.origin; return true; }   // Kopf unter Wasser: Wasser ist überall
            float b = Vector3.Dot(m, strahl.direction);
            float d = b * b - c;
            if (b >= 0f || d < 0f) return false;      // schaut vom Wasser weg
            float abstand = -b - Mathf.Sqrt(d);
            if (abstand > werte.reichweite || abstand > hindernis) return false;

            // Ist dort wirklich Wasser? Am Ufer liegt die Wasserkugel nur hauchdünn über dem Sand –
            // erst ab etwas Wassertiefe darf geschöpft werden.
            Vector3 punkt = strahl.origin + strahl.direction * abstand;
            wasserPunkt = punkt;
            Vector3 oben = schwerkraft.Oben(punkt);
            if (Baugrund.BodenUnter(punkt, oben, out Vector3 boden) && r - schwerkraft.Hoehe(boden) < MindestTiefe)
                return false;
            return true;
        }

        void EimerAnzeigen()
        {
            if (behaelterModelle == null || behaelterModelle.Length == 0) return;
            int nummer = Spielstand.Instanz != null ? Mathf.Clamp(Spielstand.Instanz.BehaelterNummer, 0, behaelterModelle.Length - 1) : 0;
            bool voll = eimerLiter.Value > 0f || schoepfen >= 0f;
            // Fremde Figuren zeigen immer den Behälter, die eigene nur, wenn er gewählt ist
            bool inDerHand = !IsOwner || Schnellleiste.Lokal == null || Schnellleiste.Lokal.BehaelterInDerHand;
            // Beim Schöpfen taucht der Behälter kurz nach unten ein
            float eintauchen = schoepfen >= 0f ? Mathf.Sin(Mathf.PI * schoepfen) * 0.35f : 0f;

            for (int i = 0; i < behaelterModelle.Length; i++)
            {
                bool aktiv = i == nummer && inDerHand;
                if (behaelterModelle[i].gameObject.activeSelf != aktiv) behaelterModelle[i].gameObject.SetActive(aktiv);
                if (!aktiv) continue;
                behaelterModelle[i].localPosition = ruhe[i] + Vector3.down * eintauchen;
                var wasserTeil = behaelterWasser[i];
                if (wasserTeil != null && wasserTeil.activeSelf != voll) wasserTeil.SetActive(voll);
            }
        }
    }
}
