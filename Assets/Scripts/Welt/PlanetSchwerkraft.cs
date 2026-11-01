using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Schwerkraft zum Planetenmittelpunkt. Sitzt auf dem Planeten-Objekt;
    /// alles, was fallen soll, fragt hier nach der Richtung.
    /// </summary>
    public class PlanetSchwerkraft : MonoBehaviour
    {
        public static PlanetSchwerkraft Instanz { get; private set; }

        [Tooltip("Fallbeschleunigung in m/s².")]
        public float staerke = 9.81f;

        void OnEnable() => Instanz = this;
        void OnDisable() { if (Instanz == this) Instanz = null; }

        /// <summary>Richtung „oben“ an einer Position (weg vom Mittelpunkt).</summary>
        public Vector3 Oben(Vector3 position) => (position - transform.position).normalized;

        /// <summary>Abstand einer Position zum Planetenmittelpunkt.</summary>
        public float Hoehe(Vector3 position) => Vector3.Distance(position, transform.position);
    }
}
