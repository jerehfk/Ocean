using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Die Wasserkugel. Das Modell ist eine Kugel mit Radius 1; der Pegel
    /// ergibt sich aus der Skalierung. Radius im Inspector ändern = Pegel testen.
    /// </summary>
    [ExecuteAlways]
    public class Wasser : MonoBehaviour
    {
        public static Wasser Instanz { get; private set; }

        [SerializeField, Tooltip("Pegel: Radius der Wasseroberfläche in Metern.")]
        float radius = 500f;

        public float Radius
        {
            get => radius;
            set { radius = Mathf.Max(0f, value); Anwenden(); }
        }

        void OnEnable() { Instanz = this; Anwenden(); }
        void OnValidate() => Anwenden();

        void Anwenden() => transform.localScale = Vector3.one * radius;
    }
}
