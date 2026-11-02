using System.Collections.Generic;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Berechnet (auf dem Host), welche Pumpen über Rohre mit dem Tank verbunden sind.
    /// Knoten = Anschlüsse; Kanten = Rohre (Ende A ↔ Ende B) und Anschlüsse, die fast an derselben
    /// Stelle liegen (eingerastet). Verfahren: Vereinigungs-Suche („Union-Find“).
    /// </summary>
    public static class Rohrnetz
    {
        const float Einrastabstand = 0.35f;

        public static void Berechnen(IEnumerable<Pumpe> pumpen)
        {
            var alle = Anschluss.Alle;
            int n = alle.Count;
            var eltern = new int[n];
            for (int i = 0; i < n; i++) eltern[i] = i;
            var nummer = new Dictionary<Anschluss, int>();
            for (int i = 0; i < n; i++) nummer[alle[i]] = i;

            int Wurzel(int x) { while (eltern[x] != x) { eltern[x] = eltern[eltern[x]]; x = eltern[x]; } return x; }
            void Verbinden(int x, int y) { x = Wurzel(x); y = Wurzel(y); if (x != y) eltern[x] = y; }

            // Rohre verbinden ihre beiden Enden
            foreach (var a in alle)
                if (a.typ == Anschluss.Typ.Rohr && a.rohr != null && a.rohr.AnschlussA != null && a.rohr.AnschlussB != null
                    && nummer.TryGetValue(a.rohr.AnschlussA, out int x) && nummer.TryGetValue(a.rohr.AnschlussB, out int y))
                    Verbinden(x, y);

            // Eingerastete Anschlüsse (gleiche Stelle) verbinden
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (Vector3.Distance(alle[i].transform.position, alle[j].transform.position) < Einrastabstand)
                        Verbinden(i, j);

            var tankGruppen = new HashSet<int>();
            for (int i = 0; i < n; i++)
                if (alle[i].typ == Anschluss.Typ.Tank) tankGruppen.Add(Wurzel(i));

            foreach (var pumpe in pumpen)
            {
                bool verbunden = pumpe.AuslassAnschluss != null && nummer.TryGetValue(pumpe.AuslassAnschluss, out int k)
                                 && tankGruppen.Contains(Wurzel(k));
                pumpe.VerbindungSetzen(verbunden);
            }
        }
    }
}
