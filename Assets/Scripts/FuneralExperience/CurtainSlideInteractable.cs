using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CurtainClothPhysics(Rail-Pinned XPBD)를 구동하는 완전 자유 물리 VR + 에디터 커튼 인터랙션.
/// - 양손(또는 한 손)으로 커튼의 어느 부위든 3D로 쥐고 당겨 젖힐 수 있습니다.
/// - 자동 열림/닫힘 없이 현실 커튼처럼 레일 마찰에 의해 밀어 둔 위치에 그대로 머뭅니다.
/// - 잡지 않은 상태에서도 손과 몸(어깨)이 닿으면 천이 물리적으로 일렁이며 비켜납니다.
/// - 우측 끝 상단 링이 openThreshold 이상 열리면 통로가 개방되고 OnCurtainOpened가 발생합니다.
/// </summary>
public class CurtainSlideInteractable : MonoBehaviour
{
    [Header("대상 커튼 & 통로 차단")]
    [Tooltip("걷어질 커튼 Transform (비우면 이 컴포넌트가 붙은 오브젝트)")]
    public Transform curtainTransform;
    [Tooltip("닫혀 있을 때 통과를 막는 콜라이더 (열림 기준 충족 시 자동 비활성화)")]
    public Collider passageBlocker;
    [Tooltip("실시간 천 물리 시뮬레이터 (비우면 자동 탐색/추가)")]
    public CurtainClothPhysics clothPhysics;

    [Header("완전 자유 물리 개방 기준")]
    [Tooltip("우측 끝 상단 링이 전체 폭의 이 비율 이상 왼쪽으로 열리면 통과 가능(OnCurtainOpened) 판정")]
    [Range(0.20f, 0.80f)] public float openThreshold = 0.42f;

    [Header("VR 양손 잡기 설정")]
    [Tooltip("컨트롤러가 커튼 천을 잡을 수 있는 최대 거리 (m)")]
    public float grabRadius = 0.28f;

    public bool InteractionEnabled { get; private set; }
    public bool IsFullyOpened { get; private set; }
    public float OpenProgress => clothPhysics != null ? clothPhysics.OpenProgress : 0f;
    public float CurrentRightEdgeX => clothPhysics != null ? clothPhysics.CurrentRightEdgeX : transform.position.x;

    public event Action OnCurtainOpened;

    private OVRCameraRig _rig;

    // Editor 3D mouse pinch-drag state
    private bool _desktopDragging;
    private float _desktopDragDepth;
    private Camera _desktopCam;

    private void Awake()
    {
        if (curtainTransform == null) curtainTransform = transform;
        if (clothPhysics == null) clothPhysics = curtainTransform.GetComponent<CurtainClothPhysics>();
        if (clothPhysics == null) clothPhysics = curtainTransform.gameObject.AddComponent<CurtainClothPhysics>();
    }

    private void Start()
    {
        _rig = FindFirstObjectByType<OVRCameraRig>();
        SetPassageBlocked(true);
        if (clothPhysics != null)
        {
            clothPhysics.SetSimulationActive(false);
        }
    }

    public void EnableInteraction(bool enable)
    {
        InteractionEnabled = enable;
        if (clothPhysics != null)
        {
            clothPhysics.SetSimulationActive(enable);
        }

        if (enable)
        {
            Debug.Log("[CurtainSlide] 완전 자유 물리 커튼 활성화: VR에서 양손으로 천을 잡고 3D로 자유롭게 젖혀보세요. (에디터: 천 좌클릭 3D 드래그 또는 E키)");
        }
        else if (clothPhysics != null)
        {
            clothPhysics.EndGrab(0);
            clothPhysics.EndGrab(1);
            clothPhysics.EndGrab(2);
            _desktopDragging = false;
        }
    }

    private void Update()
    {
        if (!InteractionEnabled || clothPhysics == null) return;

        UpdateVRHandsAndBody();

#if UNITY_EDITOR
        HandleDesktop3DDrag();
#endif

        if (!IsFullyOpened && clothPhysics.OpenProgress >= openThreshold)
        {
            MarkCurtainOpened();
        }
    }

