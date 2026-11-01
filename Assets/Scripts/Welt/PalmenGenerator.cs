using System.Collections.Generic;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Erzeugt einfache Platzhalter-Palmen aus Grundformen: gebogener Stamm aus Ringen,
    /// Wedel als leicht geknickte, herabhängende Bänder. Ein Modell mit zwei Teilen
    /// (Teil 0 = Stamm, Teil 1 = Blätter), damit zwei Materialien genutzt werden können.
    /// Die Palme steht mit dem Fuß im Ursprung und wächst entlang +Y.
    /// </summary>
    public static class PalmenGenerator
    {
        public static Mesh Palme(int zufallsSamen, string name)
        {
            var zufall = new System.Random(zufallsSamen);
            float Zahl(float min, float max) => min + (float)zufall.NextDouble() * (max - min);

            float hoehe = Zahl(6f, 9f);
            float neigung = Zahl(1.0f, 2.8f);   // wie weit die Krone seitlich über dem Fuß steht
            float fussRadius = 0.22f;

            var punkte = new List<Vector3>();
            var stammDreiecke = new List<int>();
            var blattDreiecke = new List<int>();

            // ---- Stamm: quadratische Kurve vom Fuß nach oben, Krone seitlich versetzt (+X) ----
            Vector3 p0 = Vector3.zero;
            Vector3 p1 = new Vector3(0f, hoehe * 0.55f, 0f);
            Vector3 p2 = new Vector3(neigung, hoehe, 0f);
            const int ringe = 12, seiten = 8;

            for (int i = 0; i <= ringe; i++)
            {
                float t = (float)i / ringe;
                Vector3 mitte = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2;
                Vector3 tangente = (2 * (1 - t) * (p1 - p0) + 2 * t * (p2 - p1)).normalized;
                Vector3 n = Vector3.Cross(Vector3.forward, tangente).normalized;   // quer zur Wuchsrichtung
                Vector3 b = Vector3.Cross(tangente, n);
                float r = fussRadius * (1f - 0.45f * t) * (i % 2 == 0 ? 1f : 0.93f); // leichte Ringe wie Rinde

                for (int j = 0; j <= seiten; j++)
                {
                    float a = j * Mathf.PI * 2f / seiten;
                    punkte.Add(mitte + r * (Mathf.Cos(a) * n + Mathf.Sin(a) * b));
                }
            }
            for (int i = 0; i < ringe; i++)
            for (int j = 0; j < seiten; j++)
            {
                int v = i * (seiten + 1) + j;
                int vRechts = v + 1, vOben = v + seiten + 1, vObenRechts = vOben + 1;
                stammDreiecke.AddRange(new[] { v, vRechts, vOben, vRechts, vObenRechts, vOben });
            }

            // ---- Wedel: rund um die Krone, steigen kurz an und hängen dann herab ----
            int wedel = 8;
            Vector3 krone = p2;
            for (int w = 0; w < wedel; w++)
            {
                float richtungsWinkel = w * Mathf.PI * 2f / wedel + Zahl(-0.25f, 0.25f);
                var richtung = new Vector3(Mathf.Cos(richtungsWinkel), 0f, Mathf.Sin(richtungsWinkel));
                Vector3 quer = Vector3.Cross(Vector3.up, richtung).normalized;
                float laenge = Zahl(3.0f, 4.2f);
                float anstieg = Zahl(0.6f, 1.2f);
                float haengen = Zahl(1.8f, 2.8f);
                const int schritte = 10;
                int start = punkte.Count;

                for (int s = 0; s <= schritte; s++)
                {
                    float u = (float)s / schritte;
                    Vector3 mitte = krone + richtung * (laenge * u) + Vector3.up * (anstieg * u - haengen * u * u);
                    float breite = 0.6f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.15f)) + 0.05f;
                    Vector3 knick = Vector3.down * breite * 0.25f; // Ränder leicht nach unten: V-Form
                    punkte.Add(mitte - quer * breite + knick);
                    punkte.Add(mitte);
                    punkte.Add(mitte + quer * breite + knick);
                }
                for (int s = 0; s < schritte; s++)
                {
                    int a = start + s * 3;
                    int b = a + 3;
                    // links: a, a+1 / b, b+1   rechts: a+1, a+2 / b+1, b+2
                    blattDreiecke.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    blattDreiecke.AddRange(new[] { a + 1, b + 1, a + 2, a + 2, b + 1, b + 2 });
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(punkte);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(stammDreiecke, 0);
            mesh.SetTriangles(blattDreiecke, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
