using System.Collections;
using UnityEngine;

namespace QuestTemplate
{
    /// <summary>
    /// [BuildingBlock] Camera Rig(OVRCameraRig) 루트에 부착. 공통 템플릿의 시작 위치와 이동 담당.
    ///
    /// 시작 위치
    /// - 헤드셋의 실제 높이, 바라보는 방향, 트래킹 원점(Floor/Eye Level) 설정과 무관하게
    ///   눈 위치 = SpawnPoint + eyeHeight(기본 1.6m), 시선 = SpawnPoint 정면(+Z)으로 맞춤
    /// - Y 버튼 또는 재중심(Meta 버튼 길게) 시 다시 맞춤
    ///
    /// 조작 (Quest Touch 컨트롤러)
    /// - 왼손 스틱: 이동 (머리 방향 기준, 수평)
    /// - 왼손 트리거(검지): 상승, 왼손 그립(중지): 하강. 바닥(SpawnPoint 높이) + minEyeHeight 아래로는 내려가지 않음
    /// - 오른손 스틱 좌우: 스냅 회전 (머리 위치 중심)
    /// - 비어 있는 버튼(A, B, X)은 조 기능용
    ///
    /// Interaction SDK의 이동 기능(Locomotor, 텔레포트, 회전)은 꺼 두고 이 스크립트만 사용 (이중 이동 방지).
    /// </summary>
    [DisallowMultipleComponent]
    public class RigController : MonoBehaviour
    {
        [Header("시작 위치")]
        [Tooltip("비우면 씬에서 이름이 SpawnPoint인 오브젝트를 찾음. 없으면 원점(0,0,0), +Z 방향")]
        public Transform spawnPoint;
        [Tooltip("바닥(SpawnPoint) 기준 눈높이 (m)")]
        public float eyeHeight = 1.6f;
        [Tooltip("비우면 OVRCameraRig의 CenterEyeAnchor, 없으면 Main Camera")]
        public Transform head;
        [Tooltip("헤드셋 추적 시작을 기다리는 최대 시간 (초)")]
        public float trackingWait = 2f;

        [Header("이동")]
        public float moveSpeed = 2f;
        public float verticalSpeed = 1.5f;
        [Tooltip("스틱, 트리거 입력 무시 범위")]
        [Range(0f, 0.9f)] public float deadZone = 0.2f;
        public float snapAngle = 45f;
        [Tooltip("바닥 기준 최소 눈높이 (m). 하강 한계")]
        public float minEyeHeight = 0.3f;
        [Tooltip("바닥 기준 최대 눈높이 (m). 상승 한계")]
        public float maxEyeHeight = 20f;
        [Tooltip("켜면 Rigidbody 없는 콜라이더(벽 등)에 막힘. 잡을 수 있는 물체는 통과")]
        public bool blockByStaticColliders = true;
        public float bodyRadius = 0.25f;
        [Tooltip("시작 시 Interaction SDK 기본 이동 컴포넌트를 찾아 끔. SDK 버전이 달라 씬 설정이 안 맞아도 이중 이동, 위치 되돌림 방지")]
        public bool disableSdkLocomotion = true;

        [Header("입력")]
        public OVRInput.RawAxis2D moveStick = OVRInput.RawAxis2D.LThumbstick;
        public OVRInput.RawAxis2D turnStick = OVRInput.RawAxis2D.RThumbstick;
        public OVRInput.RawAxis1D upTrigger = OVRInput.RawAxis1D.LIndexTrigger;
        public OVRInput.RawAxis1D downTrigger = OVRInput.RawAxis1D.LHandTrigger;
        public OVRInput.RawButton realignButton = OVRInput.RawButton.Y;

        /// <summary>시작 위치 맞춤이 한 번 이상 끝났는지</summary>
        public bool IsAligned { get; private set; }

        /// <summary>끌 Interaction SDK 이동 컴포넌트 이름 (Oculus.Interaction 네임스페이스만 대상)</summary>
        static readonly string[] SdkLocomotionTypes = { "FirstPersonLocomotor", "PlayerLocomotor", "CharacterController", "TeleportInteractor" };

        float floorY;
        bool turnReady = true;
        int pendingAlignFrames;
        bool subscribed;

        IEnumerator Start()
        {
            if (disableSdkLocomotion) DisableSdkLocomotion();
            if (spawnPoint == null)
            {
                GameObject sp = GameObject.Find("SpawnPoint");
                if (sp != null) spawnPoint = sp.transform;
                else Debug.LogWarning("[QuestTemplate] RigController: SpawnPoint 없음. 원점 기준으로 시작");
            }
            if (head == null)
            {
                OVRCameraRig rig = GetComponentInChildren<OVRCameraRig>();
                if (rig != null && rig.centerEyeAnchor != null) head = rig.centerEyeAnchor;
                else if (Camera.main != null) head = Camera.main.transform;
            }
            if (head == null)
            {
                Debug.LogError("[QuestTemplate] RigController: 머리(카메라)를 찾지 못함. head 연결 필요");
                enabled = false;
                yield break;
            }

            // 헤드셋 추적이 시작될 때까지 대기 (머리 로컬 위치가 0이 아니게 될 때까지, 최대 trackingWait초)
            float waited = 0f;
            while (head.localPosition.sqrMagnitude < 1e-6f && waited < trackingWait)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null; // 앵커 갱신 후 한 프레임 더
            AlignToSpawn();

            if (OVRManager.display != null)
            {
                OVRManager.display.RecenteredPose += OnRecentered;
                subscribed = true;
            }
        }

