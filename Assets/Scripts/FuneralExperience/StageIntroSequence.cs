using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Video;

/// <summary>
/// 체험 순차 진행 컨트롤러:
/// 1. Play 시작 -> CurtainFront 바로 뒤쪽 VideoSurface에서 영상 재생
/// 2. 영상 종료 -> 영상 Fade Out
/// 3. Fade Out 완료 -> FuneralGuide 1차 음성 재생
/// 4. 1차 음성 종료 -> VR에서 CurtainFront 우측을 좌측으로 슬라이드해 걷기
/// 5. 커튼을 열고 관 앞에 서면 -> 잠시 후 FuneralGuide 2차 음성 재생
/// 6. 2차 음성 종료 -> VR에서 Chrysanthemum(국화)을 들어 관 위에 헌화
/// </summary>
public class StageIntroSequence : MonoBehaviour
{
    public enum IntroState
    {
        PlayingVideo,
        FadingOutVideo,
        PlayingGuideVoice,
        WaitingCurtainSlide,
        WaitingVisitorAtCoffin,
        PlayingOfferingVoice,
        WaitingFlowerOffering,
        FlowerOffered,
        FadingOutScreen,
        Finished
    }

    [Header("1. 영상 설정 (여기에 영상 파일을 넣으세요)")]
    [Tooltip("여기에 영상 파일을 넣거나 VideoSurface의 VideoPlayer > Video Clip에 넣으면 자동 재생됩니다.")]
    public VideoClip videoClip;
    public VideoPlayer videoPlayer;
    public Renderer videoSurfaceRenderer;
    public float videoFadeOutDuration = 1.5f;
    [Tooltip("영상이 Fade Out될 때 투명도를 1로 복원할 커튼들 (비우면 씬의 모든 커튼 자동 적용)")]
    public ProceduralCurtainMesh[] curtainsToRestoreOpacity;

    [Header("2. 장례지도사 1차 음성 (영상 종료 후)")]
    [Tooltip("영상이 Fade Out된 뒤 재생될 1차 안내 음성")]
    public AudioClip guideVoiceClip;
    public AudioSource guideAudioSource;
    public FuneralGuideAnimator guideAnimator;
    public float delayAfterVideoFade = 0.3f;

    [Header("3. 커튼 슬라이드 & 관 앞 도착 판정")]
    public CurtainSlideInteractable curtainSlide;
    public Transform coffinRoot;
    [Tooltip("커튼이 걷어진 뒤 이 Z 좌표를 넘어가거나 관과 일정 거리 이내로 오면 '관 앞 도착'으로 판정")]
    public float entryZThreshold = 0.72f;
    [Tooltip("관 중심으로부터 수평 거리(m) 이내에 서면 관 앞 도착으로 판정")]
    public float coffinApproachDistance = 1.25f;

    [Header("4. 장례지도사 2차 음성 (관 앞에 선 뒤) & 국화 헌화")]
    [Tooltip("관 앞에 선 뒤 2차 음성이 나오기 전 대기 시간(초)")]
    public float delayBeforeOfferingVoice = 1.5f;
    [Tooltip("관 앞에 섰을 때 FuneralGuide에서 재생될 2차 헌화 안내 음성")]
    public AudioClip guideOfferingVoiceClip;
    public FlowerInteractable chrysanthemum;

    [Header("5. 헌화 후 화면 암전 (Fade Out)")]
    [Tooltip("헌화 완료 후 화면이 어두워지기 전 여운 대기 시간(초)")]
    public float delayBeforeScreenFadeOut = 2.5f;
    [Tooltip("화면이 완전히 어두워지는 데 걸리는 시간(초)")]
    public float screenFadeOutDuration = 2.5f;
    public VRScreenFader screenFader;

    [Header("진행 상태 (읽기 전용 확인용)")]
    public IntroState currentState = IntroState.PlayingVideo;

    [Header("이벤트 (다음 단계 확장용)")]
    public UnityEvent onCurtainOpened;
    public UnityEvent onVisitorArrivedAtCoffin;
    public UnityEvent onFlowerOffered;
    public UnityEvent onScreenFadedOut;