    private void UpdateVRHandsAndBody()
    {
        if (_rig == null) return;

        Transform leftHand = VRHandInput.GetHandTransform(_rig, true);
        Transform rightHand = VRHandInput.GetHandTransform(_rig, false);
        Transform head = _rig.centerEyeAnchor != null ? _rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);

        // 1. 왼손(슬롯 0) & 오른손(슬롯 1) 독립 3D 핀치 잡기 (양손 동시 잡기 지원!)
        ProcessVRHandSlot(0, leftHand, isLeft: true);
        ProcessVRHandSlot(1, rightHand, isLeft: false);

        // 2. 잡지 않은 손과 사용자 몸통/어깨 충돌체 전달 (스침 현존감 — 실제 트래킹 중인 컨트롤러만 충돌 활성화)
        bool hasLeft = leftHand != null && leftHand.localPosition.sqrMagnitude > 1e-5f;
        bool hasRight = rightHand != null && rightHand.localPosition.sqrMagnitude > 1e-5f;
        bool hasBody = head != null;
        clothPhysics.SetExternalColliders(
            hasLeft, hasLeft ? leftHand.position : Vector3.zero,
            hasRight, hasRight ? rightHand.position : Vector3.zero,
            hasBody, hasBody ? head.position : Vector3.zero
        );
    }

    private void ProcessVRHandSlot(int slotIndex, Transform hand, bool isLeft)
    {
        if (hand == null)
        {
            if (clothPhysics.IsSlotGrabbing(slotIndex)) clothPhysics.EndGrab(slotIndex);
            return;
        }

        if (!clothPhysics.IsSlotGrabbing(slotIndex))
        {
            if (VRHandInput.IsGrabbing(isLeft, 0.40f))
            {
                if (clothPhysics.TryBeginGrab(slotIndex, hand.position, grabRadius))
                {
                    VRHandInput.SendHaptic(isLeft, 0.42f, 0.06f);
                }
            }
        }
        else
        {
            if (!VRHandInput.IsGrabbing(isLeft, 0.28f))
            {
                clothPhysics.EndGrab(slotIndex);
                return;
            }

            float railSlideTick = clothPhysics.UpdateGrab(slotIndex, hand.position);
            if (railSlideTick > 0f)
            {
                // 커튼링이 봉 위를 미끄러질 때 미세한 촉각 진동 피드백
                VRHandInput.SendHaptic(isLeft, 0.18f, 0.025f);
            }
        }
    }

    private void HandleDesktop3DDrag()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            clothPhysics.StartEditorPhysicalAutoOpen();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        if (_desktopCam == null) _desktopCam = Camera.main;
        if (_desktopCam == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Ray ray = _desktopCam.ScreenPointToRay(mouse.position.ReadValue());
            if (clothPhysics.FindClosestVertexToRay(ray, 0.22f, out int hitIdx, out Vector3 hitPt))
            {
                _desktopDragging = true;
                _desktopDragDepth = Vector3.Distance(_desktopCam.transform.position, hitPt);
                clothPhysics.BeginGrabAtVertex(2, hitIdx, hitPt);
            }
        }
        else if (mouse.leftButton.wasReleasedThisFrame && _desktopDragging)
        {
            _desktopDragging = false;
            clothPhysics.EndGrab(2);
        }

        if (_desktopDragging)
        {
            // 마우스 휠로 천을 앞뒤로 당기거나 밀 수 있음
            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                _desktopDragDepth = Mathf.Clamp(_desktopDragDepth + scrollY * 0.001f, 0.35f, 3.0f);
            }

            Ray ray = _desktopCam.ScreenPointToRay(mouse.position.ReadValue());
            Vector3 target3D = ray.GetPoint(_desktopDragDepth);
            clothPhysics.UpdateGrab(2, target3D);
        }
    }

    private void MarkCurtainOpened()
    {
        if (IsFullyOpened) return;
        IsFullyOpened = true;
        SetPassageBlocked(false);
        Debug.Log("[CurtainSlide] 물리 커튼이 충분히 열렸습니다(OpenProgress=" + clothPhysics.OpenProgress.ToString("F2") + "). 이제 안쪽으로 들어갈 수 있으며, 천은 계속 물리적으로 반응합니다.");
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
