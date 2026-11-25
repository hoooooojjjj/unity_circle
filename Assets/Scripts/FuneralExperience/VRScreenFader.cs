using System.Collections;
using UnityEngine;

/// <summary>
/// VR(Quest 스테레오)과 에디터 Game View 모두에서 완벽하게 동작하는 화면 Fade Out/In 컴포넌트.
/// CenterEyeAnchor(메인 카메라) 바로 앞에 Overlay/ZTest Always 쿼드를 생성해 시야 전체를 부드럽게 암전합니다.
/// </summary>
public class VRScreenFader : MonoBehaviour
{
    public Color fadeColor = Color.black;
    public Material fadeMaterial;

    private MeshRenderer _quadRenderer;
    private Material _matInstance;
    private float _currentAlpha;

    private void Awake()
    {
        EnsureFadeQuad();
        SetAlpha(0f);
    }

    private void EnsureFadeQuad()
    {
        if (_quadRenderer != null) return;

        Transform existing = transform.Find("ScreenFadeOverlay");
        GameObject quad;
        if (existing != null)
        {
            quad = existing.gameObject;
        }
        else
        {
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ScreenFadeOverlay";
            quad.transform.SetParent(transform, false);
            Collider col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // 카메라 Near Clip(0.1~0.2) 바로 앞, 넓은 FOV에서도 화면 전체를 덮는 크기
        quad.transform.localPosition = new Vector3(0f, 0f, 0.25f);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(2.0f, 2.0f, 1.0f);

        _quadRenderer = quad.GetComponent<MeshRenderer>();
        _quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _quadRenderer.receiveShadows = false;

        Shader sh = Shader.Find("Custom/VRScreenFade");
        if (fadeMaterial != null)
        {
            _matInstance = new Material(fadeMaterial);
        }
        else if (sh != null)
        {
            _matInstance = new Material(sh);
        }
        else
        {
            _matInstance = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }

        _quadRenderer.sharedMaterial = _matInstance;
    }

    public IEnumerator FadeOut(float duration)
    {
        EnsureFadeQuad();
        float startAlpha = _currentAlpha;
        if (duration <= 0.01f)
        {
            SetAlpha(1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            SetAlpha(Mathf.Lerp(startAlpha, 1f, eased));
            yield return null;
        }
        SetAlpha(1f);
    }

    public IEnumerator FadeIn(float duration)
    {
        EnsureFadeQuad();
        float startAlpha = _currentAlpha;
        if (duration <= 0.01f)
        {
            SetAlpha(0f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            SetAlpha(Mathf.Lerp(startAlpha, 0f, eased));
            yield return null;
        }
        SetAlpha(0f);
    }

    public void SetAlpha(float alpha)
    {
        _currentAlpha = Mathf.Clamp01(alpha);
        if (_quadRenderer == null || _matInstance == null) return;

        bool visible = _currentAlpha > 0.001f;
        _quadRenderer.enabled = visible;
        if (visible)
        {
            Color c = fadeColor;
            c.a = _currentAlpha;
            if (_matInstance.HasProperty("_Color")) _matInstance.SetColor("_Color", c);
            if (_matInstance.HasProperty("_BaseColor")) _matInstance.SetColor("_BaseColor", c);
        }
    }
}