    private Material _videoMat;
    private bool _videoCompleted;
    private bool _curtainOpened;
    private bool _flowerOffered;
    private Transform _visitorHead;

    private void Awake()
    {
        if (videoSurfaceRenderer != null)
        {
            _videoMat = videoSurfaceRenderer.material;
        }
    }

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null) _visitorHead = rig.centerEyeAnchor;
        else if (Camera.main != null) _visitorHead = Camera.main.transform;

        if (curtainsToRestoreOpacity == null || curtainsToRestoreOpacity.Length == 0)
        {
            curtainsToRestoreOpacity = FindObjectsByType<ProceduralCurtainMesh>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        if (videoPlayer != null) videoPlayer.loopPointReached += OnVideoFinished;
        if (curtainSlide != null)
        {
            curtainSlide.OnCurtainOpened += HandleCurtainOpened;
            curtainSlide.EnableInteraction(false);
        }
        if (chrysanthemum != null)
        {
            chrysanthemum.OnFlowerPlaced += HandleFlowerPlaced;
            chrysanthemum.EnableInteraction(false);
        }

        StartCoroutine(RunSequence());
    }

    private void OnDestroy()
    {
        if (videoPlayer != null) videoPlayer.loopPointReached -= OnVideoFinished;
        if (curtainSlide != null) curtainSlide.OnCurtainOpened -= HandleCurtainOpened;
        if (chrysanthemum != null) chrysanthemum.OnFlowerPlaced -= HandleFlowerPlaced;
    }

    private void Update()
    {
#if UNITY_EDITOR
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // 에디터 테스트 편의: Space로 현재 영상/음성 대기 스킵
        if (kb.spaceKey.wasPressedThisFrame)
        {
            if (currentState == IntroState.PlayingVideo)
            {
                _videoCompleted = true;
            }
            else if ((currentState == IntroState.PlayingGuideVoice || currentState == IntroState.PlayingOfferingVoice)
                     && guideAudioSource != null && guideAudioSource.isPlaying)
            {
                guideAudioSource.Stop();
            }
        }

        // 에디터 테스트 편의: 2차 음성 종료 후 F키로 국화 잡기 -> 다시 F키로 관에 헌화
        if (kb.fKey.wasPressedThisFrame && currentState == IntroState.WaitingFlowerOffering && chrysanthemum != null)
        {
            chrysanthemum.DesktopToggleGrabOrPlace();
        }
#endif
    }

    private IEnumerator RunSequence()
    {
        // --- 1단계: 영상 재생 ---
        currentState = IntroState.PlayingVideo;
        _videoCompleted = false;

        VideoClip activeClip = videoClip != null ? videoClip : (videoPlayer != null ? videoPlayer.clip : null);
        if (videoPlayer != null && activeClip != null)
        {
            videoPlayer.clip = activeClip;
            videoPlayer.isLooping = false;

            if (videoSurfaceRenderer != null) videoSurfaceRenderer.gameObject.SetActive(true);
            SetVideoAlpha(0f);

            videoPlayer.Prepare();
            while (!videoPlayer.isPrepared) yield return null;

            SetVideoAlpha(1f);
            videoPlayer.Play();
            Debug.Log("[StageIntro] 1단계 영상 재생 시작: " + activeClip.name);

            while (!_videoCompleted) yield return null;

            // --- 2단계: 영상 Fade Out + 커튼 투명도 1로 부드럽게 복원 ---
            currentState = IntroState.FadingOutVideo;
            Debug.Log("[StageIntro] 영상 종료 -> 영상 Fade Out 및 커튼 투명도 1로 복원");
            yield return FadeVideoOutAndRestoreCurtains(1f, videoFadeOutDuration);
            videoPlayer.Stop();
            if (videoSurfaceRenderer != null) videoSurfaceRenderer.gameObject.SetActive(false);
        }
        else
        {
            if (videoSurfaceRenderer != null)
            {
                videoSurfaceRenderer.gameObject.SetActive(true);
                SetVideoAlpha(0.25f);
            }
            Debug.Log("[StageIntro] Video Clip이 비어 있습니다. Inspector에 넣거나 Space 키로 다음 단계를 테스트하세요.");

            while (!_videoCompleted) yield return null;

            currentState = IntroState.FadingOutVideo;
            yield return FadeVideoOutAndRestoreCurtains(0.25f, Mathf.Min(0.8f, videoFadeOutDuration));
            if (videoSurfaceRenderer != null) videoSurfaceRenderer.gameObject.SetActive(false);
        }

        if (delayAfterVideoFade > 0f)
        {
            yield return new WaitForSeconds(delayAfterVideoFade);
        }

        // --- 3단계: FuneralGuide 1차 음성 재생 ---
        currentState = IntroState.PlayingGuideVoice;
        AudioClip firstVoice = guideVoiceClip != null ? guideVoiceClip : (guideAudioSource != null ? guideAudioSource.clip : null);
        if (guideAudioSource != null && firstVoice != null)
        {
            guideAudioSource.clip = firstVoice;
            guideAudioSource.loop = false;
            guideAudioSource.Play();
            Debug.Log("[StageIntro] 1차 안내 음성 재생 시작: " + firstVoice.name);

            while (guideAudioSource.isPlaying) yield return null;
            Debug.Log("[StageIntro] 1차 안내 음성 재생 완료");
        }
        else
        {
            Debug.Log("[StageIntro] 1차 Guide Voice Clip이 비어 있어 커튼 슬라이드 단계로 넘어갑니다.");
        }

        // --- 4단계: CurtainFront 우측 -> 좌측 슬라이드 대기 ---
        currentState = IntroState.WaitingCurtainSlide;
        _curtainOpened = false;
        if (curtainSlide != null)
        {
            curtainSlide.EnableInteraction(true);
        }
        while (!_curtainOpened) yield return null;

        // --- 5단계: 커튼을 열고 들어가 관 앞에 설 때까지 대기 ---
        currentState = IntroState.WaitingVisitorAtCoffin;
        Debug.Log("[StageIntro] 커튼이 열렸습니다. 관 앞으로 걸어 들어가세요.");
        while (!IsVisitorInFrontOfCoffin())
        {
            yield return null;
        }
        Debug.Log("[StageIntro] 참여자가 관 앞에 도착했습니다.");
        onVisitorArrivedAtCoffin?.Invoke();

        // 관 앞에 선 뒤 잠시 대기 후 2차 음성 재생
        if (delayBeforeOfferingVoice > 0f)
        {
            yield return new WaitForSeconds(delayBeforeOfferingVoice);
        }

        // --- 6단계: FuneralGuide 2차 음성 재생 (헌화 안내) ---
        currentState = IntroState.PlayingOfferingVoice;
        if (guideAnimator != null)
        {
            guideAnimator.SetPose(FuneralGuideAnimator.PoseState.PointToCoffin);
        }

        if (guideAudioSource != null && guideOfferingVoiceClip != null)
        {
            guideAudioSource.clip = guideOfferingVoiceClip;
            guideAudioSource.loop = false;
            guideAudioSource.Play();
            Debug.Log("[StageIntro] 2차 헌화 안내 음성 재생 시작: " + guideOfferingVoiceClip.name);

            while (guideAudioSource.isPlaying) yield return null;
            Debug.Log("[StageIntro] 2차 헌화 안내 음성 재생 완료");
        }
        else
        {
            Debug.Log("[StageIntro] 2차 Guide Offering Voice Clip이 비어 있어 바로 국화 헌화 단계로 넘어갑니다.");
        }

        // --- 7단계: Chrysanthemum(국화) 들기 -> 관 위에 내려놓기 ---
        currentState = IntroState.WaitingFlowerOffering;
        _flowerOffered = false;
        if (chrysanthemum != null)
        {
            chrysanthemum.EnableInteraction(true);
        }
        while (!_flowerOffered) yield return null;

        if (chrysanthemum != null)
        {
            chrysanthemum.EnableInteraction(false);
        }
        currentState = IntroState.FlowerOffered;
        Debug.Log("[StageIntro] 헌화가 완료되었습니다!");
        onFlowerOffered?.Invoke();

        // --- 8단계: 헌화 후 잠시 대기 -> 화면 Fade Out ---
        if (guideAnimator != null)
        {
            guideAnimator.SetPose(FuneralGuideAnimator.PoseState.Bow);
        }
        if (delayBeforeScreenFadeOut > 0f)
        {
            yield return new WaitForSeconds(delayBeforeScreenFadeOut);
        }

        currentState = IntroState.FadingOutScreen;
        Debug.Log("[StageIntro] 화면 Fade Out 시작");

        if (screenFader == null && _visitorHead != null)
        {
            screenFader = _visitorHead.GetComponent<VRScreenFader>();
            if (screenFader == null) screenFader = _visitorHead.gameObject.AddComponent<VRScreenFader>();
        }

        if (screenFader != null)
        {
            yield return screenFader.FadeOut(screenFadeOutDuration);
        }

        currentState = IntroState.Finished;
        Debug.Log("[StageIntro] 화면 Fade Out 완료 (체험 종료)");
        onScreenFadedOut?.Invoke();
    }

    private bool IsVisitorInFrontOfCoffin()
    {
        if (_visitorHead == null) return false;

        Vector3 headPos = _visitorHead.position;
        if (headPos.z >= entryZThreshold) return true;

        if (coffinRoot != null)
        {
            Vector2 hXZ = new Vector2(headPos.x, headPos.z);
            Vector2 cXZ = new Vector2(coffinRoot.position.x, coffinRoot.position.z);
            if (Vector2.Distance(hXZ, cXZ) <= coffinApproachDistance) return true;
        }

        return false;
    }

    private void OnVideoFinished(VideoPlayer vp) => _videoCompleted = true;

    private void HandleCurtainOpened()
    {
        if (_curtainOpened) return;
        _curtainOpened = true;
        onCurtainOpened?.Invoke();
    }

    private void HandleFlowerPlaced()
    {
        _flowerOffered = true;
    }

    private IEnumerator FadeVideoOutAndRestoreCurtains(float fromVideoAlpha, float duration)
    {
        int count = curtainsToRestoreOpacity != null ? curtainsToRestoreOpacity.Length : 0;
        float[] startOpacities = new float[count];
        for (int i = 0; i < count; i++)
        {
            startOpacities[i] = curtainsToRestoreOpacity[i] != null ? curtainsToRestoreOpacity[i].Opacity : 1f;
        }

        if (duration <= 0.01f)
        {
            SetVideoAlpha(0f);
            for (int i = 0; i < count; i++)
            {
                if (curtainsToRestoreOpacity[i] != null) curtainsToRestoreOpacity[i].Opacity = 1f;
            }
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetVideoAlpha(Mathf.Lerp(fromVideoAlpha, 0f, t));
            for (int i = 0; i < count; i++)
            {
                if (curtainsToRestoreOpacity[i] != null)
                {
                    curtainsToRestoreOpacity[i].Opacity = Mathf.Lerp(startOpacities[i], 1f, t);
                }
            }
            yield return null;
        }

        SetVideoAlpha(0f);
        for (int i = 0; i < count; i++)
        {
            if (curtainsToRestoreOpacity[i] != null) curtainsToRestoreOpacity[i].Opacity = 1f;
        }
    }

    private void SetVideoAlpha(float alpha)
    {
        if (_videoMat == null) return;
        if (_videoMat.HasProperty("_BaseColor"))
        {
            Color c = _videoMat.GetColor("_BaseColor");
            c.a = alpha;
            _videoMat.SetColor("_BaseColor", c);
        }
        else if (_videoMat.HasProperty("_Color"))
        {
            Color c = _videoMat.GetColor("_Color");
            c.a = alpha;
            _videoMat.SetColor("_Color", c);
        }
    }
}
