using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

/// <summary>
/// 하나의 무대 씬에서 커튼·영상·조명·관의 상태를 순서대로 제어하는 전체 체험 진행 컨트롤러.
/// 단계:
/// 0. Preparing       - 어두운 무대에서 준비
/// 1. CoffinReveal    - 레퍼런스 1: 관 조명 서서히 점등
/// 2. SilhouetteVideo - 레퍼런스 2: 닫힌 커튼 앞 실루엣 영상 재생 (뒤쪽 3D 조명 낮춤)
/// 3. CurtainOpen     - 레퍼런스 3(전반): 커튼 잡기 활성화 -> 좌우 열림 애니메이션 + 참여자 조명 점등
/// 4. FlowerOffering  - 레퍼런스 3(후반): 국화 잡기/헌화 완료 판정
/// 5. Departure       - 레퍼런스 4: 인사 -> 관뚜껑 닫힘 -> 관과 조명이 뒤로 멀어짐 -> 암전
/// </summary>
public class ExperienceController : MonoBehaviour
{
    public enum StagePhase
    {
        Preparing = 0,
        CoffinReveal = 1,
        SilhouetteVideo = 2,
        CurtainOpen = 3,
        FlowerOffering = 4,
        Departure = 5,
        Finished = 6
    }

    [Header("현재 단계")]
    public StagePhase currentPhase = StagePhase.Preparing;
    public bool autoStartOnPlay = true;

    [Header("무대 오브젝트")]
    public Transform coffinRoot;
    public Transform coffinLid;
    public Transform curtainLeft;
    public Transform curtainRight;
    public GameObject videoSurface;
    public VideoPlayer videoPlayer;
    public FuneralGuideAnimator funeralGuide;
    public FlowerInteractable chrysanthemum;
    public CurtainPullInteractable curtainHandleLeft;
    public CurtainPullInteractable curtainHandleRight;

    [Header("조명")]
    public Light coffinSpot;
    public Light visitorSpot;
    public Light guideRim;
    public Renderer volumetricBeamRenderer;

    [Header("오디오 (선택)")]
    public AudioSource musicSource;
    public AudioClip guideOfferClip;
    public AudioClip guideFarewellClip;

    [Header("조명 밝기 설정")]
    public float coffinSpotRevealIntensity = 78f;
    public float coffinSpotDimIntensity = 4f;
    public float visitorSpotActiveIntensity = 24f;
    public float guideRimIntensity = 12f;

    [Header("시간 & 애니메이션 설정")]
    public float revealDuration = 3.0f;
    public float fallbackVideoDuration = 6.0f;
    public float curtainOpenDuration = 2.5f;
    public float postOfferingPause = 3.0f;
    public float lidCloseDuration = 2.2f;
    public float departureDuration = 10.0f;
    public float departureDistance = 9.0f;

    [Header("커튼/뚜껑 위치 오프셋")]
    public float curtainSlideDistance = 1.0f;
    public Vector3 lidOpenOffset = new Vector3(0f, -0.02f, 0.95f);

    private Vector3 _coffinStartPos;
    private Vector3 _coffinSpotOffset;
    private Vector3 _initialLidLocalPos;
    private Vector3 _initialCurtainLeftPos;
    private Vector3 _initialCurtainRightPos;
    private Vector3 _curtainLeftBaseScale = Vector3.one;
    private Vector3 _curtainRightBaseScale = Vector3.one;
    private Material _videoMatInstance;
    private Material _beamMatInstance;
    private Coroutine _phaseRoutine;
    private bool _videoFinishedFlag;
    private bool _curtainPulledFlag;
    private bool _flowerPlacedFlag;
    private OVRCameraRig _rig;

    private void Awake()
    {
        if (coffinRoot != null) _coffinStartPos = coffinRoot.position;
        if (coffinSpot != null && coffinRoot != null)
            _coffinSpotOffset = coffinSpot.transform.position - coffinRoot.position;
        if (coffinLid != null) _initialLidLocalPos = coffinLid.localPosition;
        if (curtainLeft != null)
        {
            _initialCurtainLeftPos = curtainLeft.position;
            _curtainLeftBaseScale = curtainLeft.localScale;
        }
        if (curtainRight != null)
        {
            _initialCurtainRightPos = curtainRight.position;
            _curtainRightBaseScale = curtainRight.localScale;
        }

        if (videoSurface != null && videoSurface.TryGetComponent<Renderer>(out var vr))
        {
            _videoMatInstance = vr.material;
        }
        if (volumetricBeamRenderer != null)
        {
            _beamMatInstance = volumetricBeamRenderer.material;
        }
    }

