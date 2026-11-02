using UnityEngine;

namespace Ocean
{
    /// <summary>Markiert Boden, auf dem gebaut werden darf (Insel, Meeresboden, Steg).</summary>
    public class Baugrund : MonoBehaviour
    {
        /// <summary>
        /// Sucht senkrecht unter einem Punkt den Boden (Insel, Steg, Meeresboden). Andere Dinge
        /// wie Pumpen, Palmen oder Spieler werden übersprungen.
        /// </summary>
        public static bool BodenUnter(Vector3 punkt, Vector3 oben, out Vector3 boden)
        {
            boden = punkt;
            float naechster = float.MaxValue;
            bool gefunden = false;
            foreach (var treffer in Physics.RaycastAll(punkt + oben * 20f, -oben, 200f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (treffer.distance >= naechster || treffer.collider.GetComponentInParent<Baugrund>() == null) continue;
                naechster = treffer.distance;
                boden = treffer.point;
                gefunden = true;
            }
            return gefunden;
        }
    }
}
