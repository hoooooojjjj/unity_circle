using System;
using UnityEngine;

/// <summary>
/// 커튼 끝 잡기 영역. VR 컨트롤러로 잡고 좌우 15~25cm(pullThreshold) 당기면 커튼 열림 이벤트를 발생시킵니다.
/// 에디터에서는 ExperienceController의 단축키(E)로도 트리거할 수 있습니다.
/// </summary>
public class CurtainPullInteractable : MonoBehaviour
{
    public bool interactionEnabled;
    public float grabRadius = 0.25f;
    [Tooltip("잡기 시작 위치에서 좌우로 이 거리(m) 이상 당기면 커튼 열림 실행 (0.15~0.25m 권장)")]
    public float pullThreshold = 0.20f;

    public event Action OnCurtainPulled;

    private OVRCameraRig _rig;
    private Transform _grabbingHand;
    private bool _isLeftHand;
    private Vector3 _grabStartPos;
    private bool _triggered;

    private void Start()
    {
        _rig = FindFirstObjectByType<OVRCameraRig>();
    }

    public void ResetInteractable()
    {
        _triggered = false;
        _grabbingHand = null;
    }

    private void Update()
    {
        if (!interactionEnabled || _triggered || _rig == null) return;

        if (_grabbingHand == null)
        {
            TryBeginGrab(_rig.leftHandAnchor, true);
            if (_grabbingHand == null)
            {
                TryBeginGrab(_rig.rightHandAnchor, false);
            }
        }
        else
        {
            bool stillHolding = _isLeftHand
                ? OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > 0.4f || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > 0.4f
                : OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.4f || OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.4f;

            if (!stillHolding)
            {
                _grabbingHand = null;
                return;
            }

            float horizontalPull = Mathf.Abs(_grabbingHand.position.x - _grabStartPos.x);
            if (horizontalPull >= pullThreshold)
            {
                TriggerPull();
            }
        }
    }

    private void TryBeginGrab(Transform hand, bool isLeft)
    {
        if (hand == null) return;
        float dist = Vector3.Distance(hand.position, transform.position);
        if (dist > grabRadius) return;

        bool gripDown = isLeft
            ? OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > 0.5f || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > 0.5f
            : OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.5f || OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.5f;

        if (gripDown)
        {
            _grabbingHand = hand;
            _isLeftHand = isLeft;
            _grabStartPos = hand.position;
        }
    }

    public void TriggerPull()
    {
        if (_triggered) return;
        _triggered = true;
        _grabbingHand = null;
        OnCurtainPulled?.Invoke();
    }
}
