using UnityEngine;

namespace XRSuperspace
{
    public static class DesktopMotion
    {
        // No overshoot; exact endpoints. Used only for presentation.
        public static float Ease(float value) {value=Mathf.Clamp01(value);return value*value*(3-2*value);}
    }
}
