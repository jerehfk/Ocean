using System.Collections.Generic;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Anschlusspunkt für Rohre: am Tank, am Auslass jeder Pumpe und an beiden Enden jedes Rohrs.
    /// Rohre rasten beim Bauen an Anschlüssen ein. Anschlüsse, die (fast) an derselben Stelle liegen,
    /// gelten als verbunden – so entstehen Abzweigungen ganz von selbst.
    /// </summary>
    public class Anschluss : MonoBehaviour
    {
        public enum Typ { Tank, Pumpe, Rohr }

        public Typ typ;
        [Tooltip("Bei Typ Pumpe: die zugehörige Pumpe.")]
        public Pumpe pumpe;
        [Tooltip("Bei Typ Rohr: das zugehörige Rohr.")]
        public Rohr rohr;

        public static readonly List<Anschluss> Alle = new List<Anschluss>();

        void OnEnable() => Alle.Add(this);
        void OnDisable() => Alle.Remove(this);

        /// <summary>Nächster Anschluss im Umkreis (oder null).</summary>
        public static Anschluss Naechster(Vector3 position, float umkreis, Anschluss ausser = null)
        {
            Anschluss bester = null;
            float besterAbstand = umkreis;
            foreach (var a in Alle)
            {
                if (a == ausser) continue;
                float abstand = Vector3.Distance(a.transform.position, position);
                if (abstand < besterAbstand) { besterAbstand = abstand; bester = a; }
            }
            return bester;
        }
    }
}
