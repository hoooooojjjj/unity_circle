using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 양손 컨트롤러(LeftControllerAnchor / RightControllerAnchor)의 실제 위치와
/// 그립/트리거 입력 상태를 눈으로 확인할 수 있게 해주는 심플한 VR 손끝 시각 피드백.
/// OpenXR에서 컨트롤러 포즈가 안 따라올 경우 XRNode 포즈로 자동 보완합니다.
/// </summary>
public class VRHandVisualCue : MonoBehaviour
{
    public bool isLeftHand;
    public Color idleColor = new Color(0.85f, 0.85f, 0.85f, 0.55f);
    public Color grabColor = new Color(1.0f, 0.82f, 0.35f, 0.95f);

    private Renderer _tipRenderer;
    private Material _tipMat;
    private Transform _tipTransform;

    private void Awake()
    {
        // 컨트롤러 앵커에 항상 보이는 작은 손끝 포인트 구체 생성
        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.name = isLeftHand ? "LeftHandTipVisual" : "RightHandTipVisual";
        tip.transform.SetParent(transform, false);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.04f);
        tip.transform.localScale = Vector3.one * 0.032f;

        Collider col = tip.GetComponent<Collider>();
        if (col != null) Destroy(col);

        _tipTransform = tip.transform;
        _tipRenderer = tip.GetComponent<Renderer>();
        _tipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        _tipMat = new Material(unlit != null ? unlit : Shader.Find("Sprites/Default"));
        if (_tipMat.HasProperty("_BaseColor")) _tipMat.SetColor("_BaseColor", idleColor);
        _tipRenderer.sharedMaterial = _tipMat;
    }

    private void Update()
    {
        // OpenXR에서 OVRControllerAnchor가 포즈를 못 갱신할 경우를 대비해 직접 XRNode 포즈 보완
        InputDevice dev = InputDevices.GetDeviceAtXRNode(isLeftHand ? XRNode.LeftHand : XRNode.RightHand);
        if (dev.isValid && transform.parent != null)
        {
            if (dev.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 devPos) && devPos.sqrMagnitude > 1e-6f)
            {
                if (transform.parent.localPosition.sqrMagnitude < 1e-6f)
                {
                    transform.parent.localPosition = devPos;
                }
            }
            if (dev.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion devRot))
            {
                if (transform.parent.localRotation == Quaternion.identity)
                {
                    transform.parent.localRotation = devRot;
                }
            }
        }

        bool grabbing = VRHandInput.IsGrabbing(isLeftHand, 0.35f);
        if (_tipMat != null && _tipMat.HasProperty("_BaseColor"))
        {
            _tipMat.SetColor("_BaseColor", grabbing ? grabColor : idleColor);
        }
        if (_tipTransform != null)
        {
            float targetScale = grabbing ? 0.046f : 0.030f;
            _tipTransform.localScale = Vector3.Lerp(_tipTransform.localScale, Vector3.one * targetScale, Time.deltaTime * 18f);
        }
    }
}
