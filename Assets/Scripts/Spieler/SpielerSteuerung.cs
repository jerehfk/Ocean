using UnityEngine;
using UnityEngine.InputSystem;

namespace Ocean
{
    /// <summary>
    /// Ego-Steuerung auf dem Kugelplaneten.
    /// Aufbau: Spieler (Rigidbody + Kapsel) → Blick (dreht nach links/rechts) → Kamera (neigt nach oben/unten).
    /// Der Körper richtet sich nur nach „oben“ aus; das Umsehen passiert im Kind „Blick“,
    /// damit die Maus flüssig bleibt, auch wenn die Physik in festen Schritten rechnet.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class SpielerSteuerung : MonoBehaviour
    {
        [Header("Aufbau")]
        public Transform blick;
        public Transform kamera;

        [Header("Bewegung")]
        public float laufTempo = 4f;
        public float sprintTempo = 7f;
        public float sprungTempo = 5f;
        [Tooltip("Wie schnell das Tempo am Boden erreicht wird (m/s²).")]
        public float beschleunigung = 40f;
        [Tooltip("Anteil der Steuerbarkeit in der Luft.")]
        [Range(0f, 1f)] public float luftKontrolle = 0.3f;
        [Tooltip("Wird von außen gesetzt, z. B. 0,9 mit vollem Eimer.")]
        public float tempoFaktor = 1f;

        [Header("Umsehen")]
        public float mausEmpfindlichkeit = 0.1f;
        public float maxNeigung = 85f;

        Rigidbody rb;
        CapsuleCollider kapsel;
        InputAction bewegen, umsehen, springen, sprinten;
        float neigung;
        bool sprungGewuenscht;
        bool amBoden;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            kapsel = GetComponent<CapsuleCollider>();
            rb.useGravity = false;                 // eigene Schwerkraft zum Mittelpunkt
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            bewegen = new InputAction("Bewegen", InputActionType.Value);
            bewegen.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            umsehen = new InputAction("Umsehen", InputActionType.Value, "<Mouse>/delta");
            springen = new InputAction("Springen", InputActionType.Button, "<Keyboard>/space");
            sprinten = new InputAction("Sprinten", InputActionType.Button, "<Keyboard>/leftShift");
        }

        void OnEnable()
        {
            bewegen.Enable(); umsehen.Enable(); springen.Enable(); sprinten.Enable();
            Cursor.lockState = CursorLockMode.Locked;
        }

        void OnDisable()
        {
            bewegen.Disable(); umsehen.Disable(); springen.Disable(); sprinten.Disable();
            Cursor.lockState = CursorLockMode.None;
        }

        void Update()
        {
            // Tab: Shop auf/zu. Esc: Shop zu – sonst Pausemenü auf/zu (Maus frei = Pause)
            var tastatur = Keyboard.current;
            if (tastatur != null && tastatur.tabKey.wasPressedThisFrame)
                ShopFenster.Umschalten();
            else if (tastatur != null && tastatur.escapeKey.wasPressedThisFrame)
            {
                if (ShopFenster.Offen) ShopFenster.Schliessen();
                else Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 maus = umsehen.ReadValue<Vector2>() * mausEmpfindlichkeit;
                blick.Rotate(Vector3.up, maus.x, Space.Self);
                neigung = Mathf.Clamp(neigung - maus.y, -maxNeigung, maxNeigung);
                kamera.localRotation = Quaternion.Euler(neigung, 0f, 0f);
            }

            if (springen.WasPressedThisFrame() && Cursor.lockState == CursorLockMode.Locked) sprungGewuenscht = true;
        }

        void FixedUpdate()
        {
            var schwerkraft = PlanetSchwerkraft.Instanz;
            if (schwerkraft == null) return;

            Vector3 oben = schwerkraft.Oben(rb.position);

            // 1. Körper nach „oben“ ausrichten
            Quaternion ausrichtung = Quaternion.FromToRotation(rb.rotation * Vector3.up, oben) * rb.rotation;
            rb.MoveRotation(ausrichtung);

            // 2. Bodenprüfung: kurzer Strahl von der Kapselmitte nach unten
            float strahl = kapsel.height * 0.5f + 0.15f;
            amBoden = Physics.Raycast(rb.position, -oben, strahl, ~0, QueryTriggerInteraction.Ignore);

            // 3. Geschwindigkeit in „hoch/runter“ und „entlang der Oberfläche“ zerlegen
            Vector3 v = rb.linearVelocity;
            Vector3 senkrecht = Vector3.Dot(v, oben) * oben;
            Vector3 waagerecht = v - senkrecht;

            // 4. Gewünschte Laufrichtung aus der Blickrichtung
            // Im Pausemenü keine Bewegung (die Welt läuft im Mehrspieler aber weiter)
            Vector2 eingabe = Cursor.lockState == CursorLockMode.Locked ? bewegen.ReadValue<Vector2>() : Vector2.zero;
            Vector3 vor = Vector3.ProjectOnPlane(blick.forward, oben).normalized;
            Vector3 rechts = Vector3.Cross(oben, vor);
            float tempo = (sprinten.IsPressed() ? sprintTempo : laufTempo) * tempoFaktor;
            Vector3 ziel = Vector3.ClampMagnitude(vor * eingabe.y + rechts * eingabe.x, 1f) * tempo;

            float kontrolle = amBoden ? 1f : luftKontrolle;
            waagerecht = Vector3.MoveTowards(waagerecht, ziel, beschleunigung * kontrolle * Time.fixedDeltaTime);

            // 5. Springen und Schwerkraft
            if (sprungGewuenscht && amBoden)
                senkrecht = oben * sprungTempo;
            sprungGewuenscht = false;
            senkrecht -= oben * schwerkraft.staerke * Time.fixedDeltaTime;

            rb.linearVelocity = waagerecht + senkrecht;
        }
    }
}
