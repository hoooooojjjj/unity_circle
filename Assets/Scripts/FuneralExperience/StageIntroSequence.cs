using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Video;

/// <summary>
/// 확정 체험 순차 진행 컨트롤러 (장례지도사 4단계 멘트 기준):
/// 1. 공간 입장: 시작 즉시 VideoSurface 숨김 + 멘트 1 재생 -> 재생 종료 후 3초 대기
/// 2. 영상 관람: CurtainFront 뒤 VideoSurface 페이드인·재생 -> 종료 후 페이드아웃 & 커튼 불투명도 1 복원
/// 3. 고인과의 대면: 멘트 2 재생 -> 물리 커튼 걷기 -> 커튼 통과 후 관 앞 도착
/// 4. 헌화: 관 앞 도착 후 멘트 3 재생(PointToCoffin) -> 국화 집기 & 관 위 물리 헌화
/// 5. 마지막 인사: 헌화 완료 후 멘트 4 재생(Bow) -> 멘트 종료 후 5초 대기 -> 화면 Fade Out
/// 6. 마지막 시퀀스: 암전 중 관 & CurtainBack 제거 + 관 앞 정면 점프샷 -> Fade In -> 정면 엔딩 영상 재생
/// </summary>
public class StageIntroSequence : MonoBehaviour
{
    public enum IntroState
    {
        WaitingBeforeEntryVoice,
        PlayingEntryWaitVoice,
        WaitingAfterEntryVoice,
        PlayingVideo,
        FadingOutVideo,
        PlayingGuideVoice,
        WaitingCurtainSlide,
        WaitingVisitorAtCoffin,
        PlayingOfferingVoice,
        WaitingFlowerOffering,
        FlowerOffered,
        PlayingFarewellVoice,
        WaitingBeforeFadeOut,
        FadingOutScreen,
        PostOfferingJumpCut,
        FadingInAfterJumpCut,
        PlayingEndingVideo,
        Finished
    }

    [Header("1. 공간 입장 (멘트 1: 잠시 자리에서 대기해주십시오...)")]
    [Tooltip("시작 후 멘트 1이 나오기 전 정지 대기 시간(초)")]
    public float delayBeforeEntryWaitVoice = 5.0f;
    [Tooltip("정지 대기 후 VideoSurface가 숨겨진 상태에서 재생될 1번 입장 대기 멘트")]
    public AudioClip entryWaitVoiceClip;
    [Tooltip("멘트 1 재생 종료 후 영상 관람으로 넘어가기 전 대기 시간(초)")]
    public float delayAfterEntryWaitVoice = 3.0f;

    [Header("2. 영상 관람 (멘트 1 종료 3초 후 시작)")]
    [Tooltip("여기에 영상 파일을 넣거나 VideoSurface의 VideoPlayer > Video Clip에 넣으면 자동 재생됩니다.")]
    public VideoClip videoClip;
    public VideoPlayer videoPlayer;
    public Renderer videoSurfaceRenderer;
    public float introVideoFadeInDuration = 0.6f;
    public float videoFadeOutDuration = 1.5f;
    [Tooltip("영상이 Fade Out될 때 투명도를 1로 복원할 커튼들 (비우면 씬의 모든 커튼 자동 적용)")]
    public ProceduralCurtainMesh[] curtainsToRestoreOpacity;

    [Header("3. 고인과의 대면 (멘트 2: 커튼을 열고 관 앞에 서서...) & 커튼 걷기")]
    [Tooltip("영상 Fade Out 후 재생될 2번 커튼/대면 안내 멘트")]
    public AudioClip guideVoiceClip;
    public AudioSource guideAudioSource;
    public FuneralGuideAnimator guideAnimator;
    public float delayAfterVideoFade = 0.3f;
    public CurtainSlideInteractable curtainSlide;
    public Transform coffinRoot;
    [Tooltip("커튼(z=1.09)을 통과한 뒤 이 Z 좌표를 넘어가면 관 앞 도착으로 판정")]
    public float entryZThreshold = 1.38f;
    [Tooltip("관 중심으로부터 수평 거리(m) 이내에 서면 관 앞 도착으로 판정")]
    public float coffinApproachDistance = 1.05f;

