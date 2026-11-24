using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestTemplate
{
    /// <summary>
    /// 씬 이동 공통 기능. BackToHub, ScenePortal, HubPortals, 조 스크립트에서 호출.
    /// 조 스크립트 사용 예: QuestTemplate.SceneNav.ReloadCurrent(); (처음부터 다시), QuestTemplate.SceneNav.LoadHub();
    /// </summary>
    public static class SceneNav
    {
        public const string HubSceneName = "Hub";
        public const string TeamsFolder = "Assets/Teams/";

        public static void LoadHub()
        {
            Load(HubSceneName);
        }

        public static void Load(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[QuestTemplate] 씬 이름이 비어 있음");
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"[QuestTemplate] '{sceneName}' 씬이 Scene List에 없음. 통합 프로젝트에서 QuestTemplate > 2. Register Team Scenes 실행 필요");
                return;
            }
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        /// <summary>Hub 씬이 Scene List에 있는지 (통합 앱이면 true, 조 단독 프로젝트면 보통 false)</summary>
        public static bool HasHub()
        {
            return Application.CanStreamedLevelBeLoaded(HubSceneName);
        }

        /// <summary>현재 씬을 처음 상태로 다시 시작 (오브젝트 위치, 상태 모두 초기화)</summary>
        public static void ReloadCurrent()
        {
            Scene s = SceneManager.GetActiveScene();
            if (s.buildIndex >= 0)
            {
                SceneManager.LoadScene(s.buildIndex, LoadSceneMode.Single);
                return;
            }
#if UNITY_EDITOR
            // Scene List에 없는 씬도 에디터 Play 중에는 다시 불러올 수 있음
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(s.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Debug.LogWarning("[QuestTemplate] 현재 씬이 Scene List에 없어 다시 시작할 수 없음");
#endif
        }

        /// <summary>Scene List에 등록된 씬 중 Assets/Teams/ 아래 씬 이름 목록 (정렬)</summary>
        public static List<string> FindTeamScenes()
        {
            var result = new List<string>();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i).Replace('\\', '/');
                if (path.StartsWith(TeamsFolder, StringComparison.Ordinal))
                    result.Add(Path.GetFileNameWithoutExtension(path));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