    private void Start()
    {
        _rig = FindFirstObjectByType<OVRCameraRig>();

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached += OnVideoLoopPointReached;
        }
        if (curtainHandleLeft != null) curtainHandleLeft.OnCurtainPulled += OnCurtainPulled;
        if (curtainHandleRight != null) curtainHandleRight.OnCurtainPulled += OnCurtainPulled;
        if (chrysanthemum != null) chrysanthemum.OnFlowerPlaced += OnFlowerPlaced;

        ApplyInitialDarkState();

        if (autoStartOnPlay)
        {
            StartExperience();
        }
    }

    private void OnDestroy()
    {
        if (videoPlayer != null) videoPlayer.loopPointReached -= OnVideoLoopPointReached;
        if (curtainHandleLeft != null) curtainHandleLeft.OnCurtainPulled -= OnCurtainPulled;
        if (curtainHandleRight != null) curtainHandleRight.OnCurtainPulled -= OnCurtainPulled;
        if (chrysanthemum != null) chrysanthemum.OnFlowerPlaced -= OnFlowerPlaced;
    }

    private void Update()
    {
        // 관 조명은 항상 관의 위치를 추적
        if (coffinSpot != null && coffinRoot != null)
        {
            coffinSpot.transform.position = coffinRoot.position + _coffinSpotOffset;
        }

        HandleEditorTestShortcuts();
    }

    private void HandleEditorTestShortcuts()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.spaceKey.wasPressedThisFrame)
        {
            if (currentPhase == StagePhase.Preparing) StartExperience();
            else if (currentPhase == StagePhase.SilhouetteVideo) _videoFinishedFlag = true;
            else if (currentPhase == StagePhase.CurtainOpen) OnCurtainPulled();
            else if (currentPhase == StagePhase.FlowerOffering && chrysanthemum != null) chrysanthemum.CompletePlacement();
        }

        if (kb.eKey.wasPressedThisFrame && currentPhase == StagePhase.CurtainOpen)
        {
            if (curtainHandleLeft != null) curtainHandleLeft.TriggerPull();
            else OnCurtainPulled();
        }

        if (kb.fKey.wasPressedThisFrame && currentPhase == StagePhase.FlowerOffering && chrysanthemum != null)
        {
            chrysanthemum.DesktopToggleGrabOrPlace();
        }

        if (kb.digit1Key.wasPressedThisFrame) JumpToPhase(StagePhase.CoffinReveal);
        if (kb.digit2Key.wasPressedThisFrame) JumpToPhase(StagePhase.SilhouetteVideo);
        if (kb.digit3Key.wasPressedThisFrame) JumpToPhase(StagePhase.CurtainOpen);
        if (kb.digit4Key.wasPressedThisFrame) JumpToPhase(StagePhase.FlowerOffering);
        if (kb.digit5Key.wasPressedThisFrame) JumpToPhase(StagePhase.Departure);
    }

    public void StartExperience()
    {
        if (_phaseRoutine != null) StopCoroutine(_phaseRoutine);
        _phaseRoutine = StartCoroutine(RunFullExperience());
    }

    public void JumpToPhase(StagePhase phase)
    {
        if (_phaseRoutine != null) StopCoroutine(_phaseRoutine);
        _phaseRoutine = StartCoroutine(RunFromPhase(phase));
    }

    private void ApplyInitialDarkState()
    {
        currentPhase = StagePhase.Preparing;
        SetLightIntensity(coffinSpot, 0f);
        SetLightIntensity(visitorSpot, 0f);
        SetLightIntensity(guideRim, 0f);
        SetBeamAlpha(0f);
        SetVideoAlpha(0f);

        if (videoSurface != null) videoSurface.SetActive(false);
        if (coffinRoot != null) coffinRoot.position = _coffinStartPos;
        if (coffinLid != null) coffinLid.localPosition = _initialLidLocalPos;
        if (curtainLeft != null)
        {
            curtainLeft.position = _initialCurtainLeftPos;
            curtainLeft.localScale = _curtainLeftBaseScale;
        }
        if (curtainRight != null)
        {
            curtainRight.position = _initialCurtainRightPos;
            curtainRight.localScale = _curtainRightBaseScale;
        }

        if (curtainHandleLeft != null) { curtainHandleLeft.ResetInteractable(); curtainHandleLeft.interactionEnabled = false; }
        if (curtainHandleRight != null) { curtainHandleRight.ResetInteractable(); curtainHandleRight.interactionEnabled = false; }
        if (chrysanthemum != null) { chrysanthemum.ResetFlower(); chrysanthemum.interactionEnabled = false; }
        if (funeralGuide != null) funeralGuide.SetPose(FuneralGuideAnimator.PoseState.Idle);
    }

    private IEnumerator RunFullExperience()
    {
        yield return RunFromPhase(StagePhase.CoffinReveal);
    }

    private IEnumerator RunFromPhase(StagePhase startPhase)
    {
        ApplyInitialDarkState();

        // 1단계: 관 조명 점등 (레퍼런스 1)
        if (startPhase <= StagePhase.CoffinReveal)
        {
            currentPhase = StagePhase.CoffinReveal;
            Debug.Log("[Experience] 1단계: 관 조명 점등 (CoffinReveal)");

            // 영상 준비 병행
            if (videoPlayer != null && videoPlayer.clip != null)
            {
                videoPlayer.Prepare();
            }

            // 현재 씬에 배치된 오브젝트 위치 그대로에서 조명만 서서히 점등
            yield return FadeLights(0f, coffinSpotRevealIntensity, 0f, visitorSpotActiveIntensity * 0.7f, 0f, guideRimIntensity, 0f, 0.65f, revealDuration);
            yield return new WaitForSeconds(2.0f);
        }

        // 2단계: 실루엣 영상 재생 (레퍼런스 2)
        if (startPhase <= StagePhase.SilhouetteVideo)
        {
            currentPhase = StagePhase.SilhouetteVideo;
            Debug.Log("[Experience] 2단계: 실루엣 영상 재생 (SilhouetteVideo)");
            _videoFinishedFlag = false;

            // 커튼 닫힘 상태 + 뒤쪽 3D 관 조명 낮춤 (영상과 3D 관이 동시에 겹치지 않게)
            SetCurtainsImmediate(0f);
            yield return FadeLights(
                coffinSpot != null ? coffinSpot.intensity : 0f, coffinSpotDimIntensity,
                visitorSpot != null ? visitorSpot.intensity : 0f, 0f,
                guideRim != null ? guideRim.intensity : guideRimIntensity, guideRimIntensity * 0.6f,
                0.65f, 0.1f, 1.2f);

            if (videoSurface != null) videoSurface.SetActive(true);

            if (videoPlayer != null && videoPlayer.clip != null)
            {
                if (!videoPlayer.isPrepared)
                {
                    videoPlayer.Prepare();
                    while (!videoPlayer.isPrepared) yield return null;
                }
                videoPlayer.Play();
                yield return FadeVideoAlpha(0f, 1f, 1.0f);
                while (!_videoFinishedFlag) yield return null;
            }
            else
            {
                // 영상 클립 미할당 시 프로토타입 대기 타이머 (Space로 즉시 스킵 가능)
                yield return FadeVideoAlpha(0f, 0.85f, 0.8f);
                float elapsed = 0f;
                while (!_videoFinishedFlag && elapsed < fallbackVideoDuration)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            // 영상 종료 -> 영상 페이드아웃 + 뒤쪽 3D 관 조명 서서히 복원
            yield return FadeVideoAlpha(1f, 0f, 1.2f);
            if (videoSurface != null) videoSurface.SetActive(false);
            yield return FadeLights(coffinSpotDimIntensity, coffinSpotRevealIntensity, 0f, visitorSpotActiveIntensity * 0.5f, guideRimIntensity * 0.6f, guideRimIntensity, 0.1f, 0.55f, 1.5f);
        }
        else
        {
            // 3단계 이후로 바로 점프한 경우 기본 조명 세팅
            SetLightIntensity(coffinSpot, coffinSpotRevealIntensity);
            SetLightIntensity(guideRim, guideRimIntensity);
            SetBeamAlpha(0.55f);
        }

        // 3단계: 커튼 열기 (레퍼런스 3 전반)
        if (startPhase <= StagePhase.CurtainOpen)
        {
            currentPhase = StagePhase.CurtainOpen;
            Debug.Log("[Experience] 3단계: 커튼 열기 대기 (VR 컨트롤러로 20cm 당기기 / 에디터 E 또는 Space)");
            _curtainPulledFlag = false;
            SetCurtainsImmediate(0f);

            if (curtainHandleLeft != null) { curtainHandleLeft.ResetInteractable(); curtainHandleLeft.interactionEnabled = true; }
            if (curtainHandleRight != null) { curtainHandleRight.ResetInteractable(); curtainHandleRight.interactionEnabled = true; }

            while (!_curtainPulledFlag) yield return null;

            if (curtainHandleLeft != null) curtainHandleLeft.interactionEnabled = false;
            if (curtainHandleRight != null) curtainHandleRight.interactionEnabled = false;

            // 좌우 커튼 열림 애니메이션 + 참여자 조명 활성화
            yield return AnimateCurtains(0f, 1f, curtainOpenDuration);
            yield return FadeLights(coffinSpotRevealIntensity, coffinSpotRevealIntensity, visitorSpot != null ? visitorSpot.intensity : 0f, visitorSpotActiveIntensity, guideRimIntensity, guideRimIntensity, 0.55f, 0.65f, 1.0f);
        }
        else
        {
            SetCurtainsImmediate(1f);
            SetLightIntensity(visitorSpot, visitorSpotActiveIntensity);
        }

        // 4단계: 헌화 (레퍼런스 3 후반)
        if (startPhase <= StagePhase.FlowerOffering)
        {
            currentPhase = StagePhase.FlowerOffering;
            Debug.Log("[Experience] 4단계: 국화 헌화 대기 (VR 잡아서 관 안에 놓기 / 에디터 F 두 번 또는 Space)");
            _flowerPlacedFlag = false;

            // 헌화 시 관뚜껑 열기
            if (coffinLid != null)
            {
                coffinLid.localPosition = _initialLidLocalPos + lidOpenOffset;
            }

            if (funeralGuide != null)
            {
                funeralGuide.SetPose(FuneralGuideAnimator.PoseState.PointToCoffin);
                funeralGuide.PlayVoiceClip(guideOfferClip);
            }

            if (chrysanthemum != null)
            {
                chrysanthemum.ResetFlower();
                chrysanthemum.interactionEnabled = true;
            }

            while (!_flowerPlacedFlag) yield return null;

            if (chrysanthemum != null) chrysanthemum.interactionEnabled = false;

            // 헌화 완료 후 잠시 인사할 시간 + 손이 관 주변에서 벗어나길 대기
            yield return new WaitForSeconds(postOfferingPause);
            yield return WaitUntilHandsClearOfCoffin(2.5f);
        }

        // 5단계: 관 닫기 및 출발 (레퍼런스 4)
        currentPhase = StagePhase.Departure;
        Debug.Log("[Experience] 5단계: 관 뚜껑 닫기 및 출발 (Departure)");

        if (funeralGuide != null)
        {
            funeralGuide.SetPose(FuneralGuideAnimator.PoseState.Bow);
            funeralGuide.PlayVoiceClip(guideFarewellClip);
        }

        // 관뚜껑 닫기 애니메이션
        if (coffinLid != null)
        {
            Vector3 fromPos = coffinLid.localPosition;
            float t = 0f;
            while (t < lidCloseDuration)
            {
                t += Time.deltaTime;
                float s = Mathf.SmoothStep(0f, 1f, t / lidCloseDuration);
                coffinLid.localPosition = Vector3.Lerp(fromPos, _initialLidLocalPos, s);
                yield return null;
            }
            coffinLid.localPosition = _initialLidLocalPos;
        }

        // 출발: 음악 서서히 들어옴 + 참여자 조명 낮아짐 + 관과 관 조명이 함께 무대 뒤쪽으로 이동
        if (musicSource != null && musicSource.clip != null)
        {
            musicSource.volume = 0f;
            musicSource.Play();
        }

        Vector3 startPos = coffinRoot != null ? coffinRoot.position : Vector3.zero;
        Vector3 endPos = startPos + Vector3.forward * departureDistance;
        float startVisitor = visitorSpot != null ? visitorSpot.intensity : 0f;
        float startGuide = guideRim != null ? guideRim.intensity : 0f;

        float elapsedDep = 0f;
        while (elapsedDep < departureDuration)
        {
            elapsedDep += Time.deltaTime;
            float u = Mathf.Clamp01(elapsedDep / departureDuration);
            float moveEase = u * u * (3f - 2f * u);

            if (coffinRoot != null)
            {
                coffinRoot.position = Vector3.Lerp(startPos, endPos, moveEase);
            }

            // 참여자 조명과 지도사 조명은 서서히 어두워져 시선이 떠나는 관에 머무르게 함
            SetLightIntensity(visitorSpot, Mathf.Lerp(startVisitor, 0f, Mathf.Clamp01(u * 2.2f)));
            SetLightIntensity(guideRim, Mathf.Lerp(startGuide, 0f, Mathf.Clamp01(u * 1.5f)));

            // 마지막 25% 구간에서 관 조명·빛기둥·음악 모두 암전과 함께 페이드아웃
            if (u > 0.75f)
            {
                float fadeOut = 1f - Mathf.InverseLerp(0.75f, 1.0f, u);
                SetLightIntensity(coffinSpot, coffinSpotRevealIntensity * fadeOut);
                SetBeamAlpha(0.65f * fadeOut);
                if (musicSource != null) musicSource.volume = fadeOut * 0.7f;
            }
            else if (musicSource != null)
            {
                musicSource.volume = Mathf.Clamp01(u * 3f) * 0.7f;
            }

            yield return null;
        }

        SetLightIntensity(coffinSpot, 0f);
        SetLightIntensity(visitorSpot, 0f);
        SetLightIntensity(guideRim, 0f);
        SetBeamAlpha(0f);
        if (musicSource != null) musicSource.Stop();

        currentPhase = StagePhase.Finished;
        Debug.Log("[Experience] 전체 체험 종료 (Finished)");
    }

    private void OnVideoLoopPointReached(VideoPlayer vp) => _videoFinishedFlag = true;
    private void OnCurtainPulled() => _curtainPulledFlag = true;
    private void OnFlowerPlaced() => _flowerPlacedFlag = true;

    private IEnumerator WaitUntilHandsClearOfCoffin(float maxWait)
    {
        if (_rig == null || coffinRoot == null) yield break;
        float elapsed = 0f;
        while (elapsed < maxWait)
        {
            bool leftNear = _rig.leftHandAnchor != null && Vector3.Distance(_rig.leftHandAnchor.position, coffinRoot.position + Vector3.up * 0.8f) < 0.7f;
            bool rightNear = _rig.rightHandAnchor != null && Vector3.Distance(_rig.rightHandAnchor.position, coffinRoot.position + Vector3.up * 0.8f) < 0.7f;
            if (!leftNear && !rightNear) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void SetCurtainsImmediate(float openAmount)
    {
        if (curtainLeft != null)
        {
            curtainLeft.position = _initialCurtainLeftPos + Vector3.left * (curtainSlideDistance * openAmount);
            curtainLeft.localScale = new Vector3(_curtainLeftBaseScale.x * Mathf.Lerp(1f, 0.45f, openAmount), _curtainLeftBaseScale.y, _curtainLeftBaseScale.z);
        }
        if (curtainRight != null)
        {
            curtainRight.position = _initialCurtainRightPos + Vector3.right * (curtainSlideDistance * openAmount);
            curtainRight.localScale = new Vector3(_curtainRightBaseScale.x * Mathf.Lerp(1f, 0.45f, openAmount), _curtainRightBaseScale.y, _curtainRightBaseScale.z);
        }
    }

    private IEnumerator AnimateCurtains(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float s = Mathf.SmoothStep(from, to, t / duration);
            SetCurtainsImmediate(s);
            yield return null;
        }
        SetCurtainsImmediate(to);
    }

    private IEnumerator FadeLights(
        float cFrom, float cTo,
        float vFrom, float vTo,
        float gFrom, float gTo,
        float bFrom, float bTo,
        float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float s = Mathf.SmoothStep(0f, 1f, t / duration);
            SetLightIntensity(coffinSpot, Mathf.Lerp(cFrom, cTo, s));
            SetLightIntensity(visitorSpot, Mathf.Lerp(vFrom, vTo, s));
            SetLightIntensity(guideRim, Mathf.Lerp(gFrom, gTo, s));
            SetBeamAlpha(Mathf.Lerp(bFrom, bTo, s));
            yield return null;
        }
        SetLightIntensity(coffinSpot, cTo);
        SetLightIntensity(visitorSpot, vTo);
        SetLightIntensity(guideRim, gTo);
        SetBeamAlpha(bTo);
    }

    private IEnumerator FadeVideoAlpha(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetVideoAlpha(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetVideoAlpha(to);
    }

    private void SetLightIntensity(Light l, float intensity)
    {
        if (l != null) l.intensity = intensity;
    }

    private void SetBeamAlpha(float intensity)
    {
        if (_beamMatInstance != null && _beamMatInstance.HasProperty("_Intensity"))
        {
            _beamMatInstance.SetFloat("_Intensity", intensity);
        }
    }

    private void SetVideoAlpha(float alpha)
    {
        if (_videoMatInstance != null && _videoMatInstance.HasProperty("_BaseColor"))
        {
            Color c = _videoMatInstance.GetColor("_BaseColor");
            c.a = alpha;
            _videoMatInstance.SetColor("_BaseColor", c);
        }
    }
}
