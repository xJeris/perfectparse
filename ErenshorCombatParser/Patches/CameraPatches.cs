using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ErenshorCombatParser.Core;

namespace ErenshorCombatParser.Patches
{
    /// <summary>
    /// Prevents the game camera from zooming via scroll wheel when the mouse
    /// is over our IMGUI window (indicated by GameData.DraggingUIElement).
    /// The game's CameraController.Controls() only checks EventSystem for
    /// pointer-over-UI, which doesn't cover IMGUI windows.
    /// We save the zoom offset before Controls() runs and restore it after,
    /// effectively undoing any scroll-wheel zoom that occurred.
    /// </summary>
    public static class CameraPatches
    {
        // Cached reflection for the zoom offset field path
        private static FieldInfo _virtualCameraField;
        private static MethodInfo _getCinemachineComponentMethod;
        private static FieldInfo _followOffsetField;

        private static float _savedZoomZ;
        private static bool _savedFPVState;
        private static bool _shouldRestore;

        public static void Apply(Harmony harmony)
        {
            var self = typeof(CameraPatches);

            var controls = typeof(CameraController).GetMethod("Controls",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (controls != null)
            {
                // Cache reflection lookups
                _virtualCameraField = typeof(CameraController).GetField("virtualCamera",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                harmony.Patch(controls,
                    prefix: new HarmonyMethod(self.GetMethod(nameof(Controls_Prefix),
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(self.GetMethod(nameof(Controls_Postfix),
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else
            {
                Log.Warning("Could not find CameraController.Controls — scroll wheel blocking disabled.");
            }
        }

        private static float GetZoomZ(CameraController instance)
        {
            if (_virtualCameraField == null) return 0f;

            var vc = _virtualCameraField.GetValue(instance);
            if (vc == null) return 0f;

            // Lazy-init the GetCinemachineComponent method
            if (_getCinemachineComponentMethod == null)
            {
                // Find GetCinemachineComponent<CinemachineOrbitalTransposer>()
                var vcType = vc.GetType();
                foreach (var m in vcType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == "GetCinemachineComponent" && m.IsGenericMethod)
                    {
                        // Find CinemachineOrbitalTransposer type
                        var transposerType = vc.GetType().Assembly.GetType("Cinemachine.CinemachineOrbitalTransposer");
                        if (transposerType != null)
                        {
                            _getCinemachineComponentMethod = m.MakeGenericMethod(transposerType);
                            _followOffsetField = transposerType.GetField("m_FollowOffset",
                                BindingFlags.Public | BindingFlags.Instance);
                        }
                        break;
                    }
                }
            }

            if (_getCinemachineComponentMethod == null || _followOffsetField == null) return 0f;

            var transposer = _getCinemachineComponentMethod.Invoke(vc, null);
            if (transposer == null) return 0f;

            var offset = (Vector3)_followOffsetField.GetValue(transposer);
            return offset.z;
        }

        private static void SetZoomZ(CameraController instance, float z)
        {
            if (_virtualCameraField == null || _getCinemachineComponentMethod == null || _followOffsetField == null)
                return;

            var vc = _virtualCameraField.GetValue(instance);
            if (vc == null) return;

            var transposer = _getCinemachineComponentMethod.Invoke(vc, null);
            if (transposer == null) return;

            var offset = (Vector3)_followOffsetField.GetValue(transposer);
            offset.z = z;
            _followOffsetField.SetValue(transposer, offset);
        }

        private static void Controls_Prefix(CameraController __instance)
        {
            _shouldRestore = false;

            if (!GameData.DraggingUIElement)
                return;

            if (Input.GetAxis("Mouse ScrollWheel") == 0f)
                return;

            try
            {
                _savedZoomZ = GetZoomZ(__instance);
                _savedFPVState = GameData.PlayerControl.FPV.gameObject.activeSelf;
                _shouldRestore = true;
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to save camera zoom state: {e.Message}");
            }
        }

        private static void Controls_Postfix(CameraController __instance)
        {
            if (!_shouldRestore)
                return;

            try
            {
                SetZoomZ(__instance, _savedZoomZ);

                // Restore FPV state if scroll toggled it
                if (GameData.PlayerControl.FPV.gameObject.activeSelf != _savedFPVState)
                {
                    GameData.PlayerControl.FPV.gameObject.SetActive(_savedFPVState);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to restore camera zoom state: {e.Message}");
            }

            _shouldRestore = false;
        }
    }
}
