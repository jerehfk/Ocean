using System.Collections.Generic;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Baut aus einer Punktfolge einen runden Schlauch (Röhre) als 3D-Modell.
    /// Anders als eine flache Linie sieht er von allen Seiten richtig aus und wirft echte Schatten.
    /// </summary>
    public static class SchlauchModell
    {
        public static Mesh Bauen(IList<Vector3> punkte, float radius, int seiten = 8)
        {
            var mesh = new Mesh { name = "Schlauch" };
            int n = punkte.Count;
            if (n < 2) return mesh;

            var ecken = new Vector3[n * (seiten + 1)];
            var normalen = new Vector3[ecken.Length];
            var dreiecke = new int[(n - 1) * seiten * 6];

            // Querrichtung wird von Punkt zu Punkt „mitgeführt“, damit sich der Schlauch nicht verdreht
            Vector3 tangente = (punkte[1] - punkte[0]).normalized;
            Vector3 quer = Vector3.Cross(tangente, Vector3.up);
            if (quer.sqrMagnitude < 0.001f) quer = Vector3.Cross(tangente, Vector3.right);
            quer.Normalize();

            for (int i = 0; i < n; i++)
            {
                Vector3 vor = i == 0 ? punkte[1] - punkte[0]
                            : i == n - 1 ? punkte[n - 1] - punkte[n - 2]
                            : punkte[i + 1] - punkte[i - 1];
                if (vor.sqrMagnitude > 1e-8f) tangente = vor.normalized;
                quer = Vector3.ProjectOnPlane(quer, tangente);
                if (quer.sqrMagnitude < 1e-6f) quer = Vector3.Cross(tangente, Vector3.up);
                quer.Normalize();
                Vector3 quer2 = Vector3.Cross(tangente, quer);

                for (int j = 0; j <= seiten; j++)
                {
                    float w = j * Mathf.PI * 2f / seiten;
                    Vector3 richtung = Mathf.Cos(w) * quer + Mathf.Sin(w) * quer2;
                    ecken[i * (seiten + 1) + j] = punkte[i] + richtung * radius;
                    normalen[i * (seiten + 1) + j] = richtung;
                }
            }

            int d = 0;
            for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < seiten; j++)
            {
                int a = i * (seiten + 1) + j;
                int b = a + seiten + 1;
                dreiecke[d++] = a; dreiecke[d++] = b; dreiecke[d++] = a + 1;
                dreiecke[d++] = a + 1; dreiecke[d++] = b; dreiecke[d++] = b + 1;
            }

            mesh.vertices = ecken;
            mesh.normals = normalen;
            mesh.triangles = dreiecke;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
