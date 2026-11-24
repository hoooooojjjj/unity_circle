using System;
using UnityEngine;

/// <summary>
/// 국화 한 송이(Chrysanthemum)의 잡기/놓기 및 헌화 완료 판정(FlowerTarget 영역 진입 + 손에서 놓음).
/// 실제 Quest 컨트롤러(Meta XR)와 에디터 테스트(F 키)를 모두 지원합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FlowerInteractable : MonoBehaviour
{
    public bool interactionEnabled;
    public Transform flowerTarget;
    public Transform coffinRoot;
    public float grabRadius = 0.22f;

    public bool IsHeld => _holdingHand != null || _desktopHeld;
    public bool IsPlaced { get; private set; }

    public event Action OnFlowerPlaced;

    private Rigidbody _rb;
    private OVRCameraRig _rig;
    private Transform _holdingHand;
    private bool _isLeftHand;
    private bool _desktopHeld;
    private bool _inTargetZone;
    private Vector3 _initialPos;
    private Quaternion _initialRot;
    private Transform _initialParent;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _initialPos = transform.position;
        _initialRot = transform.rotation;
        _initialParent = transform.parent;
    }

    private void Start()
    {
        _rig = FindFirstObjectByType<OVRCameraRig>();
    }

    public void ResetFlower()
    {
        IsPlaced = false;
        _holdingHand = null;
        _desktopHeld = false;
        _inTargetZone = false;
        transform.SetParent(_initialParent, true);
        transform.position = _initialPos;
        transform.rotation = _initialRot;
        if (_rb != null)
        {
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
                transform.position = _holdingHand.position;
                transform.rotation = _holdingHand.rotation;

                bool stillHolding = _isLeftHand
                    ? OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > 0.4f || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > 0.4f
                    : OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.4f || OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.4f;

                if (!stillHolding)
                {
                    ReleaseFlower();
                }
            }
        }
        else if (_desktopHeld && Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            transform.position = cam.position + cam.forward * 0.55f + cam.right * 0.15f - cam.up * 0.15f;
            transform.rotation = cam.rotation * Quaternion.Euler(15f, -20f, 0f);
        }

        CheckTargetProximity();
    }

    private void TryBeginVRGrab(Transform hand, bool isLeft)
    {
        if (hand == null) return;
        if (Vector3.Distance(hand.position, transform.position) > grabRadius) return;

        bool grip = isLeft
            ? OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > 0.5f || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > 0.5f
            : OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.5f || OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.5f;

        if (grip)
        {
            _holdingHand = hand;
            _isLeftHand = isLeft;
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
    }

    public void DesktopToggleGrabOrPlace()
    {
        if (!interactionEnabled || IsPlaced) return;

        if (!_desktopHeld)
        {
            _desktopHeld = true;
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        else
        {
            _desktopHeld = false;
            // 에디터 테스트에서는 F 두 번째 입력 시 헌화 위치에 내려놓기
            CompletePlacement();
        }
    }

    private void ReleaseFlower()
    {
        _holdingHand = null;
        _desktopHeld = false;

        CheckTargetProximity();
        if (_inTargetZone)
        {
            CompletePlacement();
        }
        else
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }
    }

    private void CheckTargetProximity()
    {
        if (flowerTarget == null) return;
        if (Vector3.Distance(transform.position, flowerTarget.position) <= 0.45f)
        {
            _inTargetZone = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (flowerTarget != null && other.transform == flowerTarget)
        {
            _inTargetZone = true;
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
            _inTargetZone = false;
        }
    }

    public void CompletePlacement()
    {
        if (IsPlaced) return;
        IsPlaced = true;
        _holdingHand = null;
        _desktopHeld = false;

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
            transform.rotation = flowerTarget.rotation;
        }

        if (coffinRoot != null)
        {
            transform.SetParent(coffinRoot, true);
        }

        OnFlowerPlaced?.Invoke();
    }
}
