using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Shop-Gebäude auf der Insel. Am Tresen öffnet man mit E den Shop (Kaufen und Forschung).
    /// Steht fest in der Szene (wird vom Welt-Aufbau angelegt), braucht daher kein Netzwerk-Objekt.
    /// </summary>
    public class Shopgebaeude : MonoBehaviour
    {
        public static Shopgebaeude Instanz { get; private set; }

        [Tooltip("Hierauf zielen und E drücken, um den Shop zu öffnen.")]
        public Collider tresen;

        void Awake() => Instanz = this;

        void OnDestroy()
        {
            if (Instanz == this) Instanz = null;
        }

        /// <summary>Abstand eines Punktes zum Tresen (m).</summary>
        public float Abstand(Vector3 punkt) => tresen != null ? Vector3.Distance(punkt, tresen.transform.position) : float.MaxValue;
    }
}
