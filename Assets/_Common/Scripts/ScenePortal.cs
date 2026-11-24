using UnityEngine;

namespace QuestTemplate
{
    /// <summary>
    /// Hub의 포털. 플레이어 머리(Main Camera)가 원 안(수평 거리 radius 이내)에 들어오면 해당 씬으로 이동.
    /// 콜라이더나 트리거 없이 동작.
    /// </summary>
    public class ScenePortal : MonoBehaviour
    {
        public string sceneName;
        public float radius = 0.6f;
        [Tooltip("씬 시작 직후 오작동 방지 대기 시간(초)")]
        public float armDelay = 1.5f;

        float enabledAt;
        bool loading;

        void OnEnable()
        {
            enabledAt = Time.time;
            loading = false;
        }

        void Update()
        {
            if (loading || string.IsNullOrEmpty(sceneName)) return;
            if (Time.time - enabledAt < armDelay) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 head = cam.transform.position;
            Vector3 center = transform.position;
            head.y = 0f;
            center.y = 0f;
            if (Vector3.Distance(head, center) <= radius)
            {
                loading = true;
                SceneNav.Load(sceneName);
            }
        }
    }
}
