using UnityEngine;

/// <summary>
/// VideoSurface 오브젝트의 Inspector에서 상·하·좌·우 테두리 Fade 범위를 실시간으로 조절합니다.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class VideoEdgeFadeController : MonoBehaviour
{
    [Header("영상 테두리 Fade 범위 (0 = 없음, 0.48 = 최대)")]
    [Range(0f, 0.48f)] public float fadeLeft = 0.14f;
    [Range(0f, 0.48f)] public float fadeRight = 0.14f;
    [Range(0f, 0.48f)] public float fadeTop = 0.14f;
    [Range(0f, 0.48f)] public float fadeBottom = 0.14f;

    [Header("경계 부드러움")]
    [Range(0.5f, 4.0f)] public float edgeSoftness = 1.5f;

    private static readonly int FadeLeftId = Shader.PropertyToID("_FadeLeft");
    private static readonly int FadeRightId = Shader.PropertyToID("_FadeRight");
    private static readonly int FadeTopId = Shader.PropertyToID("_FadeTop");
    private static readonly int FadeBottomId = Shader.PropertyToID("_FadeBottom");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");

    private MaterialPropertyBlock _mpb;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    public void Apply()
    {
        Renderer r = GetComponent<Renderer>();
        if (r == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetFloat(FadeLeftId, fadeLeft);
        _mpb.SetFloat(FadeRightId, fadeRight);
        _mpb.SetFloat(FadeTopId, fadeTop);
        _mpb.SetFloat(FadeBottomId, fadeBottom);
        _mpb.SetFloat(EdgeSoftnessId, edgeSoftness);
        r.SetPropertyBlock(_mpb);
    }
}
