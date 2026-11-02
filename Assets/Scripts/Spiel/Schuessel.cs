using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ocean
{
    /// <summary>
    /// Satellitenschüssel: empfängt das Bild des Satelliten am Mars. Eine reicht für alle Bildschirme.
    /// Der Kopf dreht sich von selbst zum Mars am Himmel.
    /// </summary>
    public class Schuessel : NetworkBehaviour
    {
        public static readonly List<Schuessel> Alle = new();

        [Tooltip("Drehbarer Kopf mit der Schüssel (zeigt mit +Z zum Mars).")]
        public Transform kopf;

        public override void OnNetworkSpawn() => Alle.Add(this);
        public override void OnNetworkDespawn() => Alle.Remove(this);

        void Update()
        {
            if (kopf == null || Mars.Instanz == null) return;
            Vector3 ziel = (Mars.Instanz.HimmelsPunkt - kopf.position).normalized;
            var soll = Quaternion.LookRotation(ziel, transform.up);
            kopf.rotation = Quaternion.Slerp(kopf.rotation, soll, 1f - Mathf.Exp(-1.5f * Time.deltaTime));
        }
    }
}
