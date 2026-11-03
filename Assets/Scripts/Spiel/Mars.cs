using System.Collections.Generic;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Der Mars – liegt weit weg von der Erde (außerhalb der Sichtweite der Spieler-Kameras) und
    /// wird nur von der Kamera des Satelliten gesehen. Beim Start entsteht er aus Zahlen (Spiel-Werte):
    /// Kugel mit Hügeln und Kratern, dazu eine Reihe Wasserkrater auf der Bahn des Satelliten,
    /// die sich nacheinander füllen. Außerdem: der Mars als Scheibe am Himmel über der Insel und die
    /// kleine Rakete, die den Satelliten hinbringt.
    /// Licht ist in die Farben eingerechnet, damit der Satellit überall eine helle Oberfläche sieht.
    /// </summary>
    public class Mars : MonoBehaviour
    {
        public static Mars Instanz { get; private set; }

        public SpielWerte werte;
        [Tooltip("Unbeleuchtetes Material mit Punktfarben (Shader Ocean/Vertexfarbe).")]
        public Material bodenMaterial;
        public Material wasserMaterial;
        [Tooltip("Mars als Scheibe am Himmel.")]
        public Material himmelMaterial;
        [Tooltip("Satelliten-Rakete und Einschlag.")]
        public Material weissMaterial;
        [Tooltip("Mittelpunkt des Mars – weit weg, außerhalb der Sichtweite (3 km) der Spieler.")]
        public Vector3 zentrum = new Vector3(60000f, 0f, 0f);
        [Tooltip("Auflösung der Mars-Kugel (Unterteilungen je Würfelseite).")]
        public int aufloesung = 160;

        /// <summary>Bild der Satelliten-Kamera (für die Bildschirme).</summary>
        public RenderTexture Bild { get; private set; }
        /// <summary>Wo der Mars am Himmel über der Insel steht (Weltposition der Scheibe).</summary>
        public Vector3 HimmelsPunkt { get; private set; }

        const float HimmelsAbstand = 2000f;   // Scheibe am Himmel: so weit weg von der Insel (m)
        const float HimmelsGroesse = 22f;     // ihr Radius (m) → etwa 1,3° am Himmel

        Camera satellit;
        float winkel;
        int angefordert = -10;

        // Wasserkrater
        readonly List<Vector3> kraterRichtung = new();
        readonly List<float> kraterGrund = new();      // Höhe des Geländes am Kratermittelpunkt
        readonly List<Transform> wasserFlaeche = new();
        // Zierkrater
        readonly List<(Vector3 richtung, float radius, float tiefe)> zierKrater = new();

        Transform einschlag;
        float einschlagZeit = -10f;
        Transform satRakete;

        float R => werte.marsRadius;
        float KraterRadius => werte.kraterDurchmesser * 0.5f;

        /// <summary>
        /// Volumen eines Wasserkraters in m³. Form: Tiefe × (1 − (r/R)⁴) – flacher Boden, steile Wände.
        /// Bis zur Höhe y über dem Grund gefüllt: V(y) = π R² y^1,5 / (1,5 √D); voll: (2/3) π R² D.
        /// </summary>
        public static double KraterVolumen(SpielWerte w)
        {
            double r = w.kraterDurchmesser * 0.5;
            return 2.0 / 3.0 * System.Math.PI * r * r * w.kraterTiefe;
        }

        /// <summary>Wasserhöhe über dem Kratergrund für so viele m³.</summary>
        float FuellHoehe(double kubikmeter)
        {
            double r = KraterRadius, d = werte.kraterTiefe;
            double y = System.Math.Pow(kubikmeter * 1.5 * System.Math.Sqrt(d) / (System.Math.PI * r * r), 2.0 / 3.0);
            return (float)System.Math.Min(y, d);
        }

        void Awake()
        {
            Instanz = this;
            // Insel liegt am Nordpol (oben = +Y). Mars steht schräg darüber.
            var schwerkraft = FindAnyObjectByType<PlanetSchwerkraft>();
            Vector3 erde = schwerkraft != null ? schwerkraft.transform.position : Vector3.zero;
            var wasser = FindAnyObjectByType<Wasser>();
            float erdRadius = wasser != null ? wasser.Radius : 2000f;
            float hoehe = werte.marsHoehenwinkel * Mathf.Deg2Rad;
            Vector3 waagerecht = new Vector3(-0.6f, 0f, -0.8f);
            Vector3 richtung = (Mathf.Cos(hoehe) * waagerecht + Mathf.Sin(hoehe) * Vector3.up).normalized;
            HimmelsPunkt = erde + Vector3.up * erdRadius + richtung * HimmelsAbstand;
        }

        void Start()
        {
            transform.position = zentrum;
            transform.rotation = Quaternion.identity;
            KraterVerteilen();
            OberflaecheBauen();
            WasserBauen();
            HimmelBauen();
            SatellitBauen();
            einschlag = Teil(PrimitiveType.Sphere, "Einschlag", transform, weissMaterial);
            einschlag.gameObject.SetActive(false);
            satRakete = RaketeBauen();
        }

        void OnDestroy()
        {
            if (Bild != null) Bild.Release();
            if (Instanz == this) Instanz = null;
        }

        /// <summary>Die Bildschirme fragen jedes Bild an, in dem sie das Satellitenbild zeigen.</summary>
        public void Anfordern() => angefordert = Time.frameCount;

        // ---------- Gelände ----------

        void KraterVerteilen()
        {
            // Wasserkrater auf der Bahn des Satelliten (Äquator), leicht versetzt
            for (int k = 0; k < werte.kraterAnzahl; k++)
            {
                float w = 0.35f + k * Mathf.PI * 2f / werte.kraterAnzahl;
                float breite = (k % 2 == 0 ? 0.035f : -0.03f);
                kraterRichtung.Add(new Vector3(Mathf.Cos(breite) * Mathf.Cos(w), Mathf.Sin(breite), Mathf.Cos(breite) * Mathf.Sin(w)).normalized);
            }

            // Zierkrater zufällig, aber immer gleich (fester Startwert), nicht auf den Wasserkratern
            var zufall = new System.Random(7);
            for (int versuch = 0; versuch < 200 && zierKrater.Count < 34; versuch++)
            {
                var richtung = new Vector3((float)zufall.NextDouble() * 2f - 1f, (float)zufall.NextDouble() * 2f - 1f, (float)zufall.NextDouble() * 2f - 1f);
                if (richtung.sqrMagnitude < 0.05f || richtung.sqrMagnitude > 1f) continue;
                richtung.Normalize();
                float radius = Mathf.Lerp(4f, 34f, Mathf.Pow((float)zufall.NextDouble(), 2.2f));
                bool frei = true;
                foreach (var k in kraterRichtung)
                    if (Vector3.Distance(k, richtung) * R < radius * 1.6f + KraterRadius * 2.5f) { frei = false; break; }
                if (frei) zierKrater.Add((richtung, radius, Mathf.Min(radius * 0.22f, 6f)));
            }

            foreach (var k in kraterRichtung) kraterGrund.Add(Gelaende(k));
        }

        static float Rauschen(Vector3 p) =>
            (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z + 3.3f) + Mathf.PerlinNoise(p.z + 12.3f, p.x + 5.1f)) / 3f - 0.5f;

        /// <summary>Hügel und Zierkrater (ohne Wasserkrater), Höhe über dem Radius in m.</summary>
        float Gelaende(Vector3 d)
        {
            float h = Rauschen(d * R * 0.012f) * 7f + Rauschen(d * R * 0.05f + new Vector3(7f, 2f, 9f)) * 1.6f;
            foreach (var (richtung, radius, tiefe) in zierKrater)
            {
                float x = Vector3.Distance(d, richtung) * R / radius;
                if (x > 1.8f) continue;
                if (x < 1f) h -= tiefe * (1f - x * x);
                h += tiefe * 0.3f * Mathf.Exp(-Mathf.Pow((x - 1f) / 0.3f, 2f));
            }
            return h;
        }

        /// <summary>Höhe über dem Radius mit Wasserkratern.</summary>
        float Hoehe(Vector3 d)
        {
            float h = Gelaende(d);
            for (int k = 0; k < kraterRichtung.Count; k++)
            {
                float x = Vector3.Distance(d, kraterRichtung[k]) * R / KraterRadius;
                if (x > 1.8f) continue;
                float schuessel = x < 1f ? -werte.kraterTiefe * (1f - x * x * x * x) : 0f;
                float wall = 0.9f * Mathf.Exp(-Mathf.Pow((x - 1f) / 0.3f, 2f));
                float krater = kraterGrund[k] + schuessel + wall;
                float anteil = x < 1f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (x - 1f) / 0.8f);
                h = Mathf.Lerp(h, krater, anteil);
            }
            return h;
        }

        bool InWasserkrater(Vector3 d)
        {
            foreach (var k in kraterRichtung) if (Vector3.Distance(d, k) * R < KraterRadius * 1.05f) return true;
            return false;
        }

        void OberflaecheBauen()
        {
            var mesh = KugelGenerator.Wuerfelkugel(aufloesung, "Mars");
            var punkte = mesh.vertices;
            var normalen = new Vector3[punkte.Length];
            var farben = new Color[punkte.Length];
            var rost = new Color(0.66f, 0.31f, 0.16f);
            var hell = new Color(0.84f, 0.50f, 0.28f);
            var dunkel = new Color(0.40f, 0.20f, 0.12f);
            float schritt = 0.6f / R;

            for (int i = 0; i < punkte.Length; i++)
            {
                Vector3 d = punkte[i].normalized;
                Vector3 p = d * (R + Hoehe(d));

                // Normale aus zwei Nachbarpunkten (ohne Nähte zwischen den Würfelseiten)
                Vector3 a = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 b = Vector3.Cross(d, a);
                Vector3 da = (d + a * schritt).normalized, db = (d + b * schritt).normalized;
                Vector3 n = Vector3.Cross(da * (R + Hoehe(da)) - p, db * (R + Hoehe(db)) - p).normalized;
                if (Vector3.Dot(n, d) < 0f) n = -n;

                // Licht von schräg oben, überall gleich (eingebacken)
                Vector3 norden = Vector3.ProjectOnPlane(Vector3.up, d);
                norden = norden.sqrMagnitude > 0.001f ? norden.normalized : a;
                Vector3 licht = (d + norden * 0.8f + a * 0.3f).normalized;
                float helligkeit = 0.42f + 0.7f * Mathf.Max(0f, Vector3.Dot(n, licht));

                Color grund = Color.Lerp(rost, hell, Mathf.Clamp01(Rauschen(d * R * 0.03f) * 1.6f + 0.5f));
                if (InWasserkrater(d)) grund = Color.Lerp(grund, dunkel, 0.5f);
                farben[i] = grund * helligkeit;
                farben[i].a = 1f;
                punkte[i] = p;
                normalen[i] = n;
            }

            mesh.vertices = punkte;
            mesh.normals = normalen;
            mesh.colors = farben;
            mesh.RecalculateBounds();

            var boden = new GameObject("Marsboden");
            boden.transform.SetParent(transform, false);
            boden.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = boden.AddComponent<MeshRenderer>();
            r.sharedMaterial = bodenMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ---------- Wasser ----------

        void WasserBauen()
        {
            var scheibe = Scheibe(40);
            for (int k = 0; k < kraterRichtung.Count; k++)
            {
                var objekt = new GameObject($"Kraterwasser {k + 1}");
                objekt.transform.SetParent(transform, false);
                objekt.AddComponent<MeshFilter>().sharedMesh = scheibe;
                var r = objekt.AddComponent<MeshRenderer>();
                r.sharedMaterial = wasserMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                objekt.transform.localRotation = Quaternion.FromToRotation(Vector3.up, kraterRichtung[k]);
                objekt.SetActive(false);
                wasserFlaeche.Add(objekt.transform);
            }
        }

        /// <summary>Runde Fläche mit Radius 1, nach oben gerichtet.</summary>
        static Mesh Scheibe(int teile)
        {
            var punkte = new Vector3[teile + 1];
            var dreiecke = new int[teile * 3];
            for (int i = 0; i < teile; i++)
            {
                float w = i * Mathf.PI * 2f / teile;
                punkte[i + 1] = new Vector3(Mathf.Cos(w), 0f, Mathf.Sin(w));
                dreiecke[i * 3] = 0;
                dreiecke[i * 3 + 1] = (i + 1) % teile + 1;
                dreiecke[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Kraterwasser", vertices = punkte, triangles = dreiecke };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void WasserAnzeigen()
        {
            var stand = Spielstand.Instanz;
            double rest = stand != null ? stand.marsLiter.Value / 1000.0 : 0.0;
            double voll = KraterVolumen(werte);
            for (int k = 0; k < wasserFlaeche.Count; k++)
            {
                double inhalt = System.Math.Min(rest, voll);
                rest -= inhalt;
                var flaeche = wasserFlaeche[k];
                bool sichtbar = inhalt > 0.0005;
                if (flaeche.gameObject.activeSelf != sichtbar) flaeche.gameObject.SetActive(sichtbar);
                if (!sichtbar) continue;

                // Füllhöhe y über dem Grund → Radius der Wasserfläche aus der Kraterform
                float y = Mathf.Max(0.02f, FuellHoehe(inhalt));
                float radius = KraterRadius * Mathf.Pow(Mathf.Clamp01(y / werte.kraterTiefe), 0.25f) * 1.04f;
                float hoehe = kraterGrund[k] - werte.kraterTiefe + y + 0.02f;
                flaeche.localPosition = kraterRichtung[k] * (R + hoehe);
                flaeche.localScale = new Vector3(radius, 1f, radius);
            }
        }

        /// <summary>Eine Rakete ist gelandet: kurzer Spritzer am Krater.</summary>
        public void Einschlag(int krater)
        {
            if (krater < 0 || krater >= kraterRichtung.Count) return;
            einschlagZeit = Time.time;
            einschlag.localPosition = kraterRichtung[krater] * (R + kraterGrund[krater] - werte.kraterTiefe * 0.5f);
        }

        void EinschlagAnzeigen()
        {
            float t = (Time.time - einschlagZeit) / 1.4f;
            bool aktiv = t >= 0f && t < 1f;
            if (einschlag.gameObject.activeSelf != aktiv) einschlag.gameObject.SetActive(aktiv);
            if (aktiv) einschlag.localScale = Vector3.one * Mathf.Sin(t * Mathf.PI) * KraterRadius * 1.6f;
        }

        // ---------- Himmel, Satellit, Rakete ----------

        void HimmelBauen()
        {
            var scheibe = Teil(PrimitiveType.Sphere, "Mars am Himmel", null, himmelMaterial);
            scheibe.position = HimmelsPunkt;
            scheibe.localScale = Vector3.one * HimmelsGroesse * 2f;
        }

        void SatellitBauen()
        {
            Bild = new RenderTexture(1280, 720, 24) { name = "Satellitenbild", antiAliasing = 4 };
            Bild.Create();
            var objekt = new GameObject("Satellitenkamera");
            objekt.transform.SetParent(transform, false);
            satellit = objekt.AddComponent<Camera>();
            satellit.targetTexture = Bild;
            satellit.clearFlags = CameraClearFlags.SolidColor;
            satellit.backgroundColor = new Color(0.02f, 0.02f, 0.05f);
            satellit.fieldOfView = 62f;
            satellit.nearClipPlane = 0.5f;
            satellit.farClipPlane = 2000f;
            satellit.enabled = false;
        }

        Transform RaketeBauen()
        {
            var wurzel = new GameObject("Satelliten-Rakete").transform;
            // Gut sichtbar: etwa so hoch wie ein Haus (die Rakete der Insel ist ~5 m)
            Teil(PrimitiveType.Cylinder, "Rumpf", wurzel, weissMaterial, new Vector3(0f, 2.4f, 0f), new Vector3(1.05f, 2.4f, 1.05f));
            Teil(PrimitiveType.Sphere, "Spitze", wurzel, weissMaterial, new Vector3(0f, 4.95f, 0f), new Vector3(1.05f, 1.5f, 1.05f));
            wurzel.gameObject.SetActive(false);
            return wurzel;
        }

        static Transform Teil(PrimitiveType form, string name, Transform eltern, Material material, Vector3? position = null, Vector3? groesse = null)
        {
            var teil = GameObject.CreatePrimitive(form);
            teil.name = name;
            Destroy(teil.GetComponent<Collider>());
            teil.transform.SetParent(eltern, false);
            if (position.HasValue) teil.transform.localPosition = position.Value;
            if (groesse.HasValue) teil.transform.localScale = groesse.Value;
            var r = teil.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return teil.transform;
        }

        void Update()
        {
            if (werte == null || satellit == null) return;
            var stand = Spielstand.Instanz;

            // Satellit kreist auf der Äquatorbahn, Kamera schaut schräg nach vorn unten
            winkel += Mathf.PI * 2f / Mathf.Max(5f, werte.umlaufZeit) * Time.deltaTime;
            float bahn = R + werte.umlaufHoehe;
            Vector3 oben = new Vector3(Mathf.Cos(winkel), 0f, Mathf.Sin(winkel));
            Vector3 vor = new Vector3(-Mathf.Sin(winkel), 0f, Mathf.Cos(winkel));
            float neigung = werte.kameraNeigung * Mathf.Deg2Rad;
            Vector3 blick = vor * Mathf.Cos(neigung) - oben * Mathf.Sin(neigung);
            satellit.transform.SetPositionAndRotation(zentrum + oben * bahn, Quaternion.LookRotation(blick, oben));

            // Nur rendern, wenn jemand hinschaut und es ein Bild gibt
            bool zeigen = Time.frameCount - angefordert <= 1 && stand != null && stand.SatellitAngekommen && stand.Empfang;
            if (satellit.enabled != zeigen) satellit.enabled = zeigen;

            WasserAnzeigen();
            EinschlagAnzeigen();
            RaketeAnzeigen(stand);
        }

        /// <summary>Kleine Rakete steigt neben der Startrampe auf, wenn der Satellit losfliegt.</summary>
        void RaketeAnzeigen(Spielstand stand)
        {
            var rampe = Startrampe.Instanz;
            float vergangen = stand != null && stand.satellitRest.Value > 0f
                ? stand.satellitFlugdauer.Value - stand.satellitRest.Value : -1f;
            bool sichtbar = rampe != null && vergangen >= 0f && vergangen < 16f;
            if (satRakete.gameObject.activeSelf != sichtbar) satRakete.gameObject.SetActive(sichtbar);
            if (!sichtbar) return;

            Vector3 boden = rampe.transform.position + rampe.transform.right * 5.5f;
            Vector3 hoch = PlanetSchwerkraft.Instanz != null ? PlanetSchwerkraft.Instanz.Oben(boden) : Vector3.up;
            float zuendung = Mathf.Max(0f, vergangen - 1.5f);   // kurz stehen, dann abheben
            satRakete.SetPositionAndRotation(boden + hoch * (0.5f * 14f * zuendung * zuendung),
                Quaternion.FromToRotation(Vector3.up, hoch));
        }
    }
}
