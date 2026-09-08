using System;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class CombatCam
    {
        private static CameraConstraint _rig;
        private static Vector3 _minPos, _maxPos, _lowMin, _lowMax;
        private static float _minAngle, _maxAngle;
        private static bool _saved;
        private static float _atZoom;
        private static float _pollAt;
        private static bool _pulled;

        private static float Zoom => Plugin.CfgCamZoom == null ? 1f : Mathf.Clamp(Plugin.CfgCamZoom.Value, 1f, 4f);

        internal static void Tick()
        {
            try
            {
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + 0.5f;

                if (!SideButtons.InCombat()) { _rig = null; _saved = false; _pulled = false; return; }

                var rig = _rig;
                if (rig == null)
                {
                    rig = UnityEngine.Object.FindObjectOfType<CameraConstraint>();
                    if (rig == null) return;
                    _rig = rig;
                    _saved = false;
                }

                if (!_saved)
                {
                    _minPos = rig.minPos;
                    _maxPos = rig.maxPos;
                    _lowMin = rig.lowerMinPos;
                    _lowMax = rig.lowermaxPos;
                    _minAngle = rig.minAngle;
                    _maxAngle = rig.maxAngle;
                    _saved = true;
                    _atZoom = -1f;
                    _pulled = false;
                }

                float zoom = Zoom;
                if (Mathf.Abs(zoom - _atZoom) < 0.01f) return;
                _atZoom = zoom;

                var span = _maxPos - _minPos;
                var lowSpan = _lowMax - _lowMin;
                float extra = zoom - 1f;

                rig.minPos = _minPos;
                rig.lowerMinPos = _lowMin;
                rig.minAngle = _minAngle;
                rig.maxAngle = _maxAngle;
                rig.maxPos = _maxPos + span * extra;
                rig.lowermaxPos = _lowMax + lowSpan * extra;

                Plugin.Trace("[камера] предел отдаления " + _maxPos.y.ToString("0.0") + " → " + rig.maxPos.y.ToString("0.0"));
            }
            catch (Exception e) { Plugin.Trace("[камера] " + e.Message); }

            try { Pull(); }
            catch (Exception e) { Plugin.Trace("[камера] отвод: " + e.Message); }
        }

        private static void Pull()
        {
            if (_pulled || _rig == null) return;
            if (Plugin.CfgCamStart == null || !Plugin.CfgCamStart.Value) { _pulled = true; return; }
            if (!SideButtons.InCombat()) return;

            var control = UnityEngine.Object.FindObjectOfType<CameraControl>();
            if (control == null) return;

            var body = AccessTools.Property(typeof(BaseUserInput), "cameraTransform")?.GetValue(control) as Transform;
            var step = AccessTools.Field(typeof(CameraControl), "angleStep");
            var fit = AccessTools.Method(typeof(CameraControl), "checkConstraints");
            var turn = AccessTools.Method(typeof(CameraControl), "changeCameraAngle");
            if (body == null || step == null || fit == null || turn == null) { _pulled = true; return; }

            float angleStep = (float)step.GetValue(control);
            int moved = 0;
            for (int i = 0; i < 40; i++)
            {
                var want = body.position + body.forward * -0.25f * 6f;
                var args = new object[] { null, want, true };
                bool ok = (bool)fit.Invoke(control, args);
                if (!ok) break;
                body.position = (Vector3)args[0];
                turn.Invoke(control, new object[] { -0.25f * 6f * angleStep });
                moved++;
            }

            _pulled = true;
            Plugin.Trace("[камера] бой начат отдалённой камерой, шагов " + moved);
        }
    }
}
