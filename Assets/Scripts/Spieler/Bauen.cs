using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ocean
{
    /// <summary>
    /// Baumodus. Pumpen: durchsichtige Vorschau am Fadenkreuz, grün = erlaubt, rot = nicht erlaubt;
    /// Linksklick = aufstellen (erst dann wird bezahlt), R = drehen. Erlaubt: fester Boden (Insel, Steg,
    /// Meeresboden), über dem Wasser, nichts im Weg. Den Saugschlauch legt man danach selbst.
    /// Rohre: Klick = Anfang, Klick = Ende (rastet an Anschlüssen ein), danach geht es am Ende weiter.
    /// Rechtsklick oder Esc beendet den Baumodus.
    /// </summary>
    public class Bauen : NetworkBehaviour
    {
        public static Bauen Lokal { get; private set; }
        public static bool Aktiv => Lokal != null && Lokal.art >= 0;

        public Camera kamera;
        public Material vorschauErlaubt;
        public Material vorschauVerboten;
        public float bauReichweite = 12f;
        public float wasserAbstand = 3f;

        public string Hinweis { get; private set; } = "";

        int art = -1;             // 0 = Handpumpe, 1 = Motorpumpe, 2 = Rohr, -1 = nicht im Baumodus
        GameObject vorschau;
        MeshRenderer[] vorschauTeile;
        float drehung;
        Vector3 blickZumWasser;   // geglättete Richtung zum Wasser (gegen Wackeln)

        // Rohrbau
        bool rohrAnfangGesetzt;
        Vector3 rohrAnfang;
        MeshFilter rohrVorschau;
        MeshRenderer rohrVorschauRenderer;
        const float Einrastumkreis = 1.2f;

        public override void OnNetworkSpawn() { if (IsOwner) Lokal = this; }
        public override void OnNetworkDespawn() { if (Lokal == this) Lokal = null; Abbrechen(); }

        public void Starten(int neueArt)
        {
            Abbrechen();
            var stand = Spielstand.Instanz;
            if (stand == null) return;
            art = neueArt;
            if (art == 2)
            {
                vorschau = new GameObject("Rohr-Vorschau");
                rohrVorschau = vorschau.AddComponent<MeshFilter>();
                rohrVorschauRenderer = vorschau.AddComponent<MeshRenderer>();
                rohrVorschauRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rohrAnfangGesetzt = false;
                return;
            }
            var vorlage = art == 0 ? stand.handpumpeVorlage : stand.motorpumpeVorlage;
            if (vorlage == null) { art = -1; return; }
            vorschau = VorschauBauen(vorlage);
        }

        public void Abbrechen()
        {
            art = -1;
            blickZumWasser = Vector3.zero;
            Hinweis = "";
            if (vorschau != null) Destroy(vorschau);
            vorschau = null;
        }

        void Update()
        {
            if (!IsOwner || art < 0) return;

            // Pausemenü geöffnet → Bauen abbrechen
            if (Cursor.lockState != CursorLockMode.Locked) { if (!ShopFenster.Offen) Abbrechen(); return; }
            var maus = Mouse.current;
            if (maus != null && maus.rightButton.wasPressedThisFrame) { Abbrechen(); return; }
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) drehung += 45f;

            if (art == 2) { RohrBauen(maus); return; }

            var stand = Spielstand.Instanz;
            var schwerkraft = PlanetSchwerkraft.Instanz;
            var u = art == 0 ? Upgrade.Handpumpe : Upgrade.Motorpumpe;
            string name = art == 0 ? "Handpumpe" : "Motorpumpe";

            var strahl = new Ray(kamera.transform.position, kamera.transform.forward);
            if (!Physics.Raycast(strahl, out RaycastHit treffer, bauReichweite, ~0, QueryTriggerInteraction.Ignore))
            {
                vorschau.SetActive(false);
                Hinweis = $"{name}: auf den Boden zielen · Rechtsklick abbrechen";
                return;
            }

            Vector3 position = treffer.point;
            Vector3 oben = schwerkraft.Oben(position);
            // Einlass (Vorderseite) zeigt automatisch zum Wasser; R dreht zusätzlich.
            // Die Richtung ist ein Mittelwert über viele Messpunkte (zeigt quer zur Uferlinie)
            // und wird zeitlich geglättet, damit die Vorschau nicht hin- und herspringt.
            Vector3 ziel = WasserRichtung(position, oben);
            if (ziel.sqrMagnitude < 0.001f) ziel = Vector3.ProjectOnPlane(kamera.transform.forward, oben).normalized;
            if (blickZumWasser.sqrMagnitude < 0.001f) blickZumWasser = ziel;
            blickZumWasser = Vector3.Slerp(blickZumWasser, ziel, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            Vector3 vor = Vector3.ProjectOnPlane(blickZumWasser, oben);
            if (vor.sqrMagnitude < 0.001f) vor = Vector3.ProjectOnPlane(kamera.transform.up, oben);
            Quaternion ausrichtung = Quaternion.LookRotation(Quaternion.AngleAxis(drehung, oben) * vor.normalized, oben);

            string grund = Pruefen(treffer, position);
            double preis = System.Math.Round(stand.Preis(u));
            if (grund == null && stand.kasse.Value < preis) grund = "Nicht genug Münzen";
            bool erlaubt = grund == null;

            vorschau.SetActive(true);
            vorschau.transform.SetPositionAndRotation(position, ausrichtung);
            foreach (var teil in vorschauTeile) teil.sharedMaterial = erlaubt ? vorschauErlaubt : vorschauVerboten;

            Hinweis = erlaubt
                ? $"Klick: {name} aufstellen ({preis:N0} Münzen) · R drehen · Rechtsklick abbrechen"
                : $"{grund} · Rechtsklick abbrechen";

            if (erlaubt && maus != null && maus.leftButton.wasPressedThisFrame && stand.IsSpawned)
            {
                stand.PlatzierenRpc(art, position, ausrichtung);
                Abbrechen();
            }
        }

        /// <summary>Gibt den Grund zurück, warum hier nicht gebaut werden darf – oder null, wenn alles passt.</summary>
        string Pruefen(RaycastHit treffer, Vector3 position)
        {
            if (treffer.collider.GetComponentInParent<Baugrund>() == null) return "Hier kann nichts stehen";

            var schwerkraft = PlanetSchwerkraft.Instanz;
            var wasser = Wasser.Instanz;
            if (wasser != null && schwerkraft.Hoehe(position) < wasser.Radius + 0.05f) return "Nicht im Wasser aufstellen";

            Vector3 oben = schwerkraft.Oben(position);
            foreach (var anderes in Physics.OverlapSphere(position + oben * 0.8f, 0.5f, ~0, QueryTriggerInteraction.Ignore))
                if (anderes.GetComponentInParent<Baugrund>() == null) return "Hier ist kein Platz";
            return null;
        }

        /// <summary>
        /// Rohre legen: erster Klick setzt den Anfang, jeder weitere ein Ende (und dort den nächsten Anfang).
        /// In der Nähe eines Anschlusses (Tank, Pumpe, Rohrende) rastet der Punkt dort ein.
        /// </summary>
        void RohrBauen(Mouse maus)
        {
            var stand = Spielstand.Instanz;
            var strahl = new Ray(kamera.transform.position, kamera.transform.forward);
            bool trifft = Physics.Raycast(strahl, out RaycastHit treffer, bauReichweite, ~0, QueryTriggerInteraction.Ignore);

            Vector3 punkt = trifft ? treffer.point : strahl.GetPoint(bauReichweite);
            var anschluss = Anschluss.Naechster(punkt, Einrastumkreis);
            bool eingerastet = anschluss != null;
            if (eingerastet) punkt = anschluss.transform.position;
            else if (trifft && treffer.collider.GetComponentInParent<Baugrund>() != null)
                punkt = treffer.point + PlanetSchwerkraft.Instanz.Oben(treffer.point) * (Rohr.Radius + 0.02f);
            bool gueltigerPunkt = eingerastet || (trifft && treffer.collider.GetComponentInParent<Baugrund>() != null);

            if (!rohrAnfangGesetzt)
            {
                rohrVorschau.sharedMesh = null;
                Hinweis = !gueltigerPunkt ? "Rohr: auf den Boden oder einen Anschluss zielen · Rechtsklick beenden"
                        : eingerastet ? "Klick: Rohr hier am Anschluss beginnen · Rechtsklick beenden"
                        : "Klick: Rohr hier beginnen (besser an einem Anschluss) · Rechtsklick beenden";
                if (gueltigerPunkt && maus != null && maus.leftButton.wasPressedThisFrame)
                {
                    rohrAnfang = punkt;
                    rohrAnfangGesetzt = true;
                }
                return;
            }

            float laenge = Vector3.Distance(rohrAnfang, punkt);
            double preis = stand.RohrPreis(laenge);
            string grund = !gueltigerPunkt ? "Auf den Boden oder einen Anschluss zielen"
                         : laenge < 0.3f ? "Zu kurz"
                         : laenge > stand.werte.rohrMaxLaenge ? $"Zu lang (höchstens {stand.werte.rohrMaxLaenge:0} m am Stück)"
                         : stand.kasse.Value < preis ? "Nicht genug Münzen"
                         : null;

            if (laenge >= 0.05f)
            {
                rohrVorschau.sharedMesh = SchlauchModell.Bauen(Rohr.Verlauf(rohrAnfang, punkt), Rohr.Radius, 10);
                rohrVorschauRenderer.sharedMaterial = grund == null ? vorschauErlaubt : vorschauVerboten;
            }
            Hinweis = grund == null
                ? $"Klick: Rohr legen ({laenge:0.0} m, {preis:N0} Münzen){(eingerastet ? " · rastet am Anschluss ein" : "")} · Rechtsklick beenden"
                : $"{grund} · Rechtsklick beenden";

            if (grund == null && maus != null && maus.leftButton.wasPressedThisFrame && stand.IsSpawned)
            {
                stand.RohrLegenRpc(rohrAnfang, punkt);
                rohrAnfang = punkt;   // gleich weiterbauen vom Ende aus
            }
        }

        /// <summary>
        /// Mittlere Richtung zum Wasser: Messpunkte auf drei Kreisen rundherum; jede Wasserstelle
        /// zieht in ihre Richtung, nahe stärker als ferne. Ergebnis zeigt quer zur Uferlinie.
        /// </summary>
        Vector3 WasserRichtung(Vector3 position, Vector3 oben)
        {
            var schwerkraft = PlanetSchwerkraft.Instanz;
            var wasser = Wasser.Instanz;
            if (wasser == null) return Vector3.zero;

            Vector3 querA = Vector3.Cross(oben, Vector3.forward);
            if (querA.sqrMagnitude < 0.01f) querA = Vector3.Cross(oben, Vector3.right);
            querA.Normalize();
            Vector3 querB = Vector3.Cross(oben, querA);

            Vector3 summe = Vector3.zero;
            float[] radien = { wasserAbstand * 0.5f, wasserAbstand, wasserAbstand * 1.6f };
            foreach (float r in radien)
            for (int i = 0; i < 32; i++)
            {
                float w = i * Mathf.PI * 2f / 32f;
                Vector3 seitlich = Mathf.Cos(w) * querA + Mathf.Sin(w) * querB;
                Vector3 probe = position + seitlich * r;
                if (IstWasser(probe, schwerkraft, wasser)) summe += seitlich / r;
            }
            return Vector3.ProjectOnPlane(summe, oben).normalized;
        }

        static bool IstWasser(Vector3 probe, PlanetSchwerkraft schwerkraft, Wasser wasser)
        {
            if (!Baugrund.BodenUnter(probe, schwerkraft.Oben(probe), out Vector3 boden)) return true;
            return schwerkraft.Hoehe(boden) < wasser.Radius;
        }

        /// <summary>Liegt im Umkreis Boden unter der Wasseroberfläche (also Wasser)? Liefert die nächste solche Stelle.</summary>
        bool WasserInDerNaehe(Vector3 position, Vector3 oben, out Vector3 wasserPunkt)
        {
            wasserPunkt = position;
            var schwerkraft = PlanetSchwerkraft.Instanz;
            var wasser = Wasser.Instanz;
            if (wasser == null) return false;

            Vector3 querA = Vector3.Cross(oben, Vector3.forward);
            if (querA.sqrMagnitude < 0.01f) querA = Vector3.Cross(oben, Vector3.right);
            querA.Normalize();
            Vector3 querB = Vector3.Cross(oben, querA);

            for (int ring = 1; ring <= 6; ring++)          // von innen nach außen: erster Treffer ist der nächste
            {
                float r = wasserAbstand * ring / 6f;
                for (int i = 0; i < 16; i++)
                {
                    float w = i * Mathf.PI * 2f / 16f;
                    Vector3 probe = position + (Mathf.Cos(w) * querA + Mathf.Sin(w) * querB) * r;
                    // Von oben nach dem Boden suchen; liegt er unter dem Wasserspiegel, ist dort Wasser
                    if (IstWasser(probe, schwerkraft, wasser))
                    {
                        wasserPunkt = probe;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Durchsichtige Kopie der sichtbaren Teile der Vorlage (ohne Netzwerk und Kollision).</summary>
        GameObject VorschauBauen(GameObject vorlage)
        {
            var wurzel = new GameObject("Vorschau");
            var teile = vorlage.GetComponentsInChildren<MeshRenderer>(true);
            vorschauTeile = new MeshRenderer[teile.Length];
            for (int i = 0; i < teile.Length; i++)
            {
                var quelle = teile[i];
                var teil = new GameObject(quelle.name);
                teil.transform.SetParent(wurzel.transform, false);
                // Vorlage steht im Ursprung → Weltwerte der Vorlage = Werte relativ zur Wurzel
                teil.transform.localPosition = vorlage.transform.InverseTransformPoint(quelle.transform.position);
                teil.transform.localRotation = Quaternion.Inverse(vorlage.transform.rotation) * quelle.transform.rotation;
                teil.transform.localScale = quelle.transform.lossyScale;
                teil.AddComponent<MeshFilter>().sharedMesh = quelle.GetComponent<MeshFilter>().sharedMesh;
                var r = teil.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.sharedMaterial = vorschauErlaubt;
                vorschauTeile[i] = r;
            }
            return wurzel;
        }
    }
}
