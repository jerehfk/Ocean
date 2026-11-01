using UnityEngine;
using UnityEngine.Rendering;

namespace Ocean
{
    /// <summary>
    /// Erzeugt die Modelle für Planet, Wasser und Insel.
    /// Unitys eingebaute Kugel ist für 500 m Radius viel zu grob, daher eigene Modelle.
    /// </summary>
    public static class KugelGenerator
    {
        /// <summary>
        /// Kugel mit Radius 1 aus sechs gewölbten Würfelseiten („Würfelkugel“).
        /// Gleichmäßiger unterteilt als eine Kugel aus Längen- und Breitengraden.
        /// </summary>
        public static Mesh Wuerfelkugel(int unterteilungen, string name, float texturWiederholungen = 1f, Color? punktFarbe = null)
        {
            int n = Mathf.Max(1, unterteilungen);
            int proSeite = (n + 1) * (n + 1);
            var punkte = new Vector3[proSeite * 6];
            var uvs = new Vector2[proSeite * 6];
            var dreiecke = new int[n * n * 6 * 6];
            Vector3[] seiten = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

            int d = 0;
            for (int s = 0; s < 6; s++)
            {
                Vector3 oben = seiten[s];
                Vector3 achseA = new Vector3(oben.y, oben.z, oben.x);
                Vector3 achseB = Vector3.Cross(oben, achseA);
                int start = s * proSeite;

                for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                {
                    float px = (float)x / n - 0.5f;
                    float py = (float)y / n - 0.5f;
                    Vector3 p = oben + 2f * px * achseA + 2f * py * achseB;
                    punkte[start + x + y * (n + 1)] = p.normalized;
                    uvs[start + x + y * (n + 1)] = new Vector2(px + 0.5f, py + 0.5f) * texturWiederholungen;
                }

                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = start + x + y * (n + 1);
                    dreiecke[d++] = i;
                    dreiecke[d++] = i + n + 2;
                    dreiecke[d++] = i + n + 1;
                    dreiecke[d++] = i;
                    dreiecke[d++] = i + 1;
                    dreiecke[d++] = i + n + 2;
                }
            }

            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = punkte;
            mesh.uv = uvs;
            mesh.triangles = dreiecke;
            mesh.normals = punkte; // bei einer Einheitskugel zeigt jede Normale genau nach außen
            if (punktFarbe.HasValue)
            {
                var farben = new Color[punkte.Length];
                for (int i = 0; i < farben.Length; i++) farben[i] = punktFarbe.Value;
                mesh.colors = farben;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Fein aufgelöstes Bodenstück um den Nordpol des Planeten (Inselmitte = Richtung oben).
        /// Die Höhe kommt aus WeltDaten.Bodenhoehe; Koordinaten sind absolut (Planetenmitte = 0).
        /// </summary>
        public static Mesh Inselstueck(WeltDaten daten)
        {
            float halb = daten.InselRand * (1f + daten.uferUnregelmaessigkeit) + 10f;
            int n = Mathf.CeilToInt(2f * halb / Mathf.Max(0.1f, daten.inselGitterAbstand));
            float schritt = 2f * halb / n;
            float r = daten.wasserRadius;

            var punkte = new Vector3[(n + 1) * (n + 1)];
            var uvs = new Vector2[punkte.Length];
            var farben = new Color[punkte.Length];
            var dreiecke = new int[n * n * 6];

            for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                float x = -halb + i * schritt;
                float z = -halb + j * schritt;
                float abstand = Mathf.Sqrt(x * x + z * z);

                // Ebene Koordinaten auf die Kugel „aufwickeln“: Abstand = Bogenlänge
                Vector3 richtung = Vector3.up;
                if (abstand > 0.0001f)
                {
                    float winkel = abstand / r;
                    richtung = new Vector3(Mathf.Sin(winkel) * x / abstand, Mathf.Cos(winkel), Mathf.Sin(winkel) * z / abstand);
                }
                float hoehe = daten.Bodenhoehe(x, z);
                int k = i + j * (n + 1);
                punkte[k] = richtung * (r + hoehe);
                uvs[k] = new Vector2(x, z) / 4f; // Textur wiederholt sich alle 4 m
                farben[k] = WeltDaten.BodenFarbe(hoehe);
            }

            int d = 0;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = i + j * (n + 1);
                int b = a + 1;
                int c = a + (n + 1);
                int e = c + 1;
                dreiecke[d++] = a; dreiecke[d++] = c; dreiecke[d++] = e;
                dreiecke[d++] = a; dreiecke[d++] = e; dreiecke[d++] = b;
            }

            var mesh = new Mesh { name = "Inselstueck", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = punkte;
            mesh.uv = uvs;
            mesh.colors = farben;
            mesh.triangles = dreiecke;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Punkt auf dem Boden an den ebenen Koordinaten (x, z) um die Inselmitte, plus Zusatzhöhe,
        /// sowie die Richtung „oben“ an dieser Stelle.
        /// </summary>
        public static Vector3 PunktAufBoden(WeltDaten daten, float x, float z, float zusatzHoehe, out Vector3 oben)
        {
            float abstand = Mathf.Sqrt(x * x + z * z);
            oben = Vector3.up;
            if (abstand > 0.0001f)
            {
                float winkel = abstand / daten.wasserRadius;
                oben = new Vector3(Mathf.Sin(winkel) * x / abstand, Mathf.Cos(winkel), Mathf.Sin(winkel) * z / abstand);
            }
            return oben * (daten.wasserRadius + daten.Bodenhoehe(x, z) + zusatzHoehe);
        }

        /// <summary>Punkt auf dem Inselboden im gegebenen Abstand von der Inselmitte (Richtung +X), plus Zusatzhöhe.</summary>
        public static Vector3 PunktAufInsel(WeltDaten daten, float abstand, float zusatzHoehe)
        {
            float winkel = abstand / daten.wasserRadius;
            var richtung = new Vector3(Mathf.Sin(winkel), Mathf.Cos(winkel), 0f);
            return richtung * (daten.wasserRadius + daten.Bodenhoehe(abstand, 0f) + zusatzHoehe);
        }
    }
}
