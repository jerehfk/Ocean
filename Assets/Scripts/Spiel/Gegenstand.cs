using System;
using Unity.Netcode;

namespace Ocean
{
    /// <summary>Alles, was im Inventar oder in einer Kiste liegen kann.</summary>
    public enum Gegenstand : byte { Nichts = 0, Handpumpe = 1, Motorpumpe = 2, Rohr = 3, Kiste = 4 }

    /// <summary>Ein Platz im Lager: welcher Gegenstand und wie viele (bei Rohr: Meter).</summary>
    public struct Stapel : INetworkSerializeByMemcpy, IEquatable<Stapel>
    {
        public Gegenstand art;
        public int menge;

        public Stapel(Gegenstand art, int menge) { this.art = art; this.menge = menge; }
        public bool Leer => art == Gegenstand.Nichts || menge <= 0;
        public bool Equals(Stapel o) => art == o.art && menge == o.menge;
        public override bool Equals(object o) => o is Stapel s && Equals(s);
        public override int GetHashCode() => ((int)art << 24) ^ menge;
    }

    /// <summary>Namen, Stapelgrößen und Eigenschaften der Gegenstände (Zahlen aus den Spiel-Werten).</summary>
    public static class GegenstandInfo
    {
        public static string Name(Gegenstand g) => g switch
        {
            Gegenstand.Handpumpe => "Handpumpe",
            Gegenstand.Motorpumpe => "Motorpumpe",
            Gegenstand.Rohr => "Rohr",
            Gegenstand.Kiste => "Kiste",
            _ => "",
        };

        /// <summary>Menge als Text, z. B. „3“ oder „40 m“.</summary>
        public static string Menge(Stapel s) => s.Leer ? "" : s.art == Gegenstand.Rohr ? $"{s.menge} m" : $"{s.menge}";

        public static int StapelMax(Gegenstand g)
        {
            var w = Spielstand.Instanz != null ? Spielstand.Instanz.werte : null;
            return g switch
            {
                Gegenstand.Handpumpe or Gegenstand.Motorpumpe => w != null ? w.stapelPumpe : 10,
                Gegenstand.Rohr => w != null ? w.stapelRohr : 100,
                Gegenstand.Kiste => w != null ? w.stapelKiste : 5,
                _ => 0,
            };
        }

        /// <summary>Wird aus der Schnellleiste heraus aufgestellt bzw. verlegt.</summary>
        public static bool Platzierbar(Gegenstand g) => g != Gegenstand.Nichts;

        /// <summary>Shop-Eintrag, über den man den Gegenstand kauft.</summary>
        public static Gegenstand AusUpgrade(Upgrade u) => u switch
        {
            Upgrade.Handpumpe => Gegenstand.Handpumpe,
            Upgrade.Motorpumpe => Gegenstand.Motorpumpe,
            Upgrade.Rohr => Gegenstand.Rohr,
            Upgrade.Kiste => Gegenstand.Kiste,
            _ => Gegenstand.Nichts,
        };
    }
}
