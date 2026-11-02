using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Plätze mit Stapeln – das Inventar des Spielers und jede Kiste nutzen dasselbe.
    /// Der Inhalt liegt beim Host und wird an alle verteilt; Spieler ändern ihn nur über Anfragen
    /// (verschieben, schnell verschieben). So entsteht im Koop nichts doppelt.
    /// Spieler-Inventar: Platz 0–6 = Schnellleiste (Tasten 2–8), danach der Rucksack.
    /// </summary>
    public class Lager : NetworkBehaviour
    {
        public const int SchnellPlaetze = 7;
        const float Zugriffsweite = 8f;   // so nah muss man an einer Kiste stehen

        /// <summary>Inventar der eigenen Spielfigur.</summary>
        public static Lager Lokal { get; private set; }
        public static readonly List<Lager> Kisten = new();

        [Tooltip("Anzahl der Plätze.")]
        public int groesse = 24;
        [Tooltip("Kiste in der Welt (sonst Inventar eines Spielers).")]
        public bool istKiste;

        readonly NetworkList<Stapel> plaetze = new NetworkList<Stapel>();

        public int Groesse => plaetze.Count;
        public Stapel Platz(int i) => i >= 0 && i < plaetze.Count ? plaetze[i] : default;

        public override void OnNetworkSpawn()
        {
            if (IsServer) while (plaetze.Count < groesse) plaetze.Add(default);
            if (istKiste) Kisten.Add(this);
            else if (IsOwner) Lokal = this;
        }

        public override void OnNetworkDespawn()
        {
            Kisten.Remove(this);
            if (Lokal == this) Lokal = null;
        }

        // ---------- Abfragen (überall) ----------

        public int Anzahl(Gegenstand g)
        {
            int summe = 0;
            foreach (var s in plaetze) if (s.art == g) summe += s.menge;
            return summe;
        }

        public int FreiePlaetze
        {
            get { int frei = 0; foreach (var s in plaetze) if (s.Leer) frei++; return frei; }
        }

        /// <summary>Passt die Menge noch hinein (in vorhandene Stapel oder freie Plätze)?</summary>
        public bool Passt(Gegenstand g, int menge, int von = 0, int bis = -1)
        {
            if (bis < 0 || bis > plaetze.Count) bis = plaetze.Count;
            int max = GegenstandInfo.StapelMax(g);
            int frei = 0;
            for (int i = von; i < bis; i++)
            {
                var s = plaetze[i];
                if (s.Leer) frei += max;
                else if (s.art == g) frei += Mathf.Max(0, max - s.menge);
                if (frei >= menge) return true;
            }
            return false;
        }

        // ---------- Ändern (nur Host) ----------

        /// <summary>Legt die Menge hinein – erst auf passende Stapel, dann auf freie Plätze. Nur ganz oder gar nicht.</summary>
        public bool Hinzufuegen(Gegenstand g, int menge, int von = 0, int bis = -1)
        {
            if (!IsServer || menge <= 0 || !Passt(g, menge, von, bis)) return false;
            if (bis < 0 || bis > plaetze.Count) bis = plaetze.Count;
            int max = GegenstandInfo.StapelMax(g);
            for (int durchgang = 0; durchgang < 2 && menge > 0; durchgang++)
                for (int i = von; i < bis && menge > 0; i++)
                {
                    var s = plaetze[i];
                    bool passend = durchgang == 0 ? !s.Leer && s.art == g : s.Leer;
                    if (!passend) continue;
                    int vorher = s.Leer ? 0 : s.menge;
                    int dazu = Mathf.Min(menge, max - vorher);
                    if (dazu <= 0) continue;
                    plaetze[i] = new Stapel(g, vorher + dazu);
                    menge -= dazu;
                }
            return true;
        }

        /// <summary>Nimmt die Menge heraus (aus beliebigen Stapeln). Nur ganz oder gar nicht.</summary>
        public bool Entfernen(Gegenstand g, int menge)
        {
            if (!IsServer || menge <= 0 || Anzahl(g) < menge) return false;
            for (int i = plaetze.Count - 1; i >= 0 && menge > 0; i--)   // Rucksack zuerst, Schnellleiste zuletzt
            {
                var s = plaetze[i];
                if (s.art != g) continue;
                int weg = Mathf.Min(menge, s.menge);
                plaetze[i] = s.menge - weg > 0 ? new Stapel(g, s.menge - weg) : default;
                menge -= weg;
            }
            return true;
        }

        /// <summary>Nimmt einen Gegenstand von genau diesem Platz (z. B. der gewählten Schnellleiste).</summary>
        public bool EinsNehmen(int platz, out Gegenstand g)
        {
            g = Gegenstand.Nichts;
            if (!IsServer || platz < 0 || platz >= plaetze.Count || plaetze[platz].Leer) return false;
            var s = plaetze[platz];
            g = s.art;
            plaetze[platz] = s.menge > 1 ? new Stapel(s.art, s.menge - 1) : default;
            return true;
        }

        /// <summary>Darf dieser Spieler hier etwas ändern? Eigenes Inventar: nur der Besitzer. Kiste: wer nah genug steht.</summary>
        bool DarfZugreifen(ulong spieler)
        {
            if (!istKiste) return OwnerClientId == spieler;
            if (!NetworkManager.ConnectedClients.TryGetValue(spieler, out var client) || client.PlayerObject == null) return false;
            return Vector3.Distance(client.PlayerObject.transform.position, transform.position) <= Zugriffsweite;
        }

        // ---------- Anfragen der Spieler ----------

        /// <summary>
        /// Stapel von hier auf einen Platz in einem Lager ziehen (auch dasselbe Lager):
        /// leerer Platz → verschieben, gleicher Gegenstand → zusammenlegen, anderer → tauschen.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void VerschiebenRpc(int platz, NetworkObjectReference ziel, int zielPlatz, RpcParams anfrage = default)
        {
            if (!ziel.TryGet(out var zielObjekt) || !zielObjekt.TryGetComponent(out Lager zielLager)) return;
            ulong spieler = anfrage.Receive.SenderClientId;
            if (!DarfZugreifen(spieler) || !zielLager.DarfZugreifen(spieler)) return;
            if (platz < 0 || platz >= plaetze.Count || zielPlatz < 0 || zielPlatz >= zielLager.plaetze.Count) return;
            if (zielLager == this && platz == zielPlatz) return;

            var von = plaetze[platz];
            var nach = zielLager.plaetze[zielPlatz];
            if (von.Leer) return;

            if (!nach.Leer && nach.art == von.art)
            {
                int max = GegenstandInfo.StapelMax(von.art);
                int dazu = Mathf.Min(von.menge, max - nach.menge);
                if (dazu <= 0) return;
                zielLager.plaetze[zielPlatz] = new Stapel(von.art, nach.menge + dazu);
                plaetze[platz] = von.menge - dazu > 0 ? new Stapel(von.art, von.menge - dazu) : default;
            }
            else
            {
                zielLager.plaetze[zielPlatz] = von;
                plaetze[platz] = nach;
            }
        }

        /// <summary>Umschalt + Klick: ganzer Stapel in einen Bereich eines Lagers, so viel hineinpasst.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        public void SchnellRpc(int platz, NetworkObjectReference ziel, int zielVon, int zielBis, RpcParams anfrage = default)
        {
            if (!ziel.TryGet(out var zielObjekt) || !zielObjekt.TryGetComponent(out Lager zielLager)) return;
            ulong spieler = anfrage.Receive.SenderClientId;
            if (!DarfZugreifen(spieler) || !zielLager.DarfZugreifen(spieler)) return;
            if (platz < 0 || platz >= plaetze.Count) return;
            var von = plaetze[platz];
            if (von.Leer) return;

            // So viel wie möglich verschieben, Stück für Stück herunterzählen, bis es passt
            int menge = von.menge;
            while (menge > 0 && !zielLager.Passt(von.art, menge, zielVon, zielBis)) menge--;
            if (menge <= 0) return;
            plaetze[platz] = von.menge - menge > 0 ? new Stapel(von.art, von.menge - menge) : default;
            zielLager.Hinzufuegen(von.art, menge, zielVon, zielBis);
        }
    }
}