    [Header("4. 헌화 (멘트 3: 준비된 꽃을 고인의 곁에 놓아 주세요.)")]
    [Tooltip("관 앞에 선 뒤 3번 헌화 멘트가 나오기 전 대기 시간(초)")]
    public float delayBeforeOfferingVoice = 1.5f;
    [Tooltip("관 앞에 섰을 때 재생될 3번 헌화 안내 멘트")]
    public AudioClip guideOfferingVoiceClip;
    public FlowerInteractable chrysanthemum;

    [Header("5. 마지막 인사 (멘트 4: 고인에게 마지막 인사를 건네 주세요.) & 5초 후 Fade Out")]
    [Tooltip("헌화 완료 후 4번 마지막 인사 멘트가 나오기 전 짧은 여운(초)")]
    public float delayBeforeFarewellVoice = 1.0f;
    [Tooltip("헌화 완료 후 재생될 4번 마지막 인사 멘트")]
    public AudioClip farewellVoiceClip;
    [Tooltip("4번 마지막 인사 멘트 종료 후 화면 암전 전 대기 시간(초)")]
    public float delayBeforeScreenFadeOut = 5.0f;
    [Tooltip("화면이 완전히 어두워지는 데 걸리는 시간(초)")]
    public float screenFadeOutDuration = 2.5f;
    public VRScreenFader screenFader;

    [Header("6. 암전 후 점프샷 재개 (관 & CurtainBack 제거 + 정면 영상)")]
    [Tooltip("완전 암전 후 점프샷 전환 및 Fade In 전 짧은 정적 대기 시간(초)")]
    public float delayBeforePostOfferingFadeIn = 0.8f;
    [Tooltip("점프샷 후 화면이 다시 밝아지는 시간(초)")]
    public float postOfferingFadeInDuration = 1.8f;
    [Tooltip("점프샷으로 서게 될 위치·정면 방향 (기존 관 바로 앞, 정면 +Z)")]
    public Transform postOfferingStandPoint;
    [Tooltip("점프샷 시 사라질 뒤쪽 커튼 (CurtainBack)")]
    public GameObject curtainBack;
    [Tooltip("점프샷 시 관·CurtainBack 외 추가로 숨길 오브젝트 (선택)")]
    public GameObject[] additionalObjectsToHide;
    [Tooltip("Fade In 후 정면에 엔딩 영상이 뜨기 전 대기 시간(초)")]
    public float delayBeforeEndingVideo = 0.35f;
    [Tooltip("정면에 뜰 엔딩 영상 클립 (비워두면 EndingVideoSurface의 VideoPlayer > Video Clip 사용)")]
    public VideoClip endingVideoClip;
    public VideoPlayer endingVideoPlayer;
    public Renderer endingVideoSurfaceRenderer;
    public float endingVideoFadeInDuration = 1.2f;
    public bool loopEndingVideo = false;

    [Header("진행 상태 (읽기 전용 확인용)")]
    public IntroState currentState = IntroState.WaitingBeforeEntryVoice;

    [Header("이벤트 (다음 단계 확장용)")]
    public UnityEvent onCurtainOpened;
    public UnityEvent onVisitorArrivedAtCoffin;
    public UnityEvent onFlowerOffered;
    public UnityEvent onScreenFadedOut;
    public UnityEvent onPostOfferingJumpCut;
    public UnityEvent onEndingVideoFinished;

    private Material _videoMat;
    private Material _endingVideoMat;
    private bool _videoCompleted;
    private bool _endingVideoCompleted;
    private bool _curtainOpened;
    private bool _flowerOffered;
    private bool _skipCurrentWait;
    private Transform _visitorHead;
    private VRSpawnAligner _spawnAligner;

