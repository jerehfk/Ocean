using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ocean.EditorWerkzeuge
{
    /// <summary>
    /// Grundlagen der Optik: Taghimmel, Sonne, Nachbearbeitung, Mars am Himmel, Sandtextur.
    /// Wird von „Welt aufbauen“ mit ausgeführt; einzeln über „Ocean → Optik anwenden“.
    /// </summary>
    public static class OptikAufbau
    {
        const string Ordner = "Assets/Generiert";

        // Richtung, aus der die Sonne scheint, und Richtung, in der der Mars steht (Weltkoordinaten,
        // Inselmitte liegt oben = +Y). Mars bewusst nicht in Sonnennähe, damit er gut sichtbar ist.
        static readonly Vector3 SonneRotation = new Vector3(50f, -35f, 0f);
        static readonly Vector3 MarsRichtung = new Vector3(0.45f, 0.75f, 0.48f);
        const float MarsAbstand = 2500f;
        const float MarsRadius = 180f;   // erscheint etwa 8° groß – deutlich größer als unser Mond

        [MenuItem("Ocean/Optik anwenden")]
        public static void NurOptik()
        {
            var welt = GameObject.Find("Welt");
            Anwenden(welt != null ? welt.transform : null);
        }

        public static void Anwenden(Transform welt)
        {
            WeltAufbau.OrdnerSicherstellen();
            Light sonne = SonneEinrichten();
            HimmelEinrichten(sonne);
            NachbearbeitungEinrichten();
            if (welt != null) MarsAnlegen(welt, WasserRadius());
            SchattenweiteSetzen(150f);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        static Light SonneEinrichten()
        {
            Light sonne = null;
            foreach (var licht in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (licht.type == LightType.Directional) { sonne = licht; break; }

            if (sonne == null)
            {
                sonne = new GameObject("Sonne").AddComponent<Light>();
                sonne.type = LightType.Directional;
            }
            sonne.name = "Sonne";
            sonne.transform.rotation = Quaternion.Euler(SonneRotation);
            sonne.color = new Color(1f, 0.96f, 0.88f);  // leicht warmes Tageslicht
            sonne.intensity = 1.1f;
            sonne.shadows = LightShadows.Soft;
            sonne.shadowStrength = 0.9f;
            return sonne;
        }

        static void HimmelEinrichten(Light sonne)
        {
            string pfad = $"{Ordner}/Himmel.mat";
            var himmel = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (himmel == null)
            {
                himmel = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(himmel, pfad);
            }
            himmel.SetFloat("_SunSize", 0.04f);
            himmel.SetFloat("_AtmosphereThickness", 1.0f);
            himmel.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.65f));
            // Auf dem Kugelplaneten liegt der Horizont unter der Waagerechten (~5° bei 500 m Radius).
            // Dort zeigt der Himmel seine „Bodenfarbe“ – sie muss daher wie Dunst am Horizont aussehen.
            himmel.SetColor("_GroundColor", new Color(0.72f, 0.80f, 0.88f));
            himmel.SetFloat("_Exposure", 1.0f);
            EditorUtility.SetDirty(himmel);

            RenderSettings.skybox = himmel;
            RenderSettings.sun = sonne;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = false;
            DynamicGI.UpdateEnvironment();
        }

        static void NachbearbeitungEinrichten()
        {
            string pfad = $"{Ordner}/Nachbearbeitung.asset";
            AssetDatabase.DeleteAsset(pfad);
            var profil = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profil, pfad);

            var bloom = profil.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.5f);
            bloom.scatter.Override(0.6f);

            var tonemapping = profil.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var farbe = profil.Add<ColorAdjustments>(true);
            farbe.saturation.Override(12f);
            farbe.contrast.Override(5f);

            // Komponenten müssen als Teil der Profil-Datei gespeichert werden
            foreach (var komponente in profil.components)
            {
                komponente.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(komponente, profil);
            }
            AssetDatabase.SaveAssets();

            var alt = GameObject.Find("Nachbearbeitung");
            if (alt != null) Object.DestroyImmediate(alt);
            var volume = new GameObject("Nachbearbeitung").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;   // vor dem „Global Volume“ der Vorlage
            volume.sharedProfile = profil;

            // Nachbearbeitung an der Spielerkamera einschalten
            var kamera = GameObject.Find("Kamera");
            if (kamera != null)
            {
                var daten = kamera.GetComponent<UniversalAdditionalCameraData>();
                if (daten == null) daten = kamera.AddComponent<UniversalAdditionalCameraData>();
                daten.renderPostProcessing = true;
            }
        }

        static float WasserRadius()
        {
            var wasser = Object.FindAnyObjectByType<Wasser>();
            return wasser != null ? wasser.Radius : 500f;
        }

        static void MarsAnlegen(Transform welt, float wasserRadius)
        {
            var alt = welt.Find("Mars");
            if (alt != null) Object.DestroyImmediate(alt.gameObject);

            var mars = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mars.name = "Mars";
            Object.DestroyImmediate(mars.GetComponent<Collider>());
            mars.transform.SetParent(welt, false);
            // vom Inselpunkt (Nordpol) aus gemessen, damit der Abstand bei jeder Planetengröße stimmt
            mars.transform.position = Vector3.up * wasserRadius + MarsRichtung.normalized * MarsAbstand;
            mars.transform.localScale = Vector3.one * MarsRadius * 2f;

            // Ungeleuchtet und leicht durchscheinend: wirkt blass wie der Mond am Taghimmel
            string pfad = $"{Ordner}/Mars.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, pfad);
            }
            mat.SetColor("_BaseColor", new Color(0.92f, 0.58f, 0.42f, 0.7f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);

            var renderer = mars.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static void SchattenweiteSetzen(float meter)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = meter;
                EditorUtility.SetDirty(urp);
            }
        }

        /// <summary>Feine, helle Sandkörnung als Textur (einmal erzeugt, dann wiederverwendet).</summary>
        public static Texture2D SandTextur()
        {
            string pfad = $"{Ordner}/SandTextur.png";
            AssetDatabase.DeleteAsset(pfad); // immer neu erzeugen, damit Änderungen hier wirken

            const int groesse = 512;
            var tex = new Texture2D(groesse, groesse, TextureFormat.RGB24, false);
            var zufall = new System.Random(7);
            for (int y = 0; y < groesse; y++)
            for (int x = 0; x < groesse; x++)
            {
                // Weiche Flecken (Rauschen) + einzelne Körner, alles nahe Weiß,
                // weil die eigentliche Sandfarbe aus dem Material kommt
                float flecken = Mathf.PerlinNoise(x * 0.02f, y * 0.02f) * 0.15f
                              + Mathf.PerlinNoise(x * 0.09f + 50f, y * 0.09f) * 0.08f;
                float korn = (float)zufall.NextDouble() * 0.12f;
                float wert = 1f - flecken - korn;
                if (zufall.NextDouble() < 0.006) wert -= 0.35f; // vereinzelte dunkle Steinchen
                tex.SetPixel(x, y, new Color(wert, wert, wert));
            }
            tex.Apply();
            File.WriteAllBytes(pfad, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(pfad);

            var importer = (TextureImporter)AssetImporter.GetAtPath(pfad);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(pfad);
        }
    }
}
