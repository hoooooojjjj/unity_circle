using UnityEngine;

/// <summary>
/// 장례지도사 단순 모델의 3가지 핵심 자세(대기, 헌화 안내 손짓, 인사 고개 숙임)를 부드럽게 전환합니다.
/// </summary>
public class FuneralGuideAnimator : MonoBehaviour
{
    public enum PoseState
    {
        Idle,
        PointToCoffin,
        Bow
    }

    public Transform upperBodyPivot;
    public Transform headPivot;
    public Transform leftArmPivot;
    public Transform rightArmPivot;
    public AudioSource voiceSource;
    public float transitionSpeed = 3.0f;

    [SerializeField] private PoseState currentPose = PoseState.Idle;

    private Quaternion _targetUpperRot = Quaternion.identity;
    private Quaternion _targetHeadRot = Quaternion.identity;
    private Quaternion _targetLeftArmRot = Quaternion.Euler(22f, 0f, -12f);
    private Quaternion _targetRightArmRot = Quaternion.Euler(22f, 0f, 12f);

    public void SetPose(PoseState pose)
    {
        currentPose = pose;
        switch (pose)
        {
            case PoseState.Idle:
                _targetUpperRot = Quaternion.identity;
                _targetHeadRot = Quaternion.identity;
                _targetLeftArmRot = Quaternion.Euler(22f, 18f, -14f);
                _targetRightArmRot = Quaternion.Euler(22f, -18f, 14f);
                break;

            case PoseState.PointToCoffin:
                _targetUpperRot = Quaternion.Euler(0f, -12f, 0f);
                _targetHeadRot = Quaternion.Euler(6f, -10f, 0f);
                _targetLeftArmRot = Quaternion.Euler(22f, 18f, -14f);
                // 오른팔을 관 쪽으로 부드럽게 내미는 안내 제스처
                _targetRightArmRot = Quaternion.Euler(-48f, -25f, -15f);
                break;

            case PoseState.Bow:
                _targetUpperRot = Quaternion.Euler(20f, 0f, 0f);
                _targetHeadRot = Quaternion.Euler(18f, 0f, 0f);
                _targetLeftArmRot = Quaternion.Euler(18f, 18f, -10f);
                _targetRightArmRot = Quaternion.Euler(18f, -18f, 10f);
                break;
        }
    }

    public void PlayVoiceClip(AudioClip clip)
    {
        if (voiceSource == null || clip == null) return;
        voiceSource.clip = clip;
        voiceSource.Play();
    }

    private void Start()
    {
        SetPose(currentPose);
    }

    private void Update()
    {
        float t = Time.deltaTime * transitionSpeed;
        if (upperBodyPivot != null) upperBodyPivot.localRotation = Quaternion.Slerp(upperBodyPivot.localRotation, _targetUpperRot, t);
        if (headPivot != null) headPivot.localRotation = Quaternion.Slerp(headPivot.localRotation, _targetHeadRot, t);
        if (leftArmPivot != null) leftArmPivot.localRotation = Quaternion.Slerp(leftArmPivot.localRotation, _targetLeftArmRot, t);
        if (rightArmPivot != null) rightArmPivot.localRotation = Quaternion.Slerp(rightArmPivot.localRotation, _targetRightArmRot, t);
    }
}
