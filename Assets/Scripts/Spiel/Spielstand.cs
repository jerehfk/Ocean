using System;
using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    public enum Upgrade { Behaelter, Groesse, Schuhe, Handpumpe, Motorpumpe, RaketenLadung, RaketenFlugzeit, Rohr, Kiste }

    /// <summary>
    /// Gemeinsamer Spielstand, vom Host verwaltet und an alle verteilt:
    /// Kasse (gehört allen), geschöpfte Liter (daraus der Pegel) und die Stufen aller Upgrades.
    /// Upgrades gelten für alle Spieler. Kaufen und Aufstellen laufen über den Host.
    /// Pumpen, Rohre und Kisten landen beim Kauf im Inventar des Käufers (nur wenn Platz ist)
    /// und werden von dort aufgestellt bzw. verlegt.
    /// </summary>
    public class Spielstand : NetworkBehaviour
    {
        public static Spielstand Instanz { get; private set; }

        public SpielWerte werte;
        public GameObject handpumpeVorlage;
        public GameObject motorpumpeVorlage;
        public GameObject rohrVorlage;
        public GameObject kisteVorlage;

        public readonly NetworkVariable<double> kasse = new NetworkVariable<double>();
        public readonly NetworkVariable<double> geschoepftLiter = new NetworkVariable<double>();

        // Stufen (Netcode erkennt nur einzelne Felder, keine Listen – daher je ein Feld)
        readonly NetworkVariable<int> behaelterStufe = new NetworkVariable<int>();
        readonly NetworkVariable<int> groesseStufe = new NetworkVariable<int>();
        readonly NetworkVariable<int> schuheStufe = new NetworkVariable<int>();
        readonly NetworkVariable<int> handpumpen = new NetworkVariable<int>();
        readonly NetworkVariable<int> motorpumpen = new NetworkVariable<int>();
        readonly NetworkVariable<int> ladungStufe = new NetworkVariable<int>();
        readonly NetworkVariable<int> flugStufe = new NetworkVariable<int>();

        Wasser wasser;
        double startRadius;

        void Awake() => Instanz = this;

        public override void OnNetworkSpawn()
        {
            wasser = FindAnyObjectByType<Wasser>();
            startRadius = wasser != null ? wasser.Radius : 0.0;
            geschoepftLiter.OnValueChanged += PegelGeaendert;
            PegelAnwenden(geschoepftLiter.Value);
        }

        public override void OnNetworkDespawn()
        {
            geschoepftLiter.OnValueChanged -= PegelGeaendert;
            if (wasser != null) wasser.Radius = (float)startRadius; // nach dem Verlassen wieder volles Meer
        }

        // ---------- Werte, die sich aus den Stufen ergeben ----------

        public SpielWerte.Behaelter AktuellerBehaelter => werte.behaelter[Mathf.Clamp(behaelterStufe.Value, 0, werte.behaelter.Length - 1)];
        public int BehaelterNummer => behaelterStufe.Value;
        public float Kapazitaet => AktuellerBehaelter.liter * (1f + werte.groesseBonus * groesseStufe.Value);
        public float SchuhFaktor => 1f + werte.schuheBonus * schuheStufe.Value;
        public float RaketenLadung => werte.raketeLadung * Mathf.Pow(werte.ladungMultiplikator, ladungStufe.Value);
        public float RaketenFlugzeit => werte.raketeFlugzeit * Mathf.Pow(werte.flugMultiplikator, flugStufe.Value);

        public int Stufe(Upgrade u) => u switch
        {
            Upgrade.Behaelter => behaelterStufe.Value,
            Upgrade.Groesse => groesseStufe.Value,
            Upgrade.Schuhe => schuheStufe.Value,
            Upgrade.Handpumpe => handpumpen.Value,
            Upgrade.Motorpumpe => motorpumpen.Value,
            Upgrade.RaketenLadung => ladungStufe.Value,
            Upgrade.RaketenFlugzeit => flugStufe.Value,
            _ => 0,   // Rohr, Kiste: keine Stufen
        };

        public bool Maximal(Upgrade u) => u switch
        {
            Upgrade.Behaelter => behaelterStufe.Value >= werte.behaelter.Length - 1,
            Upgrade.Groesse => groesseStufe.Value >= werte.groesseMax,
            Upgrade.Schuhe => schuheStufe.Value >= werte.schuheMax,
            Upgrade.RaketenFlugzeit => flugStufe.Value >= werte.flugMax,
            _ => false,
        };

        public double Preis(Upgrade u)
        {
            int s = Stufe(u);
            switch (u)
            {
                case Upgrade.Behaelter: return Maximal(u) ? 0 : werte.behaelter[s + 1].preis;
                case Upgrade.Groesse: return werte.groessePreis * (AktuellerBehaelter.liter / 10f) * Math.Pow(werte.groesseFaktor, s);
                case Upgrade.Schuhe: return werte.schuhePreis * Math.Pow(werte.schuheFaktor, s);
                case Upgrade.Handpumpe: return werte.handpumpePreis * Math.Pow(werte.handpumpeFaktor, s);
                case Upgrade.Motorpumpe: return werte.motorpumpePreis * Math.Pow(werte.motorpumpeFaktor, s);
                case Upgrade.RaketenLadung: return werte.ladungPreis * Math.Pow(werte.ladungFaktor, s);
                case Upgrade.RaketenFlugzeit: return werte.flugPreis * Math.Pow(werte.flugFaktor, s);
                case Upgrade.Rohr: return werte.rohrPreisProMeter * werte.rohrPaket;   // ein Paket Meter
                case Upgrade.Kiste: return werte.kistePreis;
            }
            return 0;
        }

        public bool Leistbar(Upgrade u) => !Maximal(u) && kasse.Value >= Math.Round(Preis(u));

        // ---------- Nur auf dem Host ----------

        public void Einnahme(double betrag)
        {
            if (IsServer) kasse.Value += betrag;
        }

        public void Geschoepft(double liter)
        {
            if (IsServer && liter > 0) geschoepftLiter.Value += liter;
        }

        bool Bezahlen(Upgrade u)
        {
            if (!Leistbar(u)) return false;
            kasse.Value -= Math.Round(Preis(u));
            return true;
        }

        // ---------- Anfragen der Spieler ----------

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void GeschoepftRpc(float liter) => Geschoepft(liter);

        /// <summary>Kauf eines Upgrades ohne Aufstellen (alles außer Pumpen).</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void KaufenRpc(int upgrade)
        {
            var u = (Upgrade)upgrade;
            if (GegenstandInfo.AusUpgrade(u) != Gegenstand.Nichts) return;   // Gegenstände: GegenstandKaufenRpc
            if (!Bezahlen(u)) return;
            switch (u)
            {
                case Upgrade.Behaelter: behaelterStufe.Value++; groesseStufe.Value = 0; break; // neuer Behälter, Größe beginnt neu
                case Upgrade.Groesse: groesseStufe.Value++; break;
                case Upgrade.Schuhe: schuheStufe.Value++; break;
                case Upgrade.RaketenLadung: ladungStufe.Value++; break;
                case Upgrade.RaketenFlugzeit: flugStufe.Value++; break;
            }
        }

        /// <summary>Pumpe, Rohr oder Kiste kaufen – nur, wenn im Inventar des Käufers Platz ist.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void GegenstandKaufenRpc(int upgrade, RpcParams anfrage = default)
        {
            var u = (Upgrade)upgrade;
            var g = GegenstandInfo.AusUpgrade(u);
            var lager = SpielerLager(anfrage.Receive.SenderClientId);
            if (g == Gegenstand.Nichts || lager == null) return;
            int menge = g == Gegenstand.Rohr ? werte.rohrPaket : 1;
            if (!lager.Passt(g, menge) || !Bezahlen(u)) return;
            lager.Hinzufuegen(g, menge);
            if (u == Upgrade.Handpumpe) handpumpen.Value++;
            else if (u == Upgrade.Motorpumpe) motorpumpen.Value++;
        }

        /// <summary>Pumpe oder Kiste vom gewählten Schnellleisten-Platz aufstellen.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void PlatzierenRpc(int platz, Vector3 position, Quaternion drehung, RpcParams anfrage = default)
        {
            var lager = SpielerLager(anfrage.Receive.SenderClientId);
            if (lager == null) return;
            var vorlage = lager.Platz(platz).art switch
            {
                Gegenstand.Handpumpe => handpumpeVorlage,
                Gegenstand.Motorpumpe => motorpumpeVorlage,
                Gegenstand.Kiste => kisteVorlage,
                _ => null,
            };
            if (vorlage == null || !lager.EinsNehmen(platz, out _)) return;
            var objekt = Instantiate(vorlage, position, drehung);
            objekt.GetComponent<NetworkObject>().Spawn();
        }

        /// <summary>Rohrstück von A nach B legen; verbraucht die Meter (aufgerundet) aus dem Inventar.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void RohrLegenRpc(Vector3 anfang, Vector3 ende, RpcParams anfrage = default)
        {
            float laenge = Vector3.Distance(anfang, ende);
            var lager = SpielerLager(anfrage.Receive.SenderClientId);
            if (rohrVorlage == null || lager == null || laenge < 0.3f || laenge > werte.rohrMaxLaenge + 0.5f) return;
            if (!lager.Entfernen(Gegenstand.Rohr, RohrMeter(laenge))) return;
            var objekt = Instantiate(rohrVorlage);
            var rohr = objekt.GetComponent<Rohr>();
            rohr.anfang.Value = anfang;
            rohr.ende.Value = ende;
            objekt.GetComponent<NetworkObject>().Spawn();
        }

        /// <summary>So viele Meter Rohr verbraucht ein Stück dieser Länge.</summary>
        public static int RohrMeter(float laenge) => Mathf.Max(1, Mathf.CeilToInt(laenge - 0.01f));

        Lager SpielerLager(ulong spieler) =>
            NetworkManager.ConnectedClients.TryGetValue(spieler, out var client) && client.PlayerObject != null
                ? client.PlayerObject.GetComponent<Lager>() : null;

        // Rohrnetz regelmäßig neu berechnen (welche Pumpen sind mit dem Tank verbunden?)
        float naechsteNetzberechnung;

        void Update()
        {
            if (!IsSpawned || !IsServer || Time.unscaledTime < naechsteNetzberechnung) return;
            naechsteNetzberechnung = Time.unscaledTime + 0.5f;
            Rohrnetz.Berechnen(Pumpe.Alle);
        }

        // ---------- Pegel ----------

        /// <summary>Wie viele Meter der Pegel bisher gesunken ist.</summary>
        public double PegelSenkung => startRadius - NeuerRadius(geschoepftLiter.Value);

        void PegelGeaendert(double alt, double neu) => PegelAnwenden(neu);

        void PegelAnwenden(double liter)
        {
            if (wasser != null) wasser.Radius = (float)NeuerRadius(liter);
        }

        /// <summary>Radius der Wasserkugel, nachdem so viele Liter entnommen wurden (Kugelschale).</summary>
        double NeuerRadius(double liter)
        {
            double kubikmeter = liter / 1000.0;
            double r3 = startRadius * startRadius * startRadius - 3.0 * kubikmeter / (4.0 * Math.PI);
            return Math.Cbrt(Math.Max(0.0, r3));
        }
    }
}
