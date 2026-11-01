using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Sitzt auf der Kamera: Ist die Kamera unter der Wasseroberfläche,
    /// wird die Sicht trüb und blaugrün (Nebel). Darüber ist der Nebel aus.
    /// </summary>
    public class UnterwasserEffekt : MonoBehaviour
    {
        public Color farbe = new Color(0.04f, 0.28f, 0.36f);
        [Tooltip("Je höher, desto kürzer die Sicht unter Wasser.")]
        public float dichte = 0.05f;

        Wasser wasser;

        void Update()
        {
            var schwerkraft = PlanetSchwerkraft.Instanz;
            if (wasser == null) wasser = FindAnyObjectByType<Wasser>();
            if (schwerkraft == null || wasser == null) return;

            bool unterWasser = schwerkraft.Hoehe(transform.position) < wasser.Radius;
            RenderSettings.fog = unterWasser;
            if (unterWasser)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = farbe;
                RenderSettings.fogDensity = dichte;
            }
        }

        void OnDisable() => RenderSettings.fog = false;
    }
}
