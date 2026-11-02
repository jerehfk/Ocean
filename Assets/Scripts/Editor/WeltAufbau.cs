using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine.UIElements;

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
            foreach (string name in new[] { "Welt", "Spieler", "Main Camera", "Startpunkte", "Menuekamera", "Netzwerk", "Oberflaeche", "Startrampe", "Spielstand" })
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

            SpielWerte werte = WerteLadenOderAnlegen();
            PalmenAnlegen(daten, welt.transform);
            StartrampeAnlegen(daten, werte);
            new GameObject("Spielstand").AddComponent<NetworkObject>().gameObject.AddComponent<Spielstand>();
            NetzwerkAnlegen(daten, SpielerVorlageErstellen(werte));
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
                if (new Vector2(x - RampeX, z).magnitude < 7f) continue; // Platz für die Startrampe

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

        const string SpielerVorlagePfad = "Assets/Vorlagen/Spieler.prefab";

        /// <summary>
        /// Baut die Spielfigur als Vorlage (Prefab). Im Netzwerkspiel erzeugt Netcode daraus für jeden
        /// Spieler eine eigene Figur. Aufbau: Spieler (Physik, Netz) → Blick (links/rechts) → Kamera (hoch/runter).
        /// </summary>
        static GameObject SpielerVorlageErstellen(SpielWerte werte)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Vorlagen")) AssetDatabase.CreateFolder("Assets", "Vorlagen");

            var spieler = new GameObject("Spieler");
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
            NachbearbeitungAn(kameraObjekt);
            var hoerer = kameraObjekt.AddComponent<AudioListener>();
            var unterwasser = kameraObjekt.AddComponent<UnterwasserEffekt>();
            unterwasser.enabled = false;

            // Platzhalter-Astronaut: weißer Körper, Helm dreht mit dem Blick, Visier dunkel,
            // Gürtel und Helmring in der Spielerfarbe
            var weiss = MaterialHolen("Anzug", new Color(0.93f, 0.93f, 0.95f), false);
            var visierMat = MaterialHolen("Visier", new Color(0.05f, 0.06f, 0.08f), false);
            visierMat.SetFloat("_Smoothness", 0.95f);
            var akzentMat = MaterialHolen("Akzent", Color.white, false);

            var rumpf = Teil(PrimitiveType.Capsule, "Rumpf", spieler.transform, new Vector3(0f, -0.15f, 0f), new Vector3(0.7f, 0.6f, 0.6f), weiss);
            var guertel = Teil(PrimitiveType.Cylinder, "Guertel", spieler.transform, new Vector3(0f, -0.35f, 0f), new Vector3(0.74f, 0.05f, 0.64f), akzentMat);
            var helm = Teil(PrimitiveType.Sphere, "Helm", blick, new Vector3(0f, 0.65f, 0f), Vector3.one * 0.62f, weiss);
            var visier = Teil(PrimitiveType.Sphere, "Visier", blick, new Vector3(0f, 0.66f, 0.17f), new Vector3(0.48f, 0.36f, 0.32f), visierMat);
            var helmring = Teil(PrimitiveType.Cylinder, "Helmring", blick, new Vector3(0f, 0.36f, 0f), new Vector3(0.5f, 0.03f, 0.5f), akzentMat);

            // Steuerung ist in der Vorlage aus – SpielerNetz schaltet sie nur für die eigene Figur ein
            var steuerung = spieler.AddComponent<SpielerSteuerung>();
            steuerung.blick = blick;
            steuerung.kamera = kameraObjekt.transform;
            steuerung.enabled = false;
            kamera.enabled = false;
            hoerer.enabled = false;

            // Netzwerk: Figur ist ein Netzwerk-Objekt; Position und Blickrichtung bestimmt der Besitzer
            spieler.AddComponent<NetworkObject>();
            var netzTransform = spieler.AddComponent<BesitzerNetzwerkTransform>();
            netzTransform.SyncScaleX = netzTransform.SyncScaleY = netzTransform.SyncScaleZ = false;
            spieler.AddComponent<NetworkRigidbody>();
            var blickTransform = blick.gameObject.AddComponent<BesitzerNetzwerkTransform>();
            blickTransform.InLocalSpace = true;
            blickTransform.SyncPositionX = blickTransform.SyncPositionY = blickTransform.SyncPositionZ = false;
            blickTransform.SyncScaleX = blickTransform.SyncScaleY = blickTransform.SyncScaleZ = false;

            // Eimer in der Hand: Kind der Kamera, unten rechts im Bild
            var eimer = Teil(PrimitiveType.Cylinder, "Eimer", kameraObjekt.transform, new Vector3(0.32f, -0.32f, 0.62f), new Vector3(0.26f, 0.14f, 0.26f),
                MaterialHolen("Eimer", new Color(0.98f, 0.78f, 0.18f), false));
            var eimerWasser = Teil(PrimitiveType.Cylinder, "Wasser im Eimer", eimer.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.88f, 0.03f, 0.88f),
                MaterialHolen("Eimerwasser", new Color(0.15f, 0.62f, 0.75f), false));
            eimerWasser.gameObject.SetActive(false);

            var aktionen = spieler.AddComponent<SpielerAktionen>();
            aktionen.werte = werte;
            aktionen.kamera = kamera;
            aktionen.steuerung = steuerung;
            aktionen.eimer = eimer.transform;
            aktionen.eimerWasser = eimerWasser.gameObject;

            var netz = spieler.AddComponent<SpielerNetz>();
            netz.steuerung = steuerung;
            netz.kamera = kamera;
            netz.hoerer = hoerer;
            netz.unterwasser = unterwasser;
            netz.koerper = new Renderer[] { rumpf, guertel, helm, visier, helmring };
            netz.akzentTeile = new Renderer[] { guertel, helmring };

            var vorlage = PrefabUtility.SaveAsPrefabAsset(spieler, SpielerVorlagePfad);
            Object.DestroyImmediate(spieler);
            return vorlage;
        }

        static void NachbearbeitungAn(GameObject kamera)
        {
            var daten = kamera.GetComponent<UniversalAdditionalCameraData>();
            if (daten == null) daten = kamera.AddComponent<UniversalAdditionalCameraData>();
            daten.renderPostProcessing = true;
        }

        static Renderer Teil(PrimitiveType form, string name, Transform eltern, Vector3 position, Vector3 groesse, Material material)
        {
            var teil = GameObject.CreatePrimitive(form);
            teil.name = name;
            Object.DestroyImmediate(teil.GetComponent<Collider>()); // Kollision macht nur die Kapsel
            teil.transform.SetParent(eltern, false);
            teil.transform.localPosition = position;
            teil.transform.localScale = groesse;
            var renderer = teil.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        /// <summary>Startplätze, Menükamera und Netzwerk-Objekt (NetworkManager + Menü) in die Szene.</summary>
        static void NetzwerkAnlegen(WeltDaten daten, GameObject spielerVorlage)
        {
            // Startplätze nebeneinander am Strand, mit Blick zum Steg (+X)
            var start = new GameObject("Startpunkte").AddComponent<Startpunkte>();
            float abstandZurMitte = Mathf.Max(0f, daten.inselRadius * daten.Form(0f) - 10f);
            var punkte = new Transform[6];
            for (int i = 0; i < punkte.Length; i++)
            {
                float seitlich = (i - (punkte.Length - 1) * 0.5f) * 1.6f;
                Vector3 position = KugelGenerator.PunktAufBoden(daten, abstandZurMitte, seitlich, 1.2f, out Vector3 oben);
                Vector3 vor = Vector3.ProjectOnPlane(Vector3.right, oben).normalized;
                var punkt = new GameObject($"Start {i + 1}").transform;
                punkt.SetParent(start.transform, false);
                punkt.SetPositionAndRotation(position, Quaternion.LookRotation(vor, oben));
                punkte[i] = punkt;
            }
            start.punkte = punkte;

            // Menükamera: schräg über dem Wasser mit Blick auf die Insel
            var menueKamera = new GameObject("Menuekamera").AddComponent<Camera>();
            KugelGenerator.PunktAufBoden(daten, -35f, -25f, 0f, out Vector3 kameraOben);
            Vector3 kameraPos = kameraOben * (daten.wasserRadius + 9f); // 9 m über dem Wasser
            menueKamera.transform.position = kameraPos;
            menueKamera.transform.rotation = Quaternion.LookRotation(Vector3.up * (daten.wasserRadius + 2f) - kameraPos, kameraOben);
            menueKamera.farClipPlane = 3000f;
            NachbearbeitungAn(menueKamera.gameObject);
            menueKamera.gameObject.AddComponent<AudioListener>();

            // Netzwerk
            var netzObjekt = new GameObject("Netzwerk");
            var netz = netzObjekt.AddComponent<NetworkManager>();
            var transport = netzObjekt.AddComponent<UnityTransport>();
            if (netz.NetworkConfig == null) netz.NetworkConfig = new NetworkConfig();
            netz.NetworkConfig.NetworkTransport = transport;
            netz.NetworkConfig.PlayerPrefab = spielerVorlage;
            netz.NetworkConfig.ConnectionApproval = true;
            var menue = netzObjekt.AddComponent<Netzwerkmenue>();
            menue.spielerVorlage = spielerVorlage;
            menue.menueKamera = menueKamera;
            menue.startpunkte = start;

            // Oberfläche (UI Toolkit): Aufbau in Assets/UI/Menue.uxml, Gestaltung in Menue.uss
            var oberflaeche = new GameObject("Oberflaeche").AddComponent<UIDocument>();
            oberflaeche.panelSettings = PanelEinstellungenHolen();
            oberflaeche.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Menue.uxml");
            if (oberflaeche.visualTreeAsset == null) Debug.LogWarning("Ocean: Assets/UI/Menue.uxml nicht gefunden.");
            menue.oberflaeche = oberflaeche;
            oberflaeche.gameObject.AddComponent<Anzeige>().oberflaeche = oberflaeche;
        }

        static PanelSettings PanelEinstellungenHolen()
        {
            const string pfad = "Assets/UI/MenuePanel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(pfad);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, pfad);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/OceanTheme.tss");
            // Größe passt sich dem Bildschirm an, gestaltet ist alles für 1920 × 1080
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);
            return panel;
        }

        const float RampeX = -3f; // Startrampe fast in der Inselmitte

        /// <summary>
        /// Startrampe: runde Plattform mit Rakete, daneben Tank (Eimer leeren) und Startknopf.
        /// Platzhalter aus Grundformen; Netzwerk-Objekt, damit Tank und Flug bei allen gleich sind.
        /// </summary>
        static void StartrampeAnlegen(WeltDaten daten, SpielWerte werte)
        {
            Vector3 boden = KugelGenerator.PunktAufBoden(daten, RampeX, 0f, 0f, out Vector3 oben);
            var rampe = new GameObject("Startrampe");
            rampe.transform.SetPositionAndRotation(boden, Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.right, oben), oben));
            var t = rampe.transform;

            var weiss = MaterialHolen("Anzug", new Color(0.93f, 0.93f, 0.95f), false);
            var grau = MaterialHolen("Metall", new Color(0.55f, 0.58f, 0.62f), false);
            var tuerkis = MaterialHolen("Tuerkis", new Color(0.06f, 0.66f, 0.72f), false);
            var rot = MaterialHolen("Knopf", new Color(0.90f, 0.20f, 0.18f), false);

            // Plattform (begehbar)
            var plattform = Teil(PrimitiveType.Cylinder, "Plattform", t, new Vector3(0f, 0.2f, 0f), new Vector3(6f, 0.25f, 6f), grau);
            plattform.gameObject.AddComponent<MeshCollider>().sharedMesh = plattform.GetComponent<MeshFilter>().sharedMesh;

            // Rakete (ohne Kollision; fliegt beim Start nach oben)
            var rakete = new GameObject("Rakete").transform;
            rakete.SetParent(t, false);
            rakete.localPosition = new Vector3(0f, 0.45f, 0f);
            Teil(PrimitiveType.Cylinder, "Rumpf", rakete, new Vector3(0f, 2.5f, 0f), new Vector3(1.1f, 2.5f, 1.1f), weiss);
            Teil(PrimitiveType.Sphere, "Spitze", rakete, new Vector3(0f, 5.0f, 0f), new Vector3(1.1f, 1.6f, 1.1f), tuerkis);
            Teil(PrimitiveType.Sphere, "Fenster", rakete, new Vector3(0f, 3.6f, 0.5f), new Vector3(0.45f, 0.45f, 0.2f), MaterialHolen("Visier", new Color(0.05f, 0.06f, 0.08f), false));
            for (int i = 0; i < 3; i++)
            {
                var flosse = Teil(PrimitiveType.Cube, $"Flosse {i + 1}", rakete, Vector3.zero, new Vector3(0.08f, 1.4f, 0.9f), tuerkis).transform;
                flosse.localRotation = Quaternion.Euler(0f, i * 120f, 0f);
                flosse.localPosition = flosse.localRotation * new Vector3(0f, 0.7f, 0.75f);
            }

            // Tank neben der Plattform (Ziel zum Eimer-Leeren)
            var tank = Teil(PrimitiveType.Cylinder, "Tank", t, new Vector3(-4.2f, 1.1f, 0f), new Vector3(1.8f, 1.1f, 1.8f), weiss);
            tank.gameObject.AddComponent<CapsuleCollider>();
            Teil(PrimitiveType.Cylinder, "Tankring", t, new Vector3(-4.2f, 1.6f, 0f), new Vector3(1.86f, 0.06f, 1.86f), tuerkis);

            // Startknopf auf einer Säule
            var saeule = Teil(PrimitiveType.Cube, "Startknopf", t, new Vector3(-2.6f, 0.55f, 2.6f), new Vector3(0.45f, 1.1f, 0.45f), grau);
            var knopfKollision = saeule.gameObject.AddComponent<BoxCollider>();
            Teil(PrimitiveType.Cylinder, "Knopf", saeule.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.6f, 0.08f, 0.6f), rot);

            rampe.AddComponent<NetworkObject>();
            var logik = rampe.AddComponent<Startrampe>();
            logik.werte = werte;
            logik.rakete = rakete;
            logik.tankZiel = tank.GetComponent<Collider>();
            logik.startknopf = knopfKollision;
        }

        static SpielWerte WerteLadenOderAnlegen()
        {
            const string pfad = "Assets/Daten/SpielWerte.asset";
            var werte = AssetDatabase.LoadAssetAtPath<SpielWerte>(pfad);
            if (werte != null) return werte;
            werte = ScriptableObject.CreateInstance<SpielWerte>();
            AssetDatabase.CreateAsset(werte, pfad);
            return werte;
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
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
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
