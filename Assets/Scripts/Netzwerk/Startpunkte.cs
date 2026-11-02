using UnityEngine;

namespace Ocean
{
    /// <summary>Liste der Startplätze am Strand. Neue Spieler bekommen der Reihe nach einen davon.</summary>
    public class Startpunkte : MonoBehaviour
    {
        public Transform[] punkte;

        public Transform Punkt(int nummer) =>
            punkte == null || punkte.Length == 0 ? transform : punkte[Mathf.Abs(nummer) % punkte.Length];
    }
}
