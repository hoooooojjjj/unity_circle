using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 순수 6DoF 실보행 VR 체험용 시작 위치 정렬 및 단축키 재시작 컨트롤러 (화면 UI 없음).
/// - 조이스틱 이동, 트리거 상승/하강, 스냅 회전 기능을 일체 포함하지 않습니다.
/// - playerScale(기본 0.6 = 3/5)로 FloorLevel(y=0)과 자연스러운 눈높이를 유지합니다.
/// - 단축키:
///   * X(왼손) / A(오른손) / 에디터 R : 처음부터 다시 시작 (현재 공간 정렬 유지)
///   * Y(왼손) / B(오른손) / 에디터 T : 정면 맞춤(내가 보고 있는 정면 기준)으로 처음부터 다시 시작
/// </summary>
public class VRSpawnAligner : MonoBehaviour
{
    public Transform spawnPoint;
    public float maxTrackingWait = 2.0f;

    [Tooltip("사용자 키/스케일 배율 (0.6 = 3/5 높이, 현실 바닥 y=0 및 스테레오 비율 자연 유지)")]
    [Range(0.3f, 1.5f)] public float playerScale = 0.6f;

    [Header("재시작 / 정면 맞춤 단축키 설정 (화면 UI 없음)")]
    [Tooltip("씬 로드 직후 중복 입력 방지를 위한 대기 시간(초)")]
    public float inputCooldownAfterLoad = 0.8f;

    private static bool _preserveNextAlignment;
    private static Vector3 _savedRigPos;
    private static Quaternion _savedRigRot;

    private OVRCameraRig _rig;
    private float _readyTime;
    private bool _isReloading;

    private bool _prevLeftPrimary;
    private bool _prevRightPrimary;
    private bool _prevLeftSecondary;
    private bool _prevRightSecondary;

    private void Awake()
    {
        transform.localScale = Vector3.one * playerScale;
        _readyTime = Time.unscaledTime + inputCooldownAfterLoad;

#if !UNITY_EDITOR
        if (_preserveNextAlignment)
        {
            transform.position = _savedRigPos;
            transform.rotation = _savedRigRot;
        }
        else
        {
            // 첫 프레임부터 FloorLevel(y=0) 기준으로 시작하도록 즉시 정렬 (에디터용 y=0.87 오프셋 제거)
            Vector3 p = transform.position;
            p.y = spawnPoint != null ? spawnPoint.position.y : 0f;
            transform.position = p;
        }
#endif
    }

    private IEnumerator Start()
    {
#if UNITY_EDITOR
        // 에디터에서는 DesktopVRFallbackMovement가 씬 배치 위치(y=0.87)를 그대로 사용
        _preserveNextAlignment = false;
        yield break;
#else
        transform.localScale = Vector3.one * playerScale;
        _rig = GetComponentInChildren<OVRCameraRig>();
        if (spawnPoint == null)
        {
            GameObject sp = GameObject.Find("SpawnPoint");
            if (sp != null) spawnPoint = sp.transform;
        }

        if (_preserveNextAlignment)
        {
            _preserveNextAlignment = false;
            Vector3 p = _savedRigPos;
            p.y = spawnPoint != null ? spawnPoint.position.y : 0f;
            transform.position = p;
            transform.rotation = _savedRigRot;
            Debug.Log("[VRSpawnAligner] 기존 공간 정렬을 유지한 채 처음부터 다시 시작했습니다.");
            yield break;
        }

        Vector3 initPos = transform.position;
        initPos.y = spawnPoint != null ? spawnPoint.position.y : 0f;
        transform.position = initPos;

        Transform head = (_rig != null && _rig.centerEyeAnchor != null) ? _rig.centerEyeAnchor : Camera.main?.transform;
        if (head == null || spawnPoint == null) yield break;

        // 헤드셋 실제 트래킹 포즈가 들어올 때까지 짧게 대기
        float waited = 0f;
        while (head.localPosition.sqrMagnitude < 1e-6f && waited < maxTrackingWait)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        // OVRCameraRig.UpdateAnchors가 최신 포즈를 반영하도록 2프레임 대기
        yield return null;
        yield return null;

        AlignHorizontalOnly(head);
#endif
    }

