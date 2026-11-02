using System;
using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Gemeinsamer Spielstand, vom Host verwaltet und an alle verteilt:
    /// Kasse (gehört allen) und insgesamt geschöpfte Liter. Aus den Litern ergibt sich der Pegel.
    /// </summary>
    public class Spielstand : NetworkBehaviour
    {
        public static Spielstand Instanz { get; private set; }

        public readonly NetworkVariable<double> kasse = new NetworkVariable<double>();
        public readonly NetworkVariable<double> geschoepftLiter = new NetworkVariable<double>();

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

        /// <summary>Nur auf dem Host: Geld in die gemeinsame Kasse.</summary>
        public void Einnahme(double betrag)
        {
            if (IsServer) kasse.Value += betrag;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void GeschoepftRpc(float liter)
        {
            if (liter > 0f) geschoepftLiter.Value += liter;
        }

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
