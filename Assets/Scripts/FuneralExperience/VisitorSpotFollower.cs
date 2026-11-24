using UnityEngine;

/// <summary>
/// 참여자(HMD)의 바닥상 (X, Z) 위치만 부드럽게 추적하는 조명 컨트롤러.
/// 카메라 회전을 따라가지 않으므로 고개를 돌려도 빛이 손전등처럼 휘둘리지 않습니다.
/// </summary>
public class VisitorSpotFollower : MonoBehaviour
{
    public Transform targetHead;
    public float fixedHeight = 3.1f;
    public float smoothSpeed = 3.5f;

    private Vector3 _initialSpotPos;
    private Vector3 _initialHeadPos;
    private bool _captured;

    private void Start()
    {
        fixedHeight = transform.position.y;
        _initialSpotPos = transform.position;

        if (targetHead == null)
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                targetHead = rig.centerEyeAnchor;
            }
            else if (Camera.main != null)
            {
                targetHead = Camera.main.transform;
            }
        }

        if (targetHead != null)
        {
            _initialHeadPos = targetHead.position;
            _captured = true;
        }
    }

    private void LateUpdate()
    {
        if (targetHead == null) return;
        if (!_captured)
        {
            _initialHeadPos = targetHead.position;
            _captured = true;
        }

        // 시작 위치 그대로 유지하다가 참여자가 이동한 만큼만 부드럽게 따라감
        Vector3 headDelta = targetHead.position - _initialHeadPos;
        Vector3 desired = new Vector3(_initialSpotPos.x + headDelta.x, fixedHeight, _initialSpotPos.z + headDelta.z);
        transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * smoothSpeed);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
}
