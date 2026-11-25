using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// HMD 미연결(에디터) 테스트용 이동/시선 컨트롤러.
/// - 이동: 방향키 / WASD (Shift 2배)
/// - 시선: 마우스 우클릭 드래그 (Q/E 좌우 회전 보조)
///
/// 시선 회전 구조
/// - 좌우(Yaw): 리그 루트(OVRCameraRig)를 회전
/// - 상하(Pitch): TrackingSpace를 회전
///   CenterEyeAnchor는 OVRCameraRig가 매 프레임 트래킹 값으로 덮어쓰기 때문에
///   직접 회전시키면 우클릭을 뗄 때 정면으로 튀는 문제가 생김 → 부모(TrackingSpace)를 회전.
/// </summary>
[DisallowMultipleComponent]
public class DesktopVRFallbackMovement : MonoBehaviour
{
    [Header("Movement (Arrow Keys / WASD)")]
    [SerializeField] private float moveSpeed = 2.0f;
    [SerializeField] private float sprintMultiplier = 2.0f;

    [Header("Editor Look (Mouse Right-Drag / Q-E)")]
    [Tooltip("마우스 1픽셀당 회전 각도(도)")]
    [SerializeField] private float mouseSensitivity = 0.25f;
    [Tooltip("값이 클수록 즉각적, 작을수록 더 부드럽게 따라감")]
    [SerializeField] private float lookSmoothing = 18f;
    [SerializeField] private float keyTurnSpeed = 90f;
    [SerializeField] private float pitchLimit = 80f;
    [SerializeField] private float desktopFieldOfView = 50f;

    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform pitchPivot;

    private float _targetYaw;
    private float _targetPitch;
    private float _currentYaw;
    private float _currentPitch;
    private bool _initialized;

    private void Start()
    {
        var rig = GetComponentInChildren<OVRCameraRig>();
        if (headTransform == null)
        {
            if (rig != null && rig.centerEyeAnchor != null) headTransform = rig.centerEyeAnchor;
            else if (Camera.main != null) headTransform = Camera.main.transform;
        }
        if (pitchPivot == null)
        {
            if (rig != null && rig.trackingSpace != null) pitchPivot = rig.trackingSpace;
            else if (headTransform != null) pitchPivot = headTransform.parent;
        }

        InitializeLook();
    }

    private void InitializeLook()
    {
        if (_initialized) return;
        _initialized = true;

        // 씬에 배치된 현재 방향을 그대로 시작 방향으로 사용
        _targetYaw = _currentYaw = transform.eulerAngles.y;

        float startPitch = 0f;
        if (pitchPivot != null)
        {
            startPitch = pitchPivot.localEulerAngles.x;
            if (startPitch > 180f) startPitch -= 360f;
        }
        _targetPitch = _currentPitch = startPitch;
    }

    private void Update()
    {
        if (!IsHmdActive())
        {
            ReadLookInput();
            ApplyLook();
        }

        HandleKeyboardMove();
    }

    private void LateUpdate()
    {
        if (IsHmdActive()) return;

        // OVRManager가 에디터에서 FOV를 덮어쓰는 경우 방지
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

    private void ReadLookInput()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (kb != null)
        {
            if (kb.qKey.isPressed) _targetYaw -= keyTurnSpeed * Time.deltaTime;
            if (kb.eKey.isPressed) _targetYaw += keyTurnSpeed * Time.deltaTime;
        }

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            _targetYaw += delta.x * mouseSensitivity;
            _targetPitch = Mathf.Clamp(_targetPitch - delta.y * mouseSensitivity, -pitchLimit, pitchLimit);
        }
    }

    private void ApplyLook()
    {
        // 프레임레이트와 무관한 지수 보간 → 우클릭을 떼도 마지막 방향에서 부드럽게 멈춤
        float t = 1f - Mathf.Exp(-lookSmoothing * Time.deltaTime);
        _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, t);
        _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, t);

        transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        if (pitchPivot != null)
        {
            pitchPivot.localRotation = Quaternion.Euler(_currentPitch, 0f, 0f);
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

        // 이동은 좌우 바라보는 방향(수평) 기준
        Transform refTransform = headTransform != null ? headTransform : transform;
        Vector3 forward = refTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = transform.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 moveDir = (forward * z + right * x).normalized;

        float speed = moveSpeed;
        if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) speed *= sprintMultiplier;

        transform.position += moveDir * (speed * Time.deltaTime);
    }
}
