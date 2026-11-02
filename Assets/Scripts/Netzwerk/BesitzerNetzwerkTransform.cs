using Unity.Netcode.Components;

namespace Ocean
{
    /// <summary>
    /// Position/Drehung werden vom Besitzer (dem Spieler an seinem Rechner) bestimmt und an
    /// alle anderen übertragen. Ohne das müsste jede Bewegung erst zum Host und zurück –
    /// die eigene Steuerung würde sich träge anfühlen. Für ein Koop-Spiel ohne Wettkampf
    /// ist das die übliche, einfache Lösung.
    /// </summary>
    public class BesitzerNetzwerkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
