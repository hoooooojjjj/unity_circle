using System;
using UnityEngine;

/// <summary>
/// 국화 한 송이(Chrysanthemum)의 VR 잡기/놓기 및 관 위 헌화 완료 판정.
/// 조건: (1) FlowerTarget 영역 진입 또는 근접 + (2) 손에서 놓음 -> 관 위에 안착 후 CoffinRoot 자식으로 편입.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FlowerInteractable : MonoBehaviour
{
    [Header("헌화 대상")]
    public bool interactionEnabled;
    public Transform flowerTarget;
    public Transform coffinRoot;

    [Header("VR 잡기 & 내려놓기 설정")]
    [Tooltip("컨트롤러 손이 국화를 잡을 수 있는 반경 (m)")]
    public float grabRadius = 0.28f;
    [Tooltip("FlowerTarget 중심으로부터 이 거리(m) 안에서 손을 놓으면 관에 헌화 완료로 판정")]
    public float dropSnapRadius = 0.48f;
    [Tooltip("관 밖에서 실수로 놓았을 때 받침대 원래 위치로 복귀 (분실 방지)")]
    public bool returnToStandIfDroppedOutside = true;
    [Tooltip("손에 쥐었을 때 국화의 로컬 오프셋/회전")]
    public Vector3 handHoldPosOffset = new Vector3(0f, 0f, 0.06f);
    public Vector3 handHoldRotOffset = new Vector3(70f, 0f, 0f);

    public bool IsHeld => _holdingHand != null || _desktopHeld;
    public bool IsPlaced { get; private set; }

    public event Action OnFlowerPlaced;

    private Rigidbody _rb;
    private OVRCameraRig _rig;
    private Transform _holdingHand;
    private bool _isLeftHand;
    private bool _desktopHeld;
    private bool _inTargetTrigger;

    private Vector3 _initialWorldPos;
    private Quaternion _initialWorldRot;
    private Vector3 _initialLossyScale;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _initialWorldPos = transform.position;
        _initialWorldRot = transform.rotation;
        _initialLossyScale = transform.lossyScale;

        if (_rb != null)
        {
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
            Debug.Log("[FlowerInteractable] 국화 헌화 인터랙션 활성화: VR 컨트롤러로 Chrysanthemum을 들고 관 위에 내려놓으세요. (에디터: F키 잡기 -> F키 헌화)");
        }
    }

    public void ResetFlower()
    {
        IsPlaced = false;
        _holdingHand = null;
        _desktopHeld = false;
        _inTargetTrigger = false;

        transform.SetParent(null, true);
        transform.position = _initialWorldPos;
        transform.rotation = _initialWorldRot;
        transform.localScale = _initialLossyScale;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
    }

    private void Update()
    {
        if (!interactionEnabled || IsPlaced) return;

        if (_rig != null && !_desktopHeld)
        {
            if (_holdingHand == null)
            {
                TryBeginVRGrab(_rig.rightHandAnchor, false);
                if (_holdingHand == null) TryBeginVRGrab(_rig.leftHandAnchor, true);
            }
            else
            {
                transform.position = _holdingHand.TransformPoint(handHoldPosOffset);
                transform.rotation = _holdingHand.rotation * Quaternion.Euler(handHoldRotOffset);

                bool stillHolding = IsHandGripping(_isLeftHand, 0.35f);
                if (!stillHolding)
                {
                    ReleaseFlower();
                }
            }
        }
        else if (_desktopHeld && Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            transform.position = cam.position + cam.forward * 0.45f + cam.right * 0.14f - cam.up * 0.14f;
            transform.rotation = cam.rotation * Quaternion.Euler(65f, -15f, 0f);
        }
    }

    private void TryBeginVRGrab(Transform hand, bool isLeft)
    {
        if (hand == null) return;
        if (!IsHandGripping(isLeft, 0.5f)) return;
        if (Vector3.Distance(hand.position, transform.position) > grabRadius) return;

        // 비균일 스케일 부모(FlowerStand 등)에서 분리하여 회전 시 찌그러짐 방지
        DetachKeepingWorldTransform();

        _holdingHand = hand;
        _isLeftHand = isLeft;
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        Debug.Log("[FlowerInteractable] VR 손으로 국화를 들었습니다.");
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

    public void DesktopToggleGrabOrPlace()
    {
        if (!interactionEnabled || IsPlaced) return;

        if (!_desktopHeld)
        {
            DetachKeepingWorldTransform();
            _desktopHeld = true;
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
            Debug.Log("[FlowerInteractable] (에디터) 국화를 들었습니다. 관 앞에서 F키를 한 번 더 누르면 내려놓습니다.");
        }
        else
        {
            _desktopHeld = false;
            CompletePlacement();
        }
    }

    private void DetachKeepingWorldTransform()
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

    private void ReleaseFlower()
    {
        _holdingHand = null;
        _desktopHeld = false;

        bool nearTarget = _inTargetTrigger || IsWithinDropSnapZone();
        if (nearTarget)
        {
            CompletePlacement();
        }
        else if (returnToStandIfDroppedOutside)
        {
            ResetFlower();
            Debug.Log("[FlowerInteractable] 관 영역 밖에서 놓아 받침대로 복귀했습니다.");
        }
        else if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }
    }

    private bool IsWithinDropSnapZone()
    {
        if (flowerTarget == null) return false;
        return Vector3.Distance(transform.position, flowerTarget.position) <= dropSnapRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (flowerTarget != null && other.transform == flowerTarget)
        {
            _inTargetTrigger = true;
            if (!IsHeld && interactionEnabled && !IsPlaced)
            {
                CompletePlacement();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (flowerTarget != null && other.transform == flowerTarget)
        {
            _inTargetTrigger = false;
        }
    }

    public void CompletePlacement()
    {
        if (IsPlaced) return;
        IsPlaced = true;
        _holdingHand = null;
        _desktopHeld = false;

        DetachKeepingWorldTransform();

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }

        if (flowerTarget != null)
        {
            transform.position = flowerTarget.position;
            // 관 위에 자연스럽게 눕혀진 각도로 안착
            transform.rotation = flowerTarget.rotation * Quaternion.Euler(0f, 0f, 88f);
        }

        if (coffinRoot != null)
        {
            transform.SetParent(coffinRoot, true);
        }

        Debug.Log("[FlowerInteractable] 국화가 관 위에 헌화되었습니다.");
        OnFlowerPlaced?.Invoke();
    }
}
