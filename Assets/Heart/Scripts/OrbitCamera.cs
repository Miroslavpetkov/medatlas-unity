using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MedAtlas.Heart
{
    /// <summary>Orbit / zoom / pan camera for mouse and touch (one finger rotates, two fingers pinch-zoom and pan).</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Vector3 Target;
        public float Distance = 4f, MinDistance = 1.2f, MaxDistance = 9f;
        public float Yaw = 0f, Pitch = 8f;
        Vector3 pan;
        float tYaw, tPitch, tDist; Vector3 tPan;
        float defYaw, defPitch, defDist;
        public System.Action<Vector2> OnTap;
        /// <summary>Portrait layout: farther camera and the organ moved to the upper part of the screen.</summary>
        public float DistanceScale = 1f, ViewOffsetY = 0f, ViewOffsetX = 0f;
        float curScale = 1f, curOff = 0f;
        Vector2 pressPos; float pressTime; bool dragging; bool pressOverUI;

        void Start()
        {
            EnhancedTouchSupport.Enable();
            tYaw = Yaw; tPitch = Pitch; tDist = Distance;
            defYaw = Yaw; defPitch = Pitch; defDist = Distance;
        }

        public void ResetView() { tYaw = defYaw; tPitch = defPitch; tDist = defDist; tPan = Vector3.zero; }

        static bool OverUI(Vector2 screen)
        {
            var es = EventSystem.current; if (es == null) return false;
            var data = new PointerEventData(es) { position = screen };
            var results = new System.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(data, results);
            return results.Count > 0;
        }

        void Update()
        {
            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                var a = touches[0]; var b = touches[1];
                Vector2 pa = a.screenPosition, pb = b.screenPosition;
                Vector2 la = pa - a.delta, lb = pb - b.delta;
                float d0 = (la - lb).magnitude, d1 = (pa - pb).magnitude;
                if (d0 > 1f) tDist *= d0 / Mathf.Max(d1, 1f);
                Vector2 mid = (a.delta + b.delta) * 0.5f;
                PanBy(mid);
                dragging = true;
            }
            else if (touches.Count == 1)
            {
                var t = touches[0];
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began) { pressPos = t.screenPosition; pressTime = Time.unscaledTime; dragging = false; pressOverUI = OverUI(t.screenPosition); }
                if (!pressOverUI)
                {
                    if ((t.screenPosition - pressPos).magnitude > 12f) dragging = true;
                    if (dragging) RotateBy(t.delta);
                    if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended && !dragging && Time.unscaledTime - pressTime < 0.35f) OnTap?.Invoke(t.screenPosition);
                }
            }
            else
            {
                var m = Mouse.current;
                if (m != null)
                {
                    Vector2 pos = m.position.ReadValue();
                    if (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)
                    { pressPos = pos; dragging = false; pressOverUI = OverUI(pos); }
                    Vector2 d = m.delta.ReadValue();
                    if (!pressOverUI)
                    {
                        if (m.leftButton.isPressed)
                        {
                            if ((pos - pressPos).magnitude > 4f) dragging = true;
                            if (dragging) RotateBy(d);
                        }
                        if (m.rightButton.isPressed || m.middleButton.isPressed) PanBy(d);
                        if (m.leftButton.wasReleasedThisFrame && !dragging) OnTap?.Invoke(pos);
                    }
                    float sc = m.scroll.ReadValue().y;
                    if (Mathf.Abs(sc) > 0.01f && !OverUI(pos)) tDist *= Mathf.Pow(0.9f, Mathf.Sign(sc) * Mathf.Min(Mathf.Abs(sc) / 120f + 0.5f, 2f));
                }
                var k = Keyboard.current;
                if (k != null && k.rKey.wasPressedThisFrame) ResetView();
            }

            tDist = Mathf.Clamp(tDist, MinDistance, MaxDistance);
            tPitch = Mathf.Clamp(tPitch, -85f, 85f);
            float s = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f);
            Yaw = Mathf.LerpAngle(Yaw, tYaw, s); Pitch = Mathf.Lerp(Pitch, tPitch, s); Distance = Mathf.Lerp(Distance, tDist, s);
            pan = Vector3.Lerp(pan, tPan, s);
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            curScale = Mathf.Lerp(curScale, DistanceScale, s); curOff = Mathf.Lerp(curOff, ViewOffsetY, s);
            float camD = Distance * curScale;
            transform.position = Target + pan + rot * new Vector3(ViewOffsetX * camD, -curOff * camD, -camD);
            transform.rotation = rot;
        }

        void RotateBy(Vector2 d)
        {
            float k = 0.25f * 1080f / Mathf.Max(Screen.height, 1);
            tYaw += d.x * k * 1.2f; tPitch -= d.y * k;
        }

        void PanBy(Vector2 d)
        {
            float k = tDist * 0.0012f * 1080f / Mathf.Max(Screen.height, 1);
            var rot = Quaternion.Euler(tPitch, tYaw, 0f);
            tPan -= rot * new Vector3(d.x, d.y, 0) * k;
            tPan = Vector3.ClampMagnitude(tPan, 1.5f);
        }
    }
}
