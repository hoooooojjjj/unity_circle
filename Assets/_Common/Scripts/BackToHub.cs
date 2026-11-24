using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestTemplate
{
    /// <summary>
    /// Camera Rig에 부착. 왼손 컨트롤러 메뉴 버튼(≡) 동작:
    /// - 조 작업 공간(Hub가 Scene List에 없음): 현재 씬을 처음 상태로 다시 시작 (초기화)
    /// - 통합 앱(Hub 등록됨): 조 씬에서는 Hub로 복귀, Hub에서는 Hub 다시 시작
    /// 버튼이나 Building Blocks 이벤트에서 Go()를 직접 호출해도 됨.
    /// </summary>
    public class BackToHub : MonoBehaviour
    {
        [Tooltip("왼손 메뉴 버튼 사용")]
        public bool useMenuButton = true;

        void Update()
        {
            if (useMenuButton && OVRInput.GetDown(OVRInput.Button.Start)) Go();
        }

        public void Go()
        {
            bool inHub = SceneManager.GetActiveScene().name == SceneNav.HubSceneName;
            if (!inHub && SceneNav.HasHub()) SceneNav.LoadHub();
            else SceneNav.ReloadCurrent();
        }
    }
}
