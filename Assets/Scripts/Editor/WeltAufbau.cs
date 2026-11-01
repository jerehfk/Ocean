using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ocean.EditorWerkzeuge
{
    /// <summary>
    /// Menü „Ocean → Welt aufbauen“: erzeugt Planet, Insel, Steg, Wasser und Spieler
    /// in der geöffneten Szene aus den Zahlen in Assets/Daten/WeltDaten.asset.
    /// Erneut ausführen = alte Welt ersetzen (z. B. nach dem Ändern der Zahlen).
    /// </summary>
    public static class WeltAufbau
    {
        const string DatenPfad = "Assets/Daten/WeltDaten.asset";
        const string GeneriertOrdner = "Assets/Generiert";

        [MenuItem("Ocean/Welt aufbauen")]
        public static void Aufbauen()
        {
            WeltDaten daten = DatenLadenOderAnlegen();

            // Alte Welt, alten Spieler und die Standardkamera der Vorlage entfernen
            foreach (string name in new[] { "Welt", "Spieler", "Main Camera" })
            {
                var alt = GameObject.Find(name);
                if (alt != null) Object.DestroyImmediate(alt);
            }

            var welt = new GameObject("Welt");
            welt.AddComponent<PlanetSchwerkraft>();

            // Sand (für Insel und Meeresboden dasselbe Material, damit keine Kante entsteht)
            // Eigener Boden-Shader: dunkelt den Grund mit der Wassertiefe ab (Farbe pro Punkt)
            var sand = MaterialHolen("Sand", new Color(0.96f, 0.92f, 0.82f), false);
            var bodenShader = Shader.Find("Ocean/Boden");
            if (bodenShader != null) sand.shader = bodenShader;
            else Debug.LogWarning("Ocean: Shader „Ocean/Boden“ nicht gefunden – Boden nutzt das Standard-Material.");
            sand.SetColor("_BaseColor", new Color(0.96f, 0.92f, 0.82f));
            sand.SetTexture("_BaseMap", OptikAufbau.SandTextur());

            // Meeresboden: grobe Kugel, auf Bodenradius skaliert. Eine Würfelseite ist rund
            // ein Viertel des Umfangs lang; die Sandtextur soll sich alle 4 m wiederholen.
            float seitenLaenge = Mathf.PI * 0.5f * daten.BodenRadius;
            Mesh bodenMesh = MeshSpeichern(KugelGenerator.Wuerfelkugel(daten.planetAufloesung, "Meeresboden", seitenLaenge / 4f, WeltDaten.BodenFarbe(-daten.wassertiefe)));
            var boden = ObjektMitMesh("Meeresboden", welt.transform, bodenMesh, sand);
            boden.transform.localScale = Vector3.one * daten.BodenRadius;
            boden.AddComponent<MeshCollider>().sharedMesh = bodenMesh;

            // Insel: fein aufgelöstes Bodenstück am Nordpol
            Mesh inselMesh = MeshSpeichern(KugelGenerator.Inselstueck(daten));
            var insel = ObjektMitMesh("Insel", welt.transform, inselMesh, sand);
            insel.AddComponent<MeshCollider>().sharedMesh = inselMesh;

            // Steg: vom Strand ins Wasser, entlang +X. Der Steg ist gerade, der Planet gekrümmt:
            // die Enden liegen um „Durchhang“ höher über der Oberfläche als die Mitte.
            // Die Mitte sitzt deshalb so, dass das Landende bündig mit dem Strand abschließt.
            float ufer = daten.inselRadius * daten.Form(0f); // Uferlinie in Stegrichtung (+X)
            float stegAnfang = ufer - 6f;
            float stegEnde = ufer + 19f;
            float stegMitte = (stegAnfang + stegEnde) * 0.5f;
            float halbeLaenge = (stegEnde - stegAnfang) * 0.5f;
            float durchhang = halbeLaenge * halbeLaenge / (2f * daten.wasserRadius);
            float stegHoehe = daten.Bodenhoehe(stegAnfang, 0f) - durchhang - 0.15f; // Mitte des 0,3 m dicken Stegs
            float winkel = stegMitte / daten.wasserRadius;
            var stegOben = new Vector3(Mathf.Sin(winkel), Mathf.Cos(winkel), 0f);
            var stegVor = new Vector3(Mathf.Cos(winkel), -Mathf.Sin(winkel), 0f);
            var steg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            steg.name = "Steg";
            steg.transform.SetParent(welt.transform, false);
            steg.transform.SetPositionAndRotation(stegOben * (daten.wasserRadius + stegHoehe), Quaternion.LookRotation(stegVor, stegOben));
            steg.transform.localScale = new Vector3(3f, 0.3f, stegEnde - stegAnfang);
            steg.GetComponent<MeshRenderer>().sharedMaterial = MaterialHolen("Holz", new Color(0.36f, 0.24f, 0.14f), false);

            // Wasser: feine Kugel mit Radius 1, Pegel über die Skalierung
            Mesh wasserMesh = MeshSpeichern(KugelGenerator.Wuerfelkugel(daten.wasserAufloesung, "Wasser"));
            var wasser = ObjektMitMesh("Wasser", welt.transform, wasserMesh,
                MaterialHolen("Wasser", new Color(0.05f, 0.62f, 0.68f, 0.6f), true));
            wasser.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            wasser.AddComponent<Wasser>().Radius = daten.wasserRadius;

            PalmenAnlegen(daten, welt.transform);
            SpielerAnlegen(daten);
            OptikAufbau.Anwenden(welt.transform);

            EditorSceneManager.MarkSceneDirty(welt.scene);
            Debug.Log("Ocean: Welt aufgebaut. Szene speichern (Strg+S) nicht vergessen.");
        }

        static void PalmenAnlegen(WeltDaten daten, Transform welt)
        {
            const int varianten = 4, anzahl = 9;
            var modelle = new Mesh[varianten];
            for (int v = 0; v < varianten; v++)
                modelle[v] = MeshSpeichern(PalmenGenerator.Palme(100 + v, $"Palme_{v}"));

            var materialien = new[]
            {
                MaterialHolen("Rinde", new Color(0.50f, 0.40f, 0.28f), false),
                MaterialHolen("Palmblatt", new Color(0.22f, 0.50f, 0.16f), false),
            };
            materialien[1].SetFloat("_Cull", 0f); // Blätter von beiden Seiten sichtbar

            var gruppe = new GameObject("Palmen").transform;
            gruppe.SetParent(welt, false);
            var zufall = new System.Random(42);
            int gesetzt = 0, versuche = 0;
            while (gesetzt < anzahl && versuche++ < 500)
            {
                // Zufälliger Punkt im inneren Teil der Insel (dort, wo sie wirklich trocken ist)
                float winkel = (float)(zufall.NextDouble() * Mathf.PI * 2.0);
                float maxAbstand = daten.inselRadius * daten.Form(winkel) * 0.55f;
                float abstand = Mathf.Sqrt((float)zufall.NextDouble()) * maxAbstand;
                float x = Mathf.Cos(winkel) * abstand, z = Mathf.Sin(winkel) * abstand;
                if (x > 0f && Mathf.Abs(z) < 4f) continue; // Weg vom Start zum Steg (+X) freihalten

                Vector3 fuss = KugelGenerator.PunktAufBoden(daten, x, z, -0.2f, out Vector3 oben);
                var palme = new GameObject($"Palme {gesetzt + 1}");
                palme.transform.SetParent(gruppe, false);
                float drehung = (float)(zufall.NextDouble() * 360.0);
                palme.transform.SetPositionAndRotation(fuss, Quaternion.FromToRotation(Vector3.up, oben) * Quaternion.Euler(0f, drehung, 0f));
                palme.transform.localScale = Vector3.one * (0.85f + (float)zufall.NextDouble() * 0.3f);
                palme.AddComponent<MeshFilter>().sharedMesh = modelle[gesetzt % varianten];
                palme.AddComponent<MeshRenderer>().sharedMaterials = materialien;

                // Einfache Kollision am unteren Stamm, damit man nicht hindurchläuft
                var kollision = palme.AddComponent<CapsuleCollider>();
                kollision.radius = 0.25f;
                kollision.height = 3f;
                kollision.center = new Vector3(0f, 1.5f, 0f);
                gesetzt++;
            }
        }

        static void SpielerAnlegen(WeltDaten daten)
        {
            var spieler = new GameObject("Spieler");
            // Start am Strand, kurz vor dem Steg
            spieler.transform.position = KugelGenerator.PunktAufInsel(daten, Mathf.Max(0f, daten.inselRadius * daten.Form(0f) - 10f), 1.2f);
            spieler.transform.rotation = Quaternion.FromToRotation(Vector3.up, spieler.transform.position.normalized);

            var kapsel = spieler.AddComponent<CapsuleCollider>();
            kapsel.height = 1.8f;
            kapsel.radius = 0.35f;
            spieler.AddComponent<Rigidbody>().mass = 70f;

            var blick = new GameObject("Blick").transform;
            blick.SetParent(spieler.transform, false);

            var kameraObjekt = new GameObject("Kamera");
            kameraObjekt.tag = "MainCamera";
            kameraObjekt.transform.SetParent(blick, false);
            kameraObjekt.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            var kamera = kameraObjekt.AddComponent<Camera>();
            kamera.nearClipPlane = 0.05f;
            kamera.farClipPlane = 3000f;
            kameraObjekt.AddComponent<AudioListener>();

            kameraObjekt.AddComponent<UnterwasserEffekt>();

            var steuerung = spieler.AddComponent<SpielerSteuerung>();
            steuerung.blick = blick;
            steuerung.kamera = kameraObjekt.transform;
        }

        static WeltDaten DatenLadenOderAnlegen()
        {
            var daten = AssetDatabase.LoadAssetAtPath<WeltDaten>(DatenPfad);
            if (daten != null) return daten;
            if (!AssetDatabase.IsValidFolder("Assets/Daten")) AssetDatabase.CreateFolder("Assets", "Daten");
            daten = ScriptableObject.CreateInstance<WeltDaten>();
            AssetDatabase.CreateAsset(daten, DatenPfad);
            return daten;
        }

        static GameObject ObjektMitMesh(string name, Transform eltern, Mesh mesh, Material material)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(eltern, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            return obj;
        }

        static Mesh MeshSpeichern(Mesh mesh)
        {
            OrdnerSicherstellen();
            string pfad = $"{GeneriertOrdner}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(pfad);
            AssetDatabase.CreateAsset(mesh, pfad);
            return mesh;
        }

        internal static Material MaterialHolen(string name, Color farbe, bool durchsichtig)
        {
            OrdnerSicherstellen();
            string pfad = $"{GeneriertOrdner}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, pfad);
            }
            mat.SetColor("_BaseColor", farbe);

            if (durchsichtig)
            {
                mat.SetFloat("_Surface", 1f);   // Transparent
                mat.SetFloat("_Blend", 0f);     // Alpha
                mat.SetFloat("_Cull", 0f);      // beide Seiten zeichnen (auch von unter Wasser sichtbar)
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_Smoothness", 0.95f); // glänzend: Sonnenglitzer
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static void OrdnerSicherstellen()
        {
            if (!AssetDatabase.IsValidFolder(GeneriertOrdner)) AssetDatabase.CreateFolder("Assets", "Generiert");
        }
    }
}
