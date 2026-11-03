using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ocean.EditorWerkzeuge
{
    /// <summary>
    /// Automatischer Spieltest ohne Oberfläche: startet im Play-Modus einen Solo-Host und prüft Forschung,
    /// Sperren im Shop und das Shop-Gebäude. Aufruf:
    /// Unity -batchmode -nographics -projectPath … -executeMethod Ocean.EditorWerkzeuge.Spieltest.Starten
    /// Ergebnis steht im Log (Zeilen „SPIELTEST“); Exit-Code 0 = alles gut.
    /// </summary>
    public static class Spieltest
    {
        static readonly List<string> Fehler = new();

        public static void Starten() => Los(Ablauf());

        /// <summary>Mars-Spieltest: Satellit, Schüssel, Empfang, Wasserkrater, Satellitenbild (Bilder nach OCEAN_BILD, falls gesetzt).</summary>
        public static void StartenMars() => Los(MarsAblauf());

        static void Los(IEnumerator ablauf)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            EditorApplication.update += () => { try { ablauf.MoveNext(); } catch (Exception e) { Fehler.Add(e.ToString()); Beenden(); } };
            EditorApplication.EnterPlaymode();
        }

        static void LogAbfangen(string text, string stack, LogType typ)
        {
            if (typ == LogType.Exception || typ == LogType.Error) Fehler.Add($"{typ}: {text}");
        }

        static void Pruefen(bool ok, string was)
        {
            Debug.Log($"SPIELTEST {(ok ? "ok    " : "FEHLER")} {was}");
            if (!ok) Fehler.Add(was);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Ablauf()
        {
            while (!EditorApplication.isPlaying || NetworkManager.Singleton == null) yield return null;
            yield return Frames(5);
            Application.logMessageReceived += LogAbfangen;   // erst ab hier: Editor-Eigenmeldungen (Suchindex) ignorieren
            Hosten();
            float t = 0;
            while ((Spielstand.Instanz == null || !Spielstand.Instanz.IsSpawned || Lager.Lokal == null) && t < 20f)
            {
                t += 0.02f;
                yield return null;
            }
            yield return Frames(10);
            var stand = Spielstand.Instanz;
            Pruefen(stand != null && stand.IsSpawned, "Spielstand gestartet");
            if (stand == null || Lager.Lokal == null) { Beenden(); yield break; }
            var lager = Lager.Lokal;

            Pruefen(Shopgebaeude.Instanz != null && Shopgebaeude.Instanz.tresen != null, "Shop-Gebäude mit Tresen in der Szene");
            var spieler = lager.transform.position;
            Pruefen(Shopgebaeude.Instanz != null && Shopgebaeude.Instanz.Abstand(spieler) < 25f, "Shop-Gebäude auf der Insel (Abstand zum Startpunkt)");

            stand.kasse.Value = 10000;
            yield return Frames(3);

            Pruefen(!stand.Freigeschaltet(Upgrade.Handpumpe), "Handpumpe anfangs gesperrt");
            Pruefen(stand.Freigeschaltet(Upgrade.Schuhe), "Schuhe nicht gesperrt");
            stand.GegenstandKaufenRpc((int)Upgrade.Handpumpe);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Handpumpe) == 0 && stand.kasse.Value == 10000, "Gesperrte Handpumpe lässt sich nicht kaufen");

            stand.ForschenRpc((int)Forschung.Motorantrieb);
            yield return Frames(5);
            Pruefen(!stand.Erforscht(Forschung.Motorantrieb) && stand.kasse.Value == 10000, "Motorantrieb ohne Pumpentechnik nicht erforschbar");

            stand.ForschenRpc((int)Forschung.Pumpentechnik);
            yield return Frames(5);
            double preis = stand.werte.Knoten(Forschung.Pumpentechnik).preis;
            Pruefen(stand.Erforscht(Forschung.Pumpentechnik) && stand.kasse.Value == 10000 - preis, "Pumpentechnik erforscht und bezahlt");

            stand.ForschenRpc((int)Forschung.Pumpentechnik);
            yield return Frames(5);
            Pruefen(stand.kasse.Value == 10000 - preis, "Zweimal erforschen kostet nur einmal");

            stand.GegenstandKaufenRpc((int)Upgrade.Handpumpe);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Handpumpe) == 1, "Handpumpe nach Forschung kaufbar");
            stand.GegenstandKaufenRpc((int)Upgrade.Rohr);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Rohr) == stand.werte.rohrPaket, "Rohr nach Forschung kaufbar");

            stand.GegenstandKaufenRpc((int)Upgrade.Motorpumpe);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Motorpumpe) == 0, "Motorpumpe noch gesperrt");
            stand.ForschenRpc((int)Forschung.Motorantrieb);
            yield return Frames(5);
            Pruefen(stand.Erforscht(Forschung.Motorantrieb), "Motorantrieb nach Pumpentechnik erforscht");
            stand.kasse.Value = 10000;
            stand.GegenstandKaufenRpc((int)Upgrade.Motorpumpe);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Motorpumpe) == 1, "Motorpumpe nach Forschung kaufbar");

            stand.GegenstandKaufenRpc((int)Upgrade.Kiste);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Kiste) == 0, "Kiste ohne Lagerbau gesperrt");
            stand.GegenstandKaufenRpc((int)Upgrade.Bildschirm);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Bildschirm) == 0 && !stand.SatellitGestartet, "Bildschirm ohne Raumfahrt gesperrt, Satellit bleibt am Boden");
            stand.ForschenRpc((int)Forschung.Raumfahrt);
            yield return Frames(5);
            Pruefen(!stand.Erforscht(Forschung.Raumfahrt), "Raumfahrt braucht Lagerbau");
            stand.ForschenRpc((int)Forschung.Lagerbau);
            stand.kasse.Value = 5000;
            yield return Frames(5);
            stand.ForschenRpc((int)Forschung.Raumfahrt);
            yield return Frames(5);
            Pruefen(stand.Erforscht(Forschung.Lagerbau) && stand.Erforscht(Forschung.Raumfahrt), "Lagerbau, dann Raumfahrt erforscht");
            stand.kasse.Value = 5000;
            stand.GegenstandKaufenRpc((int)Upgrade.Bildschirm);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Bildschirm) == 1 && stand.SatellitGestartet, "Bildschirm nach Raumfahrt kaufbar, Satellit startet");

            Pruefen(UnityEngine.Object.FindAnyObjectByType<ShopFenster>() != null, "Shop-Fenster vorhanden");
            Beenden();
        }

        /// <summary>Host auf eigenem Port, damit der Test nicht mit einem laufenden Spiel (7777) kollidiert.</summary>
        static void Hosten()
        {
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17777);
            NetworkManager.Singleton.StartHost();
        }

        static IEnumerator MarsAblauf()
        {
            while (!EditorApplication.isPlaying || NetworkManager.Singleton == null) yield return null;
            yield return Frames(5);
            Application.logMessageReceived += LogAbfangen;
            Hosten();
            float t = 0;
            while ((Spielstand.Instanz == null || !Spielstand.Instanz.IsSpawned || Lager.Lokal == null) && t < 20f)
            {
                t += 0.02f;
                yield return null;
            }
            yield return Frames(10);
            var stand = Spielstand.Instanz;
            var lager = Lager.Lokal;
            if (stand == null || lager == null) { Pruefen(false, "Spielstand gestartet"); Beenden(); yield break; }

            var mars = Mars.Instanz;
            Pruefen(mars != null && mars.Bild != null, "Mars und Satellitenbild vorhanden");
            Pruefen(mars != null && mars.GetComponentInChildren<MeshFilter>() != null && mars.transform.Find("Marsboden") != null, "Marsboden gebaut");
            Pruefen(mars != null && Vector3.Distance(mars.zentrum, lager.transform.position) > 3000f, "Mars außerhalb der Sichtweite der Spieler");
            Pruefen(mars != null && mars.HimmelsPunkt.y > 0f && GameObject.Find("Mars am Himmel") != null, "Mars als Scheibe am Himmel");

            stand.kasse.Value = 20000;
            stand.ForschenRpc((int)Forschung.Lagerbau);
            yield return Frames(3);
            stand.ForschenRpc((int)Forschung.Raumfahrt);
            yield return Frames(3);

            Pruefen(!stand.SatellitGestartet, "Satellit anfangs am Boden");
            double vorher = stand.kasse.Value;
            stand.GegenstandKaufenRpc((int)Upgrade.Bildschirm);
            yield return Frames(5);
            Pruefen(stand.SatellitGestartet && !stand.SatellitAngekommen && stand.satellitRest.Value > 0f, "Erster Bildschirm startet den Satelliten");
            Pruefen(vorher - stand.kasse.Value == stand.werte.bildschirmPreisErster, "Erster Bildschirm kostet den Einstiegspreis");

            for (int i = 0; i < 60; i++) yield return null;
            // Im Batchmode ist die Maus nie „gefangen“ → das Spiel gilt als pausiert (Einzelspieler) und die Spielzeit steht.
            bool pausiert = Time.timeScale == 0f;
            bool sinkt = stand.satellitRest.Value < stand.satellitFlugdauer.Value;
            Pruefen(pausiert ? !sinkt : sinkt, pausiert ? "Satellit wartet im Pausemenü (Spielzeit steht)" : "Satellit fliegt (Restzeit sinkt)");

            stand.GegenstandKaufenRpc((int)Upgrade.Bildschirm);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Bildschirm) == 2 && vorher - stand.kasse.Value == stand.werte.bildschirmPreisErster + stand.werte.bildschirmPreis, "Weiterer Bildschirm kostet den Folgepreis");

            stand.GegenstandKaufenRpc((int)Upgrade.Schuessel);
            yield return Frames(5);
            Pruefen(lager.Anzahl(Gegenstand.Schuessel) == 1, "Schüssel gekauft");
            Pruefen(!stand.Empfang, "Ohne aufgestellte Schüssel kein Empfang");

            int platz = -1;
            for (int i = 0; i < lager.Groesse; i++) if (lager.Platz(i).art == Gegenstand.Schuessel) platz = i;
            Vector3 ort = lager.transform.position + lager.transform.forward * 2f;
            stand.PlatzierenRpc(platz, ort, Quaternion.identity);
            yield return Frames(10);
            Pruefen(stand.Empfang && Schuessel.Alle.Count == 1 && lager.Anzahl(Gegenstand.Schuessel) == 0, "Schüssel aufgestellt, Empfang da");

            // Satellit „ankommen“ lassen (Host setzt die Restzeit auf 0)
            stand.satellitRest.Value = 0f;
            yield return Frames(5);
            Pruefen(stand.SatellitAngekommen, "Satellit am Mars angekommen");

            // Wasser: ein halber Krater, dann mehr als ein Krater
            double krater = stand.KraterLiter;
            stand.MarsAnkunft(krater * 0.5);
            yield return Frames(5);
            Pruefen(stand.KraterNummer(stand.marsLiter.Value) == 0 && SichtbareKrater(mars) == 1, "Halber Krater: ein Wasserkrater sichtbar");
            stand.MarsAnkunft(krater * 1.0);
            yield return Frames(5);
            Pruefen(stand.KraterNummer(stand.marsLiter.Value) == 1 && SichtbareKrater(mars) == 2, "Eineinhalb Krater: zweiter füllt sich");

            // Satellitenbild: Kamera läuft nur, wenn angefordert wird
            yield return Frames(3);
            Pruefen(!mars.GetComponentInChildren<Camera>(true).enabled, "Satellitenkamera aus, wenn niemand hinschaut");
            Texture2D bild = null;
            for (int i = 0; i < 40; i++)
            {
                mars.Anfordern();
                yield return null;
            }
            mars.Anfordern();
            yield return Frames(2);
            mars.Anfordern();
            Pruefen(mars.GetComponentInChildren<Camera>(true).enabled, "Satellitenkamera läuft, wenn ein Bildschirm hinschaut");
            bild = Lesen(mars.Bild);
            Pruefen(bild != null && Farbvielfalt(bild) > 20, $"Satellitenbild zeigt Gelände (Farbvielfalt {(bild != null ? Farbvielfalt(bild) : 0)})");
            string ordner = System.Environment.GetEnvironmentVariable("OCEAN_BILD");
            if (!string.IsNullOrEmpty(ordner) && bild != null) File.WriteAllBytes(Path.Combine(ordner, "mars_satellit.png"), bild.EncodeToPNG());

            Beenden();
        }

        static int SichtbareKrater(Mars mars)
        {
            int n = 0;
            foreach (Transform kind in mars.transform) if (kind.name.StartsWith("Kraterwasser") && kind.gameObject.activeSelf) n++;
            return n;
        }

        static Texture2D Lesen(RenderTexture rt)
        {
            if (rt == null) return null;
            var alt = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = alt;
            return tex;
        }

        /// <summary>Anzahl verschiedener (grob gerundeter) Farben im Bild – ein leeres Bild hat 1.</summary>
        static int Farbvielfalt(Texture2D tex)
        {
            var farben = new HashSet<int>();
            var pixel = tex.GetPixels32();
            for (int i = 0; i < pixel.Length; i += 37) farben.Add((pixel[i].r / 16) << 8 | (pixel[i].g / 16) << 4 | pixel[i].b / 16);
            return farben.Count;
        }

        static void Beenden()
        {
            Application.logMessageReceived -= LogAbfangen;
            foreach (var f in Fehler) Debug.Log("SPIELTEST Meldung: " + f);
            Debug.Log(Fehler.Count == 0 ? "SPIELTEST BESTANDEN" : $"SPIELTEST DURCHGEFALLEN ({Fehler.Count} Meldungen)");
            EditorApplication.Exit(Fehler.Count == 0 ? 0 : 1);
        }
    }
}
