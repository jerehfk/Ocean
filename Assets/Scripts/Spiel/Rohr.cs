using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Ein gerades Rohrstück von A nach B, am Boden entlang verlegt. An beiden Enden sitzt ein
    /// Anschluss, an dem weitere Rohre einrasten. Der Host legt es an; Anfang und Ende werden an
    /// alle übertragen, jeder Spieler baut die Form selbst.
    /// </summary>
    public class Rohr : NetworkBehaviour
    {
        public const float Radius = 0.1f;

        public MeshFilter form;
        public Material anschlussMaterial;

        public readonly NetworkVariable<Vector3> anfang = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<Vector3> ende = new NetworkVariable<Vector3>();

        public Anschluss AnschlussA { get; private set; }
        public Anschluss AnschlussB { get; private set; }

        public override void OnNetworkSpawn()
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // Form liegt in Weltkoordinaten
            var punkte = Verlauf(anfang.Value, ende.Value);
            form.sharedMesh = SchlauchModell.Bauen(punkte, Radius, 10);
            AnschlussA = AnschlussAnlegen("Ende A", anfang.Value);
            AnschlussB = AnschlussAnlegen("Ende B", ende.Value);
        }

        Anschluss AnschlussAnlegen(string name, Vector3 position)
        {
            var objekt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            objekt.name = name;
            objekt.transform.SetParent(transform, false);
            objekt.transform.position = position;
            objekt.transform.localScale = Vector3.one * (Radius * 3f);
            if (anschlussMaterial != null) objekt.GetComponent<MeshRenderer>().sharedMaterial = anschlussMaterial;
            var anschluss = objekt.AddComponent<Anschluss>();
            anschluss.typ = Anschluss.Typ.Rohr;
            anschluss.rohr = this;
            return anschluss;
        }

        /// <summary>
        /// Punkte des Rohrs: auf der Kugel von A nach B, dazwischen knapp über dem Boden
        /// (auch unter Wasser am Meeresgrund). Die Enden bleiben genau an A und B.
        /// </summary>
        public static List<Vector3> Verlauf(Vector3 a, Vector3 b)
        {
            var punkte = new List<Vector3>();
            var schwerkraft = PlanetSchwerkraft.Instanz;
            if (schwerkraft == null) { punkte.Add(a); punkte.Add(b); return punkte; }

            Vector3 mitte = schwerkraft.transform.position;
            Vector3 da = a - mitte, db = b - mitte;
            int abschnitte = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / 0.5f), 2, 80);
            for (int i = 0; i <= abschnitte; i++)
            {
                float t = (float)i / abschnitte;
                Vector3 richtung = Vector3.Slerp(da.normalized, db.normalized, t);
                Vector3 punkt = mitte + richtung * Mathf.Lerp(da.magnitude, db.magnitude, t);
                if (i > 0 && i < abschnitte && Baugrund.BodenUnter(punkt, richtung, out Vector3 boden))
                {
                    // Nicht unter den Boden, aber auch nicht höher als die gerade Verbindung
                    float bodenHoehe = Vector3.Distance(boden, mitte) + Radius + 0.02f;
                    float geradeHoehe = Vector3.Distance(punkt, mitte);
                    punkt = mitte + richtung * Mathf.Max(bodenHoehe, Mathf.Min(geradeHoehe, bodenHoehe + 0.3f));
                }
                punkte.Add(punkt);
            }
            return punkte;
        }
    }
}
