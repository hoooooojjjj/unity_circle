using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CurtainFront의 우측 끝을 잡아 좌측(-X)으로 슬라이드하면
/// 좌측 끝을 축으로 천이 접히며 걷어지고 통로가 개방되는 VR + 에디터 겸용 인터랙션.
/// </summary>
public class CurtainSlideInteractable : MonoBehaviour
{
    [Header("대상 커튼 & 통로 차단")]
    [Tooltip("걷어질 커튼 Transform (비우면 이 컴포넌트가 붙은 오브젝트)")]
    public Transform curtainTransform;
    [Tooltip("닫혀 있을 때 통과를 막는 콜라이더 (선택, 걷어지면 자동 비활성화)")]
    public Collider passageBlocker;

    [Header("걷어짐 설정")]
    [Tooltip("완전히 걷어졌을 때 남는 가로 비율 (0.12 = 12% 폭으로 좌측에 모임)")]
    [Range(0.08f, 0.35f)] public float minFoldedWidthRatio = 0.14f;
    [Tooltip("이 비율 이상 좌측으로 슬라이드하고 놓으면 끝까지 자동으로 걷힘")]
    [Range(0.15f, 0.7f)] public float autoOpenThreshold = 0.28f;
    public float autoSlideSpeed = 1.6f;

    [Header("VR 잡기 설정")]
    [Tooltip("커튼 우측 끝에서 손이 닿아야 하는 반경 (m)")]
    public float grabRadius = 0.32f;
    [Tooltip("커튼 우측 잡기 가능 높이 범위 (m)")]
    public float minGrabHeight = 0.3f;
    public float maxGrabHeight = 1.9f;

    [Header("에디터 테스트 (좌클릭 드래그 / E키)")]
    public float desktopDragSensitivity = 0.0025f;

    public bool InteractionEnabled { get; private set; }
    public bool IsFullyOpened { get; private set; }
    public float OpenProgress => _openProgress;

    public event Action OnCurtainOpened;

    private OVRCameraRig _rig;
    private Vector3 _closedPos;
    private Vector3 _closedScale;
    private float _closedWorldWidth = 1.1f;
    private float _leftEdgeX;
    private float _openProgress;

    private Transform _grabbingHand;
    private bool _isLeftHand;
    private float _grabStartHandX;
    private float _grabStartProgress;

    private bool _desktopDragging;
    private bool _autoOpening;
    private bool _autoClosing;

    private void Awake()
    {
        if (curtainTransform == null) curtainTransform = transform;

        _closedPos = curtainTransform.position;
        _closedScale = curtainTransform.localScale;

        Renderer r = curtainTransform.GetComponent<Renderer>();
        if (r != null && r.bounds.size.x > 0.05f)
        {
            _closedWorldWidth = r.bounds.size.x;
        }
        _leftEdgeX = _closedPos.x - _closedWorldWidth * 0.5f;
    }

    private void Start()
    {
        _rig = FindFirstObjectByType<OVRCameraRig>();
        SetPassageBlocked(true);
    }

    public void EnableInteraction(bool enable)
    {
        InteractionEnabled = enable;
        if (enable)
        {
            Debug.Log("[CurtainSlide] 커튼 인터랙션 활성화: VR에서 CurtainFront 우측 끝을 잡아 좌측으로 슬라이드하세요. (에디터: 좌클릭 왼쪽 드래그 또는 E키)");
        }
    }

    /// <summary>현재 커튼 우측 끝의 월드 X 좌표</summary>
    public float CurrentRightEdgeX
    {
        get
        {
            float widthFactor = Mathf.Lerp(1f, minFoldedWidthRatio, _openProgress);
            return _leftEdgeX + _closedWorldWidth * widthFactor;
        }
    }

    private void Update()
    {
        if (!InteractionEnabled || IsFullyOpened) return;

        if (_autoOpening)
        {
            _openProgress = Mathf.MoveTowards(_openProgress, 1f, autoSlideSpeed * Time.deltaTime);
            ApplyCurtainFold(_openProgress);
            if (_openProgress >= 0.999f)
            {
                FinishOpening();
            }
            return;
        }

        if (_autoClosing)
        {
            _openProgress = Mathf.MoveTowards(_openProgress, 0f, autoSlideSpeed * Time.deltaTime);
            ApplyCurtainFold(_openProgress);
            if (_openProgress <= 0.001f)
            {
                _openProgress = 0f;
                _autoClosing = false;
            }
        }

        HandleVRSlide();
        HandleDesktopTest();
    }

