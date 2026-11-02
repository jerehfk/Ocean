using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
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

        public static void Starten()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var ablauf = Ablauf();
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
            NetworkManager.Singleton.StartHost();
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

        static void Beenden()
        {
            Application.logMessageReceived -= LogAbfangen;
            foreach (var f in Fehler) Debug.Log("SPIELTEST Meldung: " + f);
            Debug.Log(Fehler.Count == 0 ? "SPIELTEST BESTANDEN" : $"SPIELTEST DURCHGEFALLEN ({Fehler.Count} Meldungen)");
            EditorApplication.Exit(Fehler.Count == 0 ? 0 : 1);
        }
    }
}