    private void Update()
    {
        if (_isReloading || Time.unscaledTime < _readyTime)
        {
            // 쿨다운 동안 버튼 상태만 동기화해 누르고 있던 버튼이 풀린 뒤 새 입력만 받도록 처리
            _prevLeftPrimary = VRHandInput.IsPrimaryButtonPressed(true);
            _prevRightPrimary = VRHandInput.IsPrimaryButtonPressed(false);
            _prevLeftSecondary = VRHandInput.IsSecondaryButtonPressed(true);
            _prevRightSecondary = VRHandInput.IsSecondaryButtonPressed(false);
            return;
        }

        bool lp = VRHandInput.IsPrimaryButtonPressed(true);     // 왼손 X
        bool rp = VRHandInput.IsPrimaryButtonPressed(false);    // 오른손 A
        bool ls = VRHandInput.IsSecondaryButtonPressed(true);   // 왼손 Y
        bool rs = VRHandInput.IsSecondaryButtonPressed(false);  // 오른손 B

        bool restartPressed = (lp && !_prevLeftPrimary) || (rp && !_prevRightPrimary);
        bool recenterRestartPressed = (ls && !_prevLeftSecondary) || (rs && !_prevRightSecondary);

        bool pressedLeft = (lp && !_prevLeftPrimary) || (ls && !_prevLeftSecondary);

        _prevLeftPrimary = lp;
        _prevRightPrimary = rp;
        _prevLeftSecondary = ls;
        _prevRightSecondary = rs;

#if UNITY_EDITOR
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.rKey.wasPressedThisFrame) restartPressed = true;
            if (kb.tKey.wasPressedThisFrame) recenterRestartPressed = true;
        }
#endif

        if (recenterRestartPressed)
        {
            VRHandInput.SendHaptic(pressedLeft, 0.55f, 0.14f);
            RestartScene(recenterForward: true);
        }
        else if (restartPressed)
        {
            VRHandInput.SendHaptic(pressedLeft, 0.35f, 0.08f);
            RestartScene(recenterForward: false);
        }
    }

    public void RestartScene(bool recenterForward)
    {
        if (_isReloading) return;
        _isReloading = true;

        if (recenterForward)
        {
            _preserveNextAlignment = false;
            Debug.Log("[VRSpawnAligner] 정면 맞춤(현재 시선 정면 기준)으로 처음부터 다시 시작합니다.");
        }
        else
        {
            _preserveNextAlignment = true;
            _savedRigPos = transform.position;
            _savedRigRot = transform.rotation;
            Debug.Log("[VRSpawnAligner] 현재 공간 정렬을 유지하며 처음부터 다시 시작합니다.");
        }

        Scene active = SceneManager.GetActiveScene();
        if (active.buildIndex >= 0)
        {
            SceneManager.LoadScene(active.buildIndex);
        }
        else
        {
            SceneManager.LoadScene(active.name);
        }
    }

    public void AlignHorizontalOnly(Transform head)
    {
        AlignToTransform(spawnPoint, head);
    }

    /// <summary>
    /// 참여자(VR 헤드셋 또는 에디터 카메라)를 지정한 위치·정면 방향으로 점프샷 정렬합니다.
    /// - VR(Quest 3): 바닥 높이(y=0)와 playerScale은 그대로 유지한 채, 현재 머리의 수평(X, Z)과 정면(Yaw)을 target에 맞춤.
    /// - Unity 에디터: 리그를 target 위치(기존 에디터 눈높이 유지)와 정면(Pitch 0)으로 즉시 이동.
    /// </summary>
    public void AlignToTransform(Transform target, Transform headOverride = null)
    {
        if (target == null) return;

#if UNITY_EDITOR
        Vector3 editorPos = new Vector3(target.position.x, transform.position.y, target.position.z);
        transform.position = editorPos;
        transform.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);

        var fallback = GetComponent<DesktopVRFallbackMovement>();
        if (fallback != null)
        {
            fallback.SnapLookTo(target.eulerAngles.y, 0f);
        }
        else if (_rig != null && _rig.trackingSpace != null)
        {
            _rig.trackingSpace.localRotation = Quaternion.identity;
        }

        Debug.Log("[VRSpawnAligner] 에디터 점프샷 정렬 완료: target=" + target.name + " pos=" + transform.position);
#else
        if (_rig == null) _rig = GetComponentInChildren<OVRCameraRig>();
        Transform head = headOverride != null
            ? headOverride
            : ((_rig != null && _rig.centerEyeAnchor != null) ? _rig.centerEyeAnchor : Camera.main?.transform);
        if (head == null) return;

        // FloorLevel 기준: 리그의 Y=target.y(0) 유지 (현실 바닥 = 가상 바닥 y=0, playerScale 배율로 눈높이 반영)
        Vector3 rigPos = transform.position;
        rigPos.y = target.position.y;
        transform.position = rigPos;

        // 1) 수평 시선 방향(Yaw)을 target 정면으로 맞춤
        Vector3 flatFwd = head.forward;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-4f)
        {
            flatFwd = head.up;
            flatFwd.y = 0f;
        }
        if (flatFwd.sqrMagnitude > 1e-4f)
        {
            float headYaw = Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
            float targetYaw = target.eulerAngles.y;
            transform.RotateAround(head.position, Vector3.up, Mathf.DeltaAngle(headYaw, targetYaw));
        }

        // 2) 수평 위치(X, Z)만 target으로 이동 (Y 높이는 절대 건드리지 않음)
        Vector3 deltaXZ = new Vector3(
            target.position.x - head.position.x,
            0f,
            target.position.z - head.position.z
        );
        transform.position += deltaXZ;

        Debug.Log("[VRSpawnAligner] 6DoF 점프샷 정렬 완료: target=" + target.name + " headPos=" + head.position + " scale=" + transform.localScale.x);
#endif
    }
}
