using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 현실과 동일한 물리 기반 국화 잡기·내려놓기(VR 컨트롤러 + 에디터 마우스/F키 지원).
/// - 잡을 때: 잡은 순간의 손-국화 상대 위치/각도를 그대로 유지해 점프 없이 자연스럽게 쥠.
/// - 놓을 때: 텔레포트 없이 실제 물리(Rigidbody 중력)로 놓은 바로 그 자리에 사뿐히 내려앉음.
/// - 헌화 완료: 손을 놓은 국화가 관 내부(FlowerTarget 영역)에 물리적으로 안착해 멈추면
///   내려놓은 바로 그 위치·각도 그대로 고정되며 CoffinRoot의 자식으로 편입됨.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FlowerInteractable : MonoBehaviour
{
    [Header("헌화 대상")]
    public bool interactionEnabled;
    public Collider flowerTargetZone;
    public Transform coffinRoot;

    [Header("VR 잡기 설정")]
    [Tooltip("컨트롤러 손이 국화를 집을 수 있는 반경 (m)")]
    public float vrGrabRadius = 0.28f;
    [Tooltip("손을 놓을 때 전달할 던지기/놓기 속도 비율 (0.35 = 사뿐히 내려놓기)")]
    [Range(0f, 1f)] public float releaseVelocityScale = 0.35f;

    [Header("에디터(PC) 집기 & 내려놓기 설정")]
    [Tooltip("에디터에서 국화를 집을 수 있는 최대 거리 (m)")]
    public float desktopReachDistance = 2.0f;
    [Tooltip("들었을 때 카메라 앞 기본 거리 (마우스 휠로 조절 가능)")]
    public float desktopHoldDistance = 0.48f;
    public float desktopMinHoldDist = 0.25f;
    public float desktopMaxHoldDist = 0.90f;
    public float desktopSmoothFollow = 14f;

    [Header("안착 판정 설정")]
    [Tooltip("관 영역 안에서 이 속도(m/s) 이하로 정지한 상태가 settleTime초 유지되면 헌화 완료")]
    public float settleSpeedThreshold = 0.06f;
    public float settleTime = 0.45f;

    public bool IsHeld => _holdingHand != null || _desktopHeld;
    public bool IsPlaced { get; private set; }

    public event Action OnFlowerPlaced;

    private Rigidbody _rb;
    private OVRCameraRig _rig;

    // VR grab state (preserves exact grab offset so object never jumps)
    private Transform _holdingHand;
    private bool _isLeftHand;
    private Vector3 _grabLocalPos;
    private Quaternion _grabLocalRot;
    private Vector3 _prevHandPos;
    private Vector3 _handVelocity;

    // Desktop grab state
    private bool _desktopHeld;
    private float _currentHoldDist;
    private Quaternion _desktopHoldRotOffset;

    // Settling state
    private bool _wasReleasedByUser;
    private bool _inTargetZone;
    private float _settleTimer;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _currentHoldDist = desktopHoldDistance;

        if (_rb != null)
        {
            _rb.mass = 0.08f;
            _rb.linearDamping = 2.2f;
            _rb.angularDamping = 3.5f;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
    }

    private void Start()
    {
        _rig = FindFirstObjectByType<OVRCameraRig>();
    }

    public void EnableInteraction(bool enable)
    {
        interactionEnabled = enable;
        if (enable)
        {
            Debug.Log("[FlowerInteractable] 헌화 활성화: VR 컨트롤러로 국화를 집어 관 안에 내려놓으세요. (에디터: 국화 좌클릭 또는 F키로 집기 -> 마우스 휠로 거리 조절 -> 놓기/F키)");
        }
    }

    private void Update()
    {
        if (!interactionEnabled || IsPlaced) return;

        if (_rig != null && !_desktopHeld)
        {
            HandleVRGrabAndRelease();
        }

#if UNITY_EDITOR
        if (_holdingHand == null)
        {
            HandleDesktopGrabAndRelease();
        }
#endif

        CheckPhysicalSettling();
    }

    private void HandleVRGrabAndRelease()
    {
        if (_holdingHand == null)
        {
            Transform rightCtrl = VRHandInput.GetHandTransform(_rig, false);
            Transform leftCtrl = VRHandInput.GetHandTransform(_rig, true);
            TryStartVRGrab(rightCtrl, false);
            if (_holdingHand == null) TryStartVRGrab(leftCtrl, true);
        }
        else
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            _handVelocity = (_holdingHand.position - _prevHandPos) / dt;
            _prevHandPos = _holdingHand.position;

            // 잡은 순간의 정확한 상대 위치/회전 유지 (점프 없음)
            transform.position = _holdingHand.TransformPoint(_grabLocalPos);
            transform.rotation = _holdingHand.rotation * _grabLocalRot;

            if (!VRHandInput.IsGrabbing(_isLeftHand, 0.30f))
            {
                ReleasePhysically(_handVelocity * releaseVelocityScale);
            }
        }
    }

    private void TryStartVRGrab(Transform hand, bool isLeft)
    {
        if (hand == null) return;
        if (!VRHandInput.IsGrabbing(isLeft, 0.40f)) return;
        if (Vector3.Distance(hand.position, transform.position) > vrGrabRadius) return;

        DetachFromParentCleanly();

        _holdingHand = hand;
        _isLeftHand = isLeft;
        _prevHandPos = hand.position;
        _handVelocity = Vector3.zero;
        _wasReleasedByUser = false;
        _settleTimer = 0f;

        // 현재 월드 포즈 그대로 손 로컬 오프셋 계산 -> 잡는 순간 툭 튀지 않음!
        _grabLocalPos = hand.InverseTransformPoint(transform.position);
        _grabLocalRot = Quaternion.Inverse(hand.rotation) * transform.rotation;

        SetPhysicsActive(false);
        VRHandInput.SendHaptic(isLeft, 0.45f, 0.08f);
        Debug.Log("[FlowerInteractable] VR 손으로 국화를 잡았습니다.");
    }

    private void HandleDesktopGrabAndRelease()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Mouse mouse = Mouse.current;
        Keyboard kb = Keyboard.current;

        if (!_desktopHeld)
        {
            bool wantGrab = false;

            // 1) 마우스 좌클릭으로 국화를 직접 클릭해 집기
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, desktopReachDistance))
                {
                    if (hit.transform == transform || hit.transform.IsChildOf(transform))
                    {
                        wantGrab = true;
                    }
                }
            }

            // 2) F키로 가까운 국화 집기
            if (kb != null && kb.fKey.wasPressedThisFrame)
            {
                if (Vector3.Distance(cam.transform.position, transform.position) <= desktopReachDistance)
                {
                    wantGrab = true;
                }
                else
                {
                    Debug.Log("[FlowerInteractable] 국화에 조금 더 가까이 다가가서 집어주세요.");
                }
            }

            if (wantGrab)
            {
                StartDesktopGrab(cam);
            }
        }
        else
        {
            // 마우스 휠로 팔 뻗는 거리 조절 (관 위 원하는 깊이에 정확히 맞추기)
            if (mouse != null)
            {
                float scrollY = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scrollY) > 0.01f)
                {
                    _currentHoldDist = Mathf.Clamp(_currentHoldDist + scrollY * 0.001f, desktopMinHoldDist, desktopMaxHoldDist);
                }
            }

            // 부드럽게 손 앞 목표 위치로 이동 (순간이동 점프 없음)
            Vector3 targetPos = cam.transform.position
                + cam.transform.forward * _currentHoldDist
                + cam.transform.right * 0.10f
                - cam.transform.up * 0.12f;
            Quaternion targetRot = cam.transform.rotation * _desktopHoldRotOffset;

            float t = 1f - Mathf.Exp(-desktopSmoothFollow * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);

            // 마우스 좌클릭을 떼거나 F키를 다시 누르면 -> 바로 그 자리에서 실제 물리로 내려놓기!
            bool releaseByMouse = mouse != null && mouse.leftButton.wasReleasedThisFrame;
            bool releaseByKey = kb != null && kb.fKey.wasPressedThisFrame;
            if (releaseByMouse || releaseByKey)
            {
                ReleasePhysically(Vector3.zero);
            }
        }
    }

    private void StartDesktopGrab(Camera cam)
    {
        DetachFromParentCleanly();
        _desktopHeld = true;
        _wasReleasedByUser = false;
        _settleTimer = 0f;
        _currentHoldDist = Mathf.Clamp(Vector3.Distance(cam.transform.position, transform.position), desktopMinHoldDist, desktopMaxHoldDist);
        _desktopHoldRotOffset = Quaternion.Euler(70f, -15f, 0f);

        SetPhysicsActive(false);
        Debug.Log("[FlowerInteractable] 국화를 들었습니다. 관 위 원하는 위치로 옮겨 마우스 버튼을 떼거나 F키를 눌러 내려놓으세요. (마우스 휠: 거리 조절)");
    }

    public void DesktopToggleGrabOrPlace()
    {
        // StageIntroSequence 호환용 (실제 입력은 Update의 HandleDesktopGrabAndRelease에서 처리)
    }

    public void ResetFlower()
    {
        IsPlaced = false;
        _holdingHand = null;
        _desktopHeld = false;
        _wasReleasedByUser = false;
        _inTargetZone = false;
        _settleTimer = 0f;
        SetPhysicsActive(false);
    }

    public void CompletePlacement()
    {
        FinalizePlacementInPlace();
    }

    private void ReleasePhysically(Vector3 initialVelocity)
    {
        _holdingHand = null;
        _desktopHeld = false;
        _wasReleasedByUser = true;
        _settleTimer = 0f;

        // 텔레포트 없이 현재 위치 그대로 중력 + 충돌 물리로 내려앉도록 전환!
        SetPhysicsActive(true);
        if (_rb != null)
        {
            _rb.linearVelocity = initialVelocity;
            _rb.angularVelocity = Vector3.zero;
        }
        Debug.Log("[FlowerInteractable] 국화를 놓았습니다. 물리적으로 내려앉는 중...");
    }

    private void CheckPhysicalSettling()
    {
        if (!_wasReleasedByUser || IsHeld || IsPlaced || _rb == null) return;

        bool insideZone = _inTargetZone || IsPositionInsideTargetBounds();
        if (!insideZone)
        {
            _settleTimer = 0f;
            return;
        }

        if (_rb.linearVelocity.magnitude <= settleSpeedThreshold)
        {
            _settleTimer += Time.deltaTime;
            if (_settleTimer >= settleTime)
            {
                FinalizePlacementInPlace();
            }
        }
        else
        {
            _settleTimer = 0f;
        }
    }

    private bool IsPositionInsideTargetBounds()
    {
        if (flowerTargetZone == null) return false;
        return flowerTargetZone.bounds.Contains(transform.position);
    }

    /// <summary>
    /// 텔레포트하지 않고, 사용자가 실제로 내려놓아 안착한 바로 그 위치·각도 그대로 고정 및 CoffinRoot 편입!
    /// </summary>
    private void FinalizePlacementInPlace()
    {
        if (IsPlaced) return;
        IsPlaced = true;
        _holdingHand = null;
        _desktopHeld = false;

        SetPhysicsActive(false);

        if (coffinRoot != null)
        {
            transform.SetParent(coffinRoot, true);
        }

        Debug.Log("[FlowerInteractable] 국화가 관 안 " + transform.position + " 위치에 자연스럽게 헌화되었습니다!");
        OnFlowerPlaced?.Invoke();
    }

    private void SetPhysicsActive(bool active)
    {
        if (_rb == null) return;
        if (!active)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        _rb.isKinematic = !active;
        _rb.useGravity = active;
    }

    private void DetachFromParentCleanly()
    {
        if (transform.parent == null) return;
        Vector3 wp = transform.position;
        Quaternion wr = transform.rotation;
        Vector3 ws = transform.lossyScale;
        transform.SetParent(null, false);
        transform.position = wp;
        transform.rotation = wr;
        transform.localScale = ws;
    }

    private bool IsHandGripping(bool isLeft, float threshold)
    {
        if (isLeft)
        {
            return OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger) > threshold
                || OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger) > threshold;
        }
        return OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger) > threshold
            || OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger) > threshold;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (flowerTargetZone != null && other == flowerTargetZone)
        {
            _inTargetZone = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (flowerTargetZone != null && other == flowerTargetZone)
        {
            _inTargetZone = false;
            _settleTimer = 0f;
        }
    }
}
