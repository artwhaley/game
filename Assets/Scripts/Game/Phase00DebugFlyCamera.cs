using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Minimal inspection camera for the Phase 00 showcase.</summary>
    [DisallowMultipleComponent]
    public sealed class Phase00DebugFlyCamera : MonoBehaviour
    {
        [SerializeField] private float movementSpeed = 4f;
        [SerializeField] private float lookSensitivity = 2.25f;

        private float yaw;
        private float pitch;

        private void Start()
        {
            var rotation = transform.eulerAngles;
            yaw = rotation.y;
            pitch = NormalizePitch(rotation.x);
        }

        private void Update()
        {
            // Read the requested debug keys directly. This project also has
            // joystick axes named Horizontal/Vertical; Input.GetAxisRaw merges
            // duplicate names and allowed ordinary stick drift to move the
            // camera forever even when no keyboard key was held.
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.A)) move -= transform.right;
            if (Input.GetKey(KeyCode.D)) move += transform.right;
            if (Input.GetKey(KeyCode.S)) move -= transform.forward;
            if (Input.GetKey(KeyCode.W)) move += transform.forward;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (move.sqrMagnitude > 1f) move.Normalize();
            var speed = movementSpeed * (Input.GetKey(KeyCode.LeftShift) ? 2.5f : 1f);
            transform.position += move * (speed * Time.unscaledDeltaTime);

            if (!Input.GetMouseButton(1))
            {
                if (Cursor.lockState == CursorLockMode.Locked) Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static float NormalizePitch(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
