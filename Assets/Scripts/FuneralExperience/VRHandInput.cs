using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Meta XR SDK(OVRInput)와 Unity OpenXR 표준(InputDevices)을 모두 체크하는 통합 VR 손 입력 헬퍼.
/// OpenXR + Meta XR 조합에서 왼손/오른손 그립·트리거 입력이 절대 누락되지 않도록 보장합니다.
/// </summary>
public static class VRHandInput
{
    public static bool IsGrabbing(bool isLeft, float threshold = 0.45f)
    {
        // 1. Meta OVRInput RawAxis1D (Quest Touch / Touch Plus)
        if (isLeft)
        {
            if (OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger) > threshold ||
                OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger) > threshold)
            {
                return true;
            }
        }
        else
        {
            if (OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger) > threshold ||
                OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger) > threshold)
            {
                return true;
            }
        }

        // 2. Unity OpenXR InputDevices 표준 폴백 (Quest 3 OpenXR 런타임 직결)
        InputDevice device = InputDevices.GetDeviceAtXRNode(isLeft ? XRNode.LeftHand : XRNode.RightHand);
        if (device.isValid)
        {
            if (device.TryGetFeatureValue(CommonUsages.grip, out float gripVal) && gripVal > threshold) return true;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out float trigVal) && trigVal > threshold) return true;
            if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripBtn) && gripBtn) return true;
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigBtn) && trigBtn) return true;
        }

        return false;
    }

    public static Transform GetHandTransform(OVRCameraRig rig, bool isLeft)
    {
        if (rig == null) return null;
        if (isLeft)
        {
            return rig.leftControllerAnchor != null ? rig.leftControllerAnchor : rig.leftHandAnchor;
        }
        return rig.rightControllerAnchor != null ? rig.rightControllerAnchor : rig.rightHandAnchor;
    }

    public static bool IsPrimaryButtonPressed(bool isLeft)
    {
        if (isLeft)
        {
            if (OVRInput.Get(OVRInput.RawButton.X)) return true;
        }
        else
        {
            if (OVRInput.Get(OVRInput.RawButton.A)) return true;
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(isLeft ? XRNode.LeftHand : XRNode.RightHand);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed) && pressed)
        {
            return true;
        }
        return false;
    }

    public static bool IsSecondaryButtonPressed(bool isLeft)
    {
        if (isLeft)
        {
            if (OVRInput.Get(OVRInput.RawButton.Y)) return true;
        }
        else
        {
            if (OVRInput.Get(OVRInput.RawButton.B)) return true;
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(isLeft ? XRNode.LeftHand : XRNode.RightHand);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed) && pressed)
        {
            return true;
        }
        return false;
    }

    public static void SendHaptic(bool isLeft, float amplitude = 0.35f, float duration = 0.08f)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(isLeft ? XRNode.LeftHand : XRNode.RightHand);
        if (device.isValid && device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
        {
            device.SendHapticImpulse(0u, amplitude, duration);
        }
    }
}
