using System.Collections.Generic;
using UnityEngine;

namespace QuestTemplate
{
    /// <summary>
    /// Hub 씬에 부착. Scene List의 조 씬(Assets/Teams/ 아래) 수만큼 포털을 이 오브젝트 앞쪽 반원에 자동 배치.
    /// 조가 늘어나도 씬 등록만 하면 포털이 생김.
    /// </summary>
    public class HubPortals : MonoBehaviour
    {
        [Tooltip("ScenePortal과 자식 TextMesh(라벨)를 가진 프리팹")]
        public ScenePortal portalPrefab;
        public float distance = 3.5f;
        [Tooltip("포털이 펼쳐지는 전체 각도")]
        public float spreadDegrees = 140f;
        public string sceneSuffix = "_Main";

        void Start()
        {
            if (portalPrefab == null)
            {
                Debug.LogError("[QuestTemplate] HubPortals: portalPrefab 연결 누락");
                return;
            }
            List<string> scenes = SceneNav.FindTeamScenes();
            if (scenes.Count == 0)
            {
                Debug.LogWarning("[QuestTemplate] HubPortals: 등록된 조 씬 없음. QuestTemplate > 2. Register Team Scenes 실행 필요");
                return;
            }
            Vector3 center = transform.position;
            for (int i = 0; i < scenes.Count; i++)
            {
                float t = scenes.Count == 1 ? 0.5f : (float)i / (scenes.Count - 1);
                float angle = Mathf.Deg2Rad * (-spreadDegrees / 2f + spreadDegrees * t) + Mathf.Deg2Rad * transform.eulerAngles.y;
                Vector3 pos = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * distance;
                Vector3 outward = pos - center;
                outward.y = 0f;
                ScenePortal portal = Instantiate(portalPrefab, pos, Quaternion.LookRotation(outward), transform);
                portal.sceneName = scenes[i];
                portal.name = "Portal_" + scenes[i];
                TextMesh label = portal.GetComponentInChildren<TextMesh>(true);
                if (label != null)
                {
                    string n = scenes[i];
                    label.text = n.EndsWith(sceneSuffix) ? n.Substring(0, n.Length - sceneSuffix.Length) : n;
                }
            }
        }
    }
}