    private void HandleVRSlide()
    {
        if (_rig == null) return;

        if (_grabbingHand == null)
        {
            TryStartVRGrab(_rig.rightHandAnchor, false);
            if (_grabbingHand == null) TryStartVRGrab(_rig.leftHandAnchor, true);
        }
        else
        {
            bool stillHolding = IsHandGripping(_isLeftHand, 0.35f);
            if (!stillHolding)
            {
                _grabbingHand = null;
                if (_openProgress >= autoOpenThreshold)
                {
                    _autoOpening = true;
                }
                else
                {
                    _autoClosing = true;
                }
                return;
            }

            // 우측에서 좌측(-X)으로 이동한 거리만큼 openProgress 증가
            float movedLeft = _grabStartHandX - _grabbingHand.position.x;
            float maxSlideDist = _closedWorldWidth * (1f - minFoldedWidthRatio);
            _openProgress = Mathf.Clamp01(_grabStartProgress + movedLeft / Mathf.Max(0.1f, maxSlideDist));
            _autoClosing = false;
            ApplyCurtainFold(_openProgress);

            if (_openProgress >= 0.92f)
            {
                _grabbingHand = null;
                _autoOpening = true;
            }
        }
    }

    private void TryStartVRGrab(Transform hand, bool isLeft)
    {
        if (hand == null) return;
        if (!IsHandGripping(isLeft, 0.5f)) return;

        Vector3 hp = hand.position;
        if (hp.y < minGrabHeight || hp.y > maxGrabHeight) return;

        // 커튼의 현재 우측 끝(X, Z) 근처인지 판정
        float dx = hp.x - CurrentRightEdgeX;
        float dz = hp.z - _closedPos.z;
        float distXZ = Mathf.Sqrt(dx * dx + dz * dz);

        if (distXZ <= grabRadius)
        {
            _grabbingHand = hand;
            _isLeftHand = isLeft;
            _grabStartHandX = hp.x;
            _grabStartProgress = _openProgress;
            _autoClosing = false;
        }
    }

    private bool IsHandGripping(bool isLeft, float threshold)
    {
        if (isLeft)
        {
            return OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > threshold
                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > threshold;
        }
        return OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > threshold
            || OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > threshold;
    }

    private void HandleDesktopTest()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            _autoClosing = false;
            _autoOpening = true;
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            _desktopDragging = true;
            _autoClosing = false;
        }
        else if (mouse.leftButton.wasReleasedThisFrame && _desktopDragging)
        {
            _desktopDragging = false;
            if (_openProgress >= autoOpenThreshold)
            {
                _autoOpening = true;
            }
            else
            {
                _autoClosing = true;
            }
        }

        if (_desktopDragging)
        {
            float dx = mouse.delta.ReadValue().x;
            // 왼쪽으로 드래그(dx < 0)하면 열림 증가
            if (Mathf.Abs(dx) > 0.01f)
            {
                _openProgress = Mathf.Clamp01(_openProgress - dx * desktopDragSensitivity);
                ApplyCurtainFold(_openProgress);
                if (_openProgress >= 0.92f)
                {
                    _desktopDragging = false;
                    _autoOpening = true;
                }
            }
        }
    }

    private void ApplyCurtainFold(float progress)
    {
        float widthFactor = Mathf.Lerp(1f, minFoldedWidthRatio, progress);

        Vector3 sc = _closedScale;
        sc.x = _closedScale.x * widthFactor;
        curtainTransform.localScale = sc;

        Vector3 pos = _closedPos;
        pos.x = _leftEdgeX + (_closedWorldWidth * widthFactor) * 0.5f;
        curtainTransform.position = pos;
    }

    private void FinishOpening()
    {
        _openProgress = 1f;
        _autoOpening = false;
        IsFullyOpened = true;
        ApplyCurtainFold(1f);
        SetPassageBlocked(false);

        Debug.Log("[CurtainSlide] CurtainFront가 좌측으로 완전히 걷어졌습니다. 이제 안쪽으로 들어갈 수 있습니다.");
        OnCurtainOpened?.Invoke();
    }

    private void SetPassageBlocked(bool blocked)
    {
        if (passageBlocker != null)
        {
            passageBlocker.enabled = blocked;
        }
    }
}