        void OnDestroy()
        {
            if (subscribed && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        }

        void OnRecentered()
        {
            pendingAlignFrames = 2; // 재중심 직후 머리 위치가 갱신된 다음 맞춤
        }

        /// <summary>눈 위치 = SpawnPoint + eyeHeight, 시선 = SpawnPoint 정면으로 리그를 회전, 이동</summary>
        public void AlignToSpawn()
        {
            if (head == null) return;
            Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            float spawnYaw = spawnPoint != null ? spawnPoint.eulerAngles.y : 0f;

            // 0) 리그는 수평 유지 (Y 회전만)
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            // 1) 방향: 머리 정면(수평)이 SpawnPoint 정면이 되도록 머리 위치 중심으로 회전
            float headYaw = YawOf(FlatForward());
            transform.RotateAround(head.position, Vector3.up, Mathf.DeltaAngle(headYaw, spawnYaw));
            // 2) 위치, 높이: 눈 = SpawnPoint + eyeHeight
            Vector3 target = spawnPos + Vector3.up * eyeHeight;
            transform.position += target - head.position;

            floorY = spawnPos.y;
            IsAligned = true;
            Debug.Log("[QuestTemplate] RigController: 시작 위치 맞춤 완료 (눈높이 " + eyeHeight.ToString("0.00") + "m, 방향 " + spawnYaw.ToString("0") + "도)");
        }

        void Update()
        {
            if (!IsAligned) return;

            if (pendingAlignFrames > 0 && --pendingAlignFrames == 0)
            {
                AlignToSpawn();
                return;
            }
            if (OVRInput.GetDown(realignButton))
            {
                AlignToSpawn();
                return;
            }

            float dt = Time.deltaTime;

            // 수평 이동: 머리 방향 기준
            Vector2 mv = OVRInput.Get(moveStick);
            if (mv.magnitude > deadZone)
            {
                Vector3 fwd = FlatForward();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 delta = (fwd * mv.y + right * mv.x) * (moveSpeed * dt);
                if (!blockByStaticColliders || !IsBlocked(delta)) transform.position += delta;
            }

            // 상승(왼손 트리거), 하강(왼손 그립)
            float up = OVRInput.Get(upTrigger);
            float down = OVRInput.Get(downTrigger);
            float v = (up > deadZone ? up : 0f) - (down > deadZone ? down : 0f);
            if (v != 0f)
            {
                float eye = head.position.y - floorY;
                float step = v * verticalSpeed * dt;
                float next = eye + step;
                if (step < 0f) next = Mathf.Max(next, Mathf.Min(eye, minEyeHeight));
                else next = Mathf.Min(next, Mathf.Max(eye, maxEyeHeight));
                transform.position += Vector3.up * (next - eye);
            }

            // 스냅 회전: 머리 위치 중심 (스틱을 놓아야 다음 회전)
            float tx = OVRInput.Get(turnStick).x;
            if (turnReady && Mathf.Abs(tx) > 0.7f)
            {
                transform.RotateAround(head.position, Vector3.up, Mathf.Sign(tx) * snapAngle);
                turnReady = false;
            }
            else if (Mathf.Abs(tx) < 0.3f)
            {
                turnReady = true;
            }
        }

        /// <summary>Interaction SDK 리그의 기본 이동 컴포넌트를 이름으로 찾아 끔 (SDK 어셈블리를 직접 참조하지 않음)</summary>
        void DisableSdkLocomotion()
        {
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null || mb == this || !mb.enabled) continue;
                System.Type t = mb.GetType();
                if (t.Namespace == null || !t.Namespace.StartsWith("Oculus.Interaction", System.StringComparison.Ordinal)) continue;
                if (System.Array.IndexOf(SdkLocomotionTypes, t.Name) < 0) continue;
                mb.enabled = false;
                Debug.Log("[QuestTemplate] RigController: SDK 이동 컴포넌트 끔 → " + t.Name + " (" + mb.gameObject.name + ")");
            }
        }

        /// <summary>머리 정면의 수평 방향. 바로 아래를 볼 때는 머리 위쪽 방향 사용</summary>
        Vector3 FlatForward()
        {
            Vector3 f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f)
            {
                f = head.up;
                f.y = 0f;
            }
            return f.sqrMagnitude < 1e-6f ? transform.forward : f.normalized;
        }

        static float YawOf(Vector3 flatForward)
        {
            return Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        }

        /// <summary>이동 방향에 Rigidbody 없는 콜라이더(벽 등)가 있으면 true. 리그 자신, 트리거, 이미 겹친 콜라이더는 제외</summary>
        bool IsBlocked(Vector3 delta)
        {
            float dist = delta.magnitude;
            if (dist < 1e-5f) return false;
            RaycastHit[] hits = Physics.SphereCastAll(head.position, bodyRadius, delta / dist, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.distance <= 0f) continue;
                if (h.rigidbody != null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }
    }
}
