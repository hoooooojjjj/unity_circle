using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class DesktopVRFallbackMovement : MonoBehaviour
{
    [Header("Movement (Arrow Keys / WASD)")]
    [SerializeField] private float moveSpeed = 2.0f;
    [SerializeField] private float sprintMultiplier = 2.0f;
    [SerializeField] private float fallbackEyeHeight = 0.65f;

    [Header("Editor Look (Mouse Right-Drag / Q-E)")]
    [SerializeField] private float mouseSensitivity = 0.18f;
    [SerializeField] private float keyTurnSpeed = 60f;
    [SerializeField] private float desktopFieldOfView = 35f;
    [SerializeField] private float initialDesktopPitch = 0f;
    [SerializeField] private Transform headTransform;

    private float _pitch;
    private float _yaw;
    private bool _initializedDesktopPose;

    private void Start()
    {
        if (headTransform == null)
        {
            var rig = GetComponentInChildren<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                headTransform = rig.centerEyeAnchor;
            }
            else if (Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }
        }

        if (!IsHmdActive())
        {
            InitializeDesktopHeight();
        }
    }

    private void Update()
    {
        bool hmdActive = IsHmdActive();

        if (!hmdActive)
        {
            HandleDesktopLook();
        }

        HandleKeyboardMove();
    }

    private void LateUpdate()
    {
        if (IsHmdActive()) return;

        // OVRManager가 에디터에서 FOV를 덮어쓰는 경우를 방지
        if (headTransform != null && headTransform.TryGetComponent<Camera>(out var cam))
        {
            if (!Mathf.Approximately(cam.fieldOfView, desktopFieldOfView))
            {
                cam.fieldOfView = desktopFieldOfView;
            }
        }
    }

    private bool IsHmdActive()
    {
        return OVRManager.isHmdPresent || XRSettings.isDeviceActive;
    }

    private void InitializeDesktopHeight()
    {
        if (_initializedDesktopPose) return;
        _initializedDesktopPose = true;

        Vector3 pos = transform.position;
        pos.y = fallbackEyeHeight;
        transform.position = pos;

        _yaw = transform.eulerAngles.y;
        _pitch = initialDesktopPitch;

        if (headTransform != null)
        {
            headTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            if (headTransform.TryGetComponent<Camera>(out var cam))
            {
                cam.fieldOfView = desktopFieldOfView;
            }
        }
    }

    private void HandleKeyboardMove()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        float x = 0f;
        float z = 0f;

        if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) x -= 1f;
        if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) x += 1f;
        if (kb.upArrowKey.isPressed || kb.wKey.isPressed) z += 1f;
        if (kb.downArrowKey.isPressed || kb.sKey.isPressed) z -= 1f;

        if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0f)) return;

        Transform refTransform = headTransform != null ? headTransform : transform;
        Vector3 forward = refTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = transform.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 moveDir = (forward * z + right * x).normalized;

        float speed = moveSpeed;
        if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
        {
            speed *= sprintMultiplier;
        }

        transform.position += moveDir * (speed * Time.deltaTime);
    }

    private void HandleDesktopLook()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (kb != null)
        {
            float yawDelta = 0f;
            if (kb.qKey.isPressed) yawDelta -= keyTurnSpeed * Time.deltaTime;
            if (kb.eKey.isPressed) yawDelta += keyTurnSpeed * Time.deltaTime;

            if (!Mathf.Approximately(yawDelta, 0f))
            {
                _yaw += yawDelta;
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
        }

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * mouseSensitivity, -80f, 80f);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (headTransform != null)
            {
                headTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }
    }
}
