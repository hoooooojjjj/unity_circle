#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestTemplate.EditorTools
{
    /// <summary>
    /// 강사용 메뉴 QuestTemplate
    /// 1. Create Team Workspaces : Teams/Team01~05 폴더, TeamNN_Main 씬(템플릿 복제), 루트 이름 변경, 조별 어셈블리(TeamNN.asmdef)
    /// 2. Register Team Scenes  : Scene List = Hub + Teams/*/Scenes/*_Main
    /// 3. Export Team Packages  : Builds/TeamKits 에 Common + 조별 패키지 내보내기
    /// 4. Check Team Rules      : 통합 대비 점검 (어셈블리, namespace, 전역 영향, 조 폴더 밖 참조, 셰이더 이름)
    /// </summary>
    public static class QuestTemplateTools
    {
        public const string Version = "v1";
        public const int TeamCount = 5;

        const string CommonPath = "Assets/_Common";
        const string TemplateScene = "Assets/_Common/Scenes/Team_Template.unity";
        const string HubScene = "Assets/_Common/Scenes/Hub.unity";
        const string TeamsFolder = "Assets/Teams";
        const string TemplateRootName = "TeamNN_Root";
        static readonly string[] SubFolders = { "Scenes", "Scripts", "Prefabs", "Materials", "Models", "Textures", "Audio" };

        /// <summary>조 어셈블리가 참조할 어셈블리. 프로젝트에 있는 것만 넣음</summary>
        static readonly string[] TeamAssemblyRefs =
        {
            "QuestTemplate", "Oculus.VR", "Oculus.Interaction", "Oculus.Interaction.OVR",
            "Unity.TextMeshPro", "UnityEngine.UI", "Unity.InputSystem"
        };

        static string TeamName(int i) { return "Team" + i.ToString("00"); }
        static string TeamPrefix(int i) { return "T" + i.ToString("00") + "_"; }

        [MenuItem("QuestTemplate/1. Create Team Workspaces")]
        public static void CreateTeamWorkspaces()
        {
            if (!File.Exists(TemplateScene))
            {
                Debug.LogError("[QuestTemplate] 템플릿 씬 없음: " + TemplateScene);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            HashSet<string> assemblies = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name));
            if (!assemblies.Contains("QuestTemplate"))
                Debug.LogWarning("[QuestTemplate] QuestTemplate 어셈블리 없음. _Common/Scripts/QuestTemplate.asmdef 확인 후 다시 실행 권장");

            EnsureFolder("Assets", "Teams");
            var report = new StringBuilder();
            for (int i = 1; i <= TeamCount; i++)
            {
                string team = TeamName(i);
                string root = TeamsFolder + "/" + team;
                EnsureFolder(TeamsFolder, team);
                foreach (string sub in SubFolders) EnsureFolder(root, sub);

                string scenePath = root + "/Scenes/" + team + "_Main.unity";
                if (!File.Exists(scenePath))
                {
                    if (!AssetDatabase.CopyAsset(TemplateScene, scenePath))
                    {
                        Debug.LogError("[QuestTemplate] 씬 복제 실패: " + scenePath);
                        continue;
                    }
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    GameObject rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == TemplateRootName);
                    if (rootGo != null) rootGo.name = team + "_Root";
                    else Debug.LogWarning("[QuestTemplate] " + TemplateRootName + " 없음: " + scenePath);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                string asmdef = root + "/" + team + ".asmdef";
                if (!File.Exists(asmdef))
                {
                    List<string> refs = TeamAssemblyRefs.Where(assemblies.Contains).ToList();
                    List<string> missing = TeamAssemblyRefs.Where(r => !assemblies.Contains(r)).ToList();
                    File.WriteAllText(asmdef, TeamAsmdefJson(team, refs), new UTF8Encoding(false));
                    if (missing.Count > 0) report.AppendLine("  " + team + ".asmdef 참조 생략 (프로젝트에 없음): " + string.Join(", ", missing));
                }

                string readme = root + "/README_" + team + ".txt";
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        team + " 작업 공간\n" +
                        "- 씬: Scenes/" + team + "_Main (루트: " + team + "_Root)\n" +
                        "- 스크립트: 어셈블리 " + team + ".asmdef, namespace " + team + "\n" +
                        "- 파일 이름 접두어: " + TeamPrefix(i) + "\n" +
                        "- 이 폴더 밖은 수정 금지. 점검: QuestTemplate > 4. Check Team Rules\n", new UTF8Encoding(false));
                }
                report.AppendLine(team + "  " + scenePath);
            }
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);
            Debug.Log("[QuestTemplate] 조 작업 공간 생성 완료\n" + report);
        }

        static string TeamAsmdefJson(string team, List<string> refs)
        {
            string refList = string.Join(",\n        ", refs.Select(r => "\"" + r + "\""));
            return "{\n" +
                   "    \"name\": \"" + team + "\",\n" +
                   "    \"rootNamespace\": \"" + team + "\",\n" +
                   "    \"references\": [\n        " + refList + "\n    ],\n" +
                   "    \"includePlatforms\": [],\n" +
                   "    \"excludePlatforms\": [],\n" +
                   "    \"allowUnsafeCode\": false,\n" +
                   "    \"overrideReferences\": false,\n" +
                   "    \"precompiledReferences\": [],\n" +
                   "    \"autoReferenced\": false,\n" +
                   "    \"defineConstraints\": [],\n" +
                   "    \"versionDefines\": [],\n" +
                   "    \"noEngineReferences\": false\n" +
                   "}\n";
        }

        [MenuItem("QuestTemplate/2. Register Team Scenes")]
        public static void RegisterTeamScenes()
        {
            if (!File.Exists(HubScene))
            {
                Debug.LogError("[QuestTemplate] Hub 씬 없음: " + HubScene);
                return;
            }
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(HubScene, true) };
            if (AssetDatabase.IsValidFolder(TeamsFolder))
            {
                IEnumerable<string> teamScenes = AssetDatabase.FindAssets("t:Scene", new[] { TeamsFolder })
                    .Select(g => AssetDatabase.GUIDToAssetPath(g))
                    .Where(p => p.Contains("/Scenes/") && Path.GetFileNameWithoutExtension(p).EndsWith("_Main", StringComparison.Ordinal))
                    .Distinct()
                    .OrderBy(p => p, StringComparer.Ordinal);
                foreach (string p in teamScenes) scenes.Add(new EditorBuildSettingsScene(p, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            string list = string.Join("\n", scenes.Select((s, i) => i + "  " + s.path));
            Debug.Log("[QuestTemplate] Scene List 등록 완료: Hub + 조 씬 " + (scenes.Count - 1) + "개\n" + list);
        }

        [MenuItem("QuestTemplate/3. Export Team Packages")]
        public static void ExportTeamPackages()
        {
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "TeamKits");
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();

            string common = Path.Combine(outDir, "QuestTemplate_" + Version + "_Common.unitypackage");
            AssetDatabase.ExportPackage(CommonPath, common, ExportPackageOptions.Recurse);
            report.AppendLine(common);

            for (int i = 1; i <= TeamCount; i++)
            {
                string team = TeamName(i);
                string root = TeamsFolder + "/" + team;
                if (!AssetDatabase.IsValidFolder(root))
                {
                    Debug.LogWarning("[QuestTemplate] 폴더 없음, 건너뜀: " + root);
                    continue;
                }
                string file = Path.Combine(outDir, "QuestTemplate_" + Version + "_" + team + ".unitypackage");
                AssetDatabase.ExportPackage(new[] { CommonPath, root }, file, ExportPackageOptions.Recurse);
                report.AppendLine(file);
            }
            Debug.Log("[QuestTemplate] 패키지 내보내기 완료\n" + report);
            EditorUtility.RevealInFinder(outDir);
        }

        // 스크립트 금지 패턴 (오류): 씬 밖으로 영향이 남아 통합 앱의 허브, 다른 조 씬을 깨뜨림
        static readonly string[][] ForbiddenCode =
        {
            new[] { "DontDestroyOnLoad", "DontDestroyOnLoad 금지 (씬을 나가도 남아 다른 조 씬에 영향)" },
            new[] { "RuntimeInitializeOnLoadMethod", "RuntimeInitializeOnLoadMethod 금지 (앱 시작 시 모든 씬에서 실행)" },
            new[] { "Resources.Load", "Resources.Load 금지 (이름 충돌). 에셋은 인스펙터로 연결" },
            new[] { "UnityEditor", "조 스크립트에서 UnityEditor 사용 금지 (빌드 실패)" },
        };

        // 스크립트 주의 패턴 (경고): 바꾸면 씬 종료 시 원래 값으로 복구해야 함
        static readonly string[] GlobalSettings =
        {
            "Time.timeScale", "Time.fixedDeltaTime", "Physics.gravity", "RenderSettings.", "QualitySettings.",
            "Application.targetFrameRate", "AudioListener.volume", "SceneManager.sceneLoaded"
        };

        static readonly Regex ShaderNameRegex = new Regex("Shader\\s+\"([^\"]+)\"");

        [MenuItem("QuestTemplate/4. Check Team Rules")]
        public static void CheckTeamRules()
        {
            var errors = new StringBuilder();
            var warnings = new StringBuilder();
            int checkedTeams = 0, checkedScripts = 0;

            for (int i = 1; i <= TeamCount; i++)
            {
                string team = TeamName(i);
                string root = TeamsFolder + "/" + team;
                if (!AssetDatabase.IsValidFolder(root)) continue;
                checkedTeams++;

                // 1) 조 어셈블리
                if (!File.Exists(root + "/" + team + ".asmdef"))
                    errors.AppendLine(team + ": " + team + ".asmdef 없음 (조 스크립트 어셈블리 분리 필요)");

                // 2) 금지 폴더
                foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(dir);
                    string rel = dir.Replace('\\', '/');
                    if (name == "Resources") errors.AppendLine(team + ": Resources 폴더 금지 → " + rel);
                    else if (name == "Editor") errors.AppendLine(team + ": Editor 폴더 금지 → " + rel);
                    else if (name == "StreamingAssets" || name == "Plugins") errors.AppendLine(team + ": 특수 폴더 금지 → " + rel);
                }

                // 3) 스크립트
                foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    checkedScripts++;
                    string code = File.ReadAllText(path);
                    if (!Regex.IsMatch(code, "namespace\\s+" + team + "\\b"))
                        errors.AppendLine(team + ": namespace " + team + " 누락 → " + path);
                    foreach (string[] f in ForbiddenCode)
                        if (code.Contains(f[0])) errors.AppendLine(team + ": " + f[1] + " → " + path);
                    foreach (string g in GlobalSettings)
                        if (code.Contains(g)) warnings.AppendLine(team + ": 전역 설정 " + g.TrimEnd('.') + " 사용. OnDestroy, OnDisable에서 복구, 해제 확인 → " + path);
                }

                // 4) 셰이더 이름
                foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    {
                        Match m = ShaderNameRegex.Match(File.ReadAllText(path));
                        if (m.Success && !m.Groups[1].Value.StartsWith(team + "/", StringComparison.Ordinal))
                            errors.AppendLine(team + ": 셰이더 이름은 \"" + team + "/\"로 시작 (현재 \"" + m.Groups[1].Value + "\") → " + path);
                    }
                    else if (!Path.GetFileName(path).StartsWith(TeamPrefix(i), StringComparison.Ordinal))
                    {
                        warnings.AppendLine(team + ": Shader Graph 파일 이름은 " + TeamPrefix(i) + "로 시작 권장 → " + path);
                    }
                }

                // 5) 씬
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) != team + "_Main")
                        warnings.AppendLine(team + ": 씬은 " + team + "_Main 하나 권장 (허브 포털은 _Main만 연결) → " + path);
                }

                // 6) 조 폴더 밖 에셋 참조 (통합 시 누락, 충돌)
                string[] teamAssets = AssetDatabase.FindAssets("", new[] { root })
                    .Select(g => AssetDatabase.GUIDToAssetPath(g))
                    .Where(p => !AssetDatabase.IsValidFolder(p))
                    .Distinct().ToArray();
                if (teamAssets.Length > 0)
                {
                    foreach (string dep in AssetDatabase.GetDependencies(teamAssets, true).Distinct())
                    {
                        if (!dep.StartsWith("Assets/", StringComparison.Ordinal)) continue;           // 패키지, 내장 리소스
                        if (dep.StartsWith(root + "/", StringComparison.Ordinal)) continue;
                        if (dep.StartsWith(CommonPath + "/", StringComparison.Ordinal)) continue;
                        if (dep.StartsWith(TeamsFolder + "/", StringComparison.Ordinal))
                            errors.AppendLine(team + ": 다른 조 에셋 참조 금지 → " + dep);
                        else
                            warnings.AppendLine(team + ": 조 폴더 밖 에셋 참조 (통합 시 누락 가능, 조 폴더로 복제 권장) → " + dep);
                    }
                }
            }

            string head = "[QuestTemplate] 규칙 점검: 조 " + checkedTeams + "개, 스크립트 " + checkedScripts + "개";
            if (errors.Length == 0 && warnings.Length == 0) Debug.Log(head + " → 통과");
            if (warnings.Length > 0) Debug.LogWarning(head + " → 경고\n" + warnings);
            if (errors.Length > 0) Debug.LogError(head + " → 위반 (통합 전 수정 필요)\n" + errors);
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