    private void Awake()
    {
        // 인트로 VideoSurface는 처음부터 첫 장면(정지 프레임)을 보여주기 위해 활성 유지,
        // 엔딩 EndingVideoSurface만 숨김 상태로 초기화
        if (videoSurfaceRenderer != null)
        {
            _videoMat = videoSurfaceRenderer.material;
            videoSurfaceRenderer.gameObject.SetActive(true);
            SetMaterialAlpha(_videoMat, 1f);
        }
        if (endingVideoSurfaceRenderer != null)
        {
            _endingVideoMat = endingVideoSurfaceRenderer.material;
            SetMaterialAlpha(_endingVideoMat, 0f);
            endingVideoSurfaceRenderer.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null)
        {
            if (rig.centerEyeAnchor != null) _visitorHead = rig.centerEyeAnchor;
            _spawnAligner = rig.GetComponent<VRSpawnAligner>();
        }
        if (_visitorHead == null && Camera.main != null) _visitorHead = Camera.main.transform;
        if (_spawnAligner == null) _spawnAligner = FindFirstObjectByType<VRSpawnAligner>();

        if (curtainBack == null)
        {
            GameObject cb = GameObject.Find("CurtainBack");
            if (cb != null) curtainBack = cb;
        }

        if (curtainsToRestoreOpacity == null || curtainsToRestoreOpacity.Length == 0)
        {
            curtainsToRestoreOpacity = FindObjectsByType<ProceduralCurtainMesh>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        if (videoPlayer != null) videoPlayer.loopPointReached += OnVideoFinished;
        if (endingVideoPlayer != null) endingVideoPlayer.loopPointReached += OnEndingVideoFinished;
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
        if (endingVideoPlayer != null) endingVideoPlayer.loopPointReached -= OnEndingVideoFinished;
        if (curtainSlide != null) curtainSlide.OnCurtainOpened -= HandleCurtainOpened;
        if (chrysanthemum != null) chrysanthemum.OnFlowerPlaced -= HandleFlowerPlaced;
    }

    private void Update()
    {
#if UNITY_EDITOR
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // 에디터 테스트 편의: Space로 현재 영상/음성/대기 스킵
        if (kb.spaceKey.wasPressedThisFrame)
        {
            if (currentState == IntroState.PlayingVideo)
            {
                _videoCompleted = true;
            }
            else if (currentState == IntroState.PlayingEndingVideo)
            {
                _endingVideoCompleted = true;
            }
            else if (currentState == IntroState.WaitingBeforeEntryVoice
                     || currentState == IntroState.WaitingAfterEntryVoice
                     || currentState == IntroState.WaitingBeforeFadeOut)
            {
                _skipCurrentWait = true;
            }
            else if ((currentState == IntroState.PlayingEntryWaitVoice
                      || currentState == IntroState.PlayingGuideVoice
                      || currentState == IntroState.PlayingOfferingVoice
                      || currentState == IntroState.PlayingFarewellVoice)
                     && guideAudioSource != null && guideAudioSource.isPlaying)
            {
                guideAudioSource.Stop();
            }
        }

        // 에디터 테스트 편의: 3번 헌화 멘트 종료 후 F키로 국화 잡기 -> 다시 F키로 관에 헌화
        if (kb.fKey.wasPressedThisFrame && currentState == IntroState.WaitingFlowerOffering && chrysanthemum != null)
        {
            chrysanthemum.DesktopToggleGrabOrPlace();
        }

        // 에디터 테스트 편의: J키로 암전 -> 점프샷 -> 정면 엔딩 영상 구간 바로 테스트
        if (kb.jKey.wasPressedThisFrame && currentState < IntroState.FadingOutScreen)
        {
            StopAllCoroutines();
            if (videoPlayer != null && videoPlayer.isPlaying) videoPlayer.Stop();
            if (videoSurfaceRenderer != null) videoSurfaceRenderer.gameObject.SetActive(false);
            if (guideAudioSource != null && guideAudioSource.isPlaying) guideAudioSource.Stop();
            if (curtainSlide != null) curtainSlide.EnableInteraction(false);
            if (chrysanthemum != null) chrysanthemum.EnableInteraction(false);
            Debug.Log("[StageIntro] (에디터 J키) 암전 -> 점프샷 -> 정면 엔딩 영상 구간으로 즉시 점프합니다.");
            StartCoroutine(RunPostOfferingJumpCutSequence());
        }
#endif
    }

    private IEnumerator RunSequence()
    {
        // --- 0단계: 시작 즉시 인트로 영상 첫 장면(0프레임)을 정지 상태로 표시 ---
        VideoClip activeClip = videoClip != null ? videoClip : (videoPlayer != null ? videoPlayer.clip : null);
        if (videoPlayer != null && activeClip != null)
        {
            videoPlayer.clip = activeClip;
            videoPlayer.isLooping = false;
            if (videoSurfaceRenderer != null)
            {
                videoSurfaceRenderer.gameObject.SetActive(true);
                SetVideoAlpha(1f);
            }

            videoPlayer.Prepare();
            float prepTimeout = 0f;
            while (!videoPlayer.isPrepared && prepTimeout < 3f)
            {
                prepTimeout += Time.unscaledDeltaTime;
                yield return null;
            }

            // 첫 프레임이 RenderTexture(SilhouetteRT)에 확실히 그려지도록 1프레임 디코딩 후 정지(Pause)
            bool prevMute = videoPlayer.GetDirectAudioMute(0);
            videoPlayer.SetDirectAudioMute(0, true);
            videoPlayer.frame = 0;
            videoPlayer.Play();
            float frameWait = 0f;
            while (videoPlayer.frame < 0 && frameWait < 0.35f)
            {
                frameWait += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;
            videoPlayer.Pause();
            videoPlayer.SetDirectAudioMute(0, prevMute);
            Debug.Log("[StageIntro] 인트로 영상 첫 장면(정지 상태) 표시 완료: " + activeClip.name);
        }
        else if (videoSurfaceRenderer != null)
        {
            videoSurfaceRenderer.gameObject.SetActive(true);
            SetVideoAlpha(0.25f);
        }

        // --- 1단계: 공간 입장 (첫 장면 유지 + n초 정지 대기 -> 멘트 1 재생 -> 종료 후 3초 대기) ---
        currentState = IntroState.WaitingBeforeEntryVoice;
        yield return WaitSecondsSkippable(delayBeforeEntryWaitVoice);

        currentState = IntroState.PlayingEntryWaitVoice;
        yield return PlayGuideVoice(entryWaitVoiceClip, "1번 공간 입장 대기 멘트");

        currentState = IntroState.WaitingAfterEntryVoice;
        yield return WaitSecondsSkippable(delayAfterEntryWaitVoice);

        // --- 2단계: 인트로 영상 재생 (정지돼 있던 첫 장면에서 그대로 재생 시작 -> 종료 후 페이드아웃 & 커튼 복원) ---
        currentState = IntroState.PlayingVideo;
        _videoCompleted = false;

        if (videoPlayer != null && activeClip != null)
        {
            SetVideoAlpha(1f);
            videoPlayer.Play();
            Debug.Log("[StageIntro] 인트로 영상 재생 시작: " + activeClip.name);

            while (!_videoCompleted) yield return null;

            currentState = IntroState.FadingOutVideo;
            Debug.Log("[StageIntro] 영상 종료 -> 영상 Fade Out 및 커튼 불투명도 1로 복원");
            yield return FadeVideoOutAndRestoreCurtains(1f, videoFadeOutDuration);
            videoPlayer.Stop();
            if (videoSurfaceRenderer != null) videoSurfaceRenderer.gameObject.SetActive(false);
        }
        else
        {
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

        // --- 3단계: 고인과의 대면 (멘트 2 재생 -> 물리 커튼 걷기 -> 관 앞 도착) ---
        currentState = IntroState.PlayingGuideVoice;
        AudioClip curtainMent = guideVoiceClip != null ? guideVoiceClip : (guideAudioSource != null ? guideAudioSource.clip : null);
        yield return PlayGuideVoice(curtainMent, "2번 고인과의 대면(커튼 열기) 멘트");

        currentState = IntroState.WaitingCurtainSlide;
        _curtainOpened = false;
        if (curtainSlide != null)
        {
            curtainSlide.EnableInteraction(true);
        }
        while (!_curtainOpened) yield return null;

        currentState = IntroState.WaitingVisitorAtCoffin;
        Debug.Log("[StageIntro] 커튼이 열렸습니다. 관 앞으로 걸어 들어가세요.");
        while (!IsVisitorInFrontOfCoffin())
        {
            yield return null;
        }
        Debug.Log("[StageIntro] 참여자가 관 앞에 도착했습니다.");
        onVisitorArrivedAtCoffin?.Invoke();

        // --- 4단계: 헌화 (여운 대기 -> 멘트 3 재생 + PointToCoffin -> 국화 집어 관 위 헌화) ---
        if (delayBeforeOfferingVoice > 0f)
        {
            yield return new WaitForSeconds(delayBeforeOfferingVoice);
        }

        currentState = IntroState.PlayingOfferingVoice;
        if (guideAnimator != null)
        {
            guideAnimator.SetPose(FuneralGuideAnimator.PoseState.PointToCoffin);
        }
        yield return PlayGuideVoice(guideOfferingVoiceClip, "3번 헌화 안내 멘트");

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

        // --- 5단계: 마지막 인사 (멘트 4 재생 + Bow -> 멘트 종료 후 5초 대기 -> Fade Out) ---
        if (delayBeforeFarewellVoice > 0f)
        {
            yield return new WaitForSeconds(delayBeforeFarewellVoice);
        }

        currentState = IntroState.PlayingFarewellVoice;
        if (guideAnimator != null)
        {
            guideAnimator.SetPose(FuneralGuideAnimator.PoseState.Bow);
        }
        yield return PlayGuideVoice(farewellVoiceClip, "4번 마지막 인사 멘트");

        currentState = IntroState.WaitingBeforeFadeOut;
        Debug.Log("[StageIntro] 마지막 인사 멘트 완료 -> " + delayBeforeScreenFadeOut.ToString("F1") + "초 후 화면 Fade Out 시작");
        yield return WaitSecondsSkippable(delayBeforeScreenFadeOut);

        // --- 6단계: 암전 -> 점프샷(관 & CurtainBack 제거) -> Fade In -> 정면 엔딩 영상 ---
        yield return RunPostOfferingJumpCutSequence();
    }

    private IEnumerator PlayGuideVoice(AudioClip clip, string label)
    {
        if (guideAudioSource != null && clip != null)
        {
            guideAudioSource.clip = clip;
            guideAudioSource.loop = false;
            guideAudioSource.Play();
            Debug.Log("[StageIntro] " + label + " 재생 시작: " + clip.name);

            while (guideAudioSource.isPlaying) yield return null;
            Debug.Log("[StageIntro] " + label + " 재생 완료");
        }
        else
        {
            Debug.Log("[StageIntro] " + label + " 클립이 비어 있어 다음 단계로 진행합니다.");
        }
    }

    private IEnumerator WaitSecondsSkippable(float seconds)
    {
        if (seconds <= 0f) yield break;
        _skipCurrentWait = false;
        float elapsed = 0f;
        while (elapsed < seconds && !_skipCurrentWait)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        _skipCurrentWait = false;
    }

    private IEnumerator RunPostOfferingJumpCutSequence()
    {
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

        Debug.Log("[StageIntro] 화면 Fade Out 완료 -> 암전 중 점프샷 전환 준비");
        onScreenFadedOut?.Invoke();

        // 완전 암전 상태에서 관 & CurtainBack 제거 + 관 바로 앞 정면으로 점프샷 정렬
        currentState = IntroState.PostOfferingJumpCut;
        if (delayBeforePostOfferingFadeIn > 0f)
        {
            yield return new WaitForSeconds(delayBeforePostOfferingFadeIn);
        }

        if (coffinRoot != null)
        {
            coffinRoot.gameObject.SetActive(false);
        }
        if (curtainBack != null)
        {
            curtainBack.SetActive(false);
        }
        if (additionalObjectsToHide != null)
        {
            for (int i = 0; i < additionalObjectsToHide.Length; i++)
            {
                if (additionalObjectsToHide[i] != null) additionalObjectsToHide[i].SetActive(false);
            }
        }

        if (_spawnAligner != null && postOfferingStandPoint != null)
        {
            _spawnAligner.AlignToTransform(postOfferingStandPoint, _visitorHead);
        }

        VideoClip activeEndingClip = endingVideoClip != null ? endingVideoClip : (endingVideoPlayer != null ? endingVideoPlayer.clip : null);
        if (endingVideoPlayer != null && activeEndingClip != null)
        {
            endingVideoPlayer.clip = activeEndingClip;
            endingVideoPlayer.isLooping = loopEndingVideo;
            if (endingVideoSurfaceRenderer != null)
            {
                endingVideoSurfaceRenderer.gameObject.SetActive(true);
                SetMaterialAlpha(_endingVideoMat, 0f);
            }
            endingVideoPlayer.Prepare();
            float prepWait = 0f;
            while (!endingVideoPlayer.isPrepared && prepWait < 3f)
            {
                prepWait += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        onPostOfferingJumpCut?.Invoke();

        currentState = IntroState.FadingInAfterJumpCut;
        Debug.Log("[StageIntro] 점프샷 완료 -> 화면 Fade In 시작");
        if (screenFader != null)
        {
            yield return screenFader.FadeIn(postOfferingFadeInDuration);
        }

        if (delayBeforeEndingVideo > 0f)
        {
            yield return new WaitForSeconds(delayBeforeEndingVideo);
        }

        currentState = IntroState.PlayingEndingVideo;
        _endingVideoCompleted = false;

        if (endingVideoPlayer != null && activeEndingClip != null)
        {
            if (endingVideoSurfaceRenderer != null) endingVideoSurfaceRenderer.gameObject.SetActive(true);
            endingVideoPlayer.Play();
            Debug.Log("[StageIntro] 정면 엔딩 영상 재생 시작: " + activeEndingClip.name);
            yield return FadeMaterialAlpha(_endingVideoMat, 0f, 1f, endingVideoFadeInDuration);

            if (!loopEndingVideo)
            {
                while (!_endingVideoCompleted) yield return null;
            }
        }
        else if (endingVideoSurfaceRenderer != null)
        {
            endingVideoSurfaceRenderer.gameObject.SetActive(true);
            yield return FadeMaterialAlpha(_endingVideoMat, 0f, 0.35f, Mathf.Min(0.8f, endingVideoFadeInDuration));
            Debug.Log("[StageIntro] Ending Video Clip이 비어 있습니다. Inspector의 StageIntroSequence > Ending Video Clip 또는 EndingVideoSurface에 영상을 넣어주세요.");
        }

        currentState = IntroState.Finished;
        Debug.Log("[StageIntro] 정면 엔딩 영상 시퀀스 완료");
        onEndingVideoFinished?.Invoke();
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
    private void OnEndingVideoFinished(VideoPlayer vp) => _endingVideoCompleted = true;

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

    private IEnumerator FadeMaterialAlpha(Material mat, float fromAlpha, float toAlpha, float duration)
    {
        if (mat == null) yield break;
        if (duration <= 0.01f)
        {
            SetMaterialAlpha(mat, toAlpha);
            yield break;
        }
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            SetMaterialAlpha(mat, Mathf.Lerp(fromAlpha, toAlpha, eased));
            yield return null;
        }
        SetMaterialAlpha(mat, toAlpha);
    }

    private void SetVideoAlpha(float alpha)
    {
        SetMaterialAlpha(_videoMat, alpha);
    }

    private void SetMaterialAlpha(Material mat, float alpha)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }
        else if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            c.a = alpha;
            mat.SetColor("_Color", c);
        }
    }
}
