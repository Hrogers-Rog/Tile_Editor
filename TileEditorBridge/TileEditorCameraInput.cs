using Cameras;
using HarmonyLib;
using UI;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    /// <summary>
    /// Keeps Railroader's mouse-driven cameras from consuming the same
    /// pointer gestures used by the in-game editor. F9 only activates the
    /// editor input guard; middle mouse independently toggles whether the
    /// normal mouse camera is free or the editor camera is locked.
    /// </summary>
    internal static class TileEditorCameraInput
    {
        internal static bool EditorInputActive;

        internal static bool MouseCameraLocked;

        internal static bool PointerOverEditorWindow;

        // True when the active editor workspace consumes a primary-button
        // world gesture directly (terrain painting, pointer placement,
        // Mandela picking, or a node drag). This is independent of the
        // user's camera-lock preference.
        internal static bool WorldEditPointerActive;

        internal static int LockRevision { get; private set; }

        internal static bool CameraNavigationUnlocked =>
            EditorInputActive && !MouseCameraLocked;

        // Camera-free mode must still permit editor IPickable overlays. The
        // native strategy camera already refuses to begin a left-drag pan
        // when ObjectPicker is over one of those overlays, so a click can
        // select track while a drag started on empty terrain still pans.
        internal static bool EditorWorldInputBlocked =>
            PointerOverEditorWindow;

        internal static bool SuppressMouseCameraForWorldEdit =>
            EditorInputActive
            && WorldEditPointerActive
            && !PointerOverEditorWindow
            && Input.GetMouseButton(0);

        internal static void SetMouseCameraLocked(bool locked)
        {
            locked = EditorInputActive && locked;
            if (MouseCameraLocked == locked)
                return;
            MouseCameraLocked = locked;
            LockRevision++;
        }
    }

    [HarmonyPatch(typeof(StrategyCameraController), "UpdateInput")]
    internal static class TileEditorStrategyCameraInputPatch
    {
        private static int _observedLockRevision = -1;

        private static bool Prefix(
            StrategyCameraController __instance,
            ref Vector3 ____movementInput,
            ref float ____distanceInput,
            ref float ____angleXInput,
            ref float ____angleYInput,
            ref float ____distanceVelocity,
            ref float ____angleXVelocity,
            ref float ____angleYVelocity,
            ref Vector3? ____moveToTarget,
            ref Vector3? ____panStartPosition,
            ref bool ____rotateStarted)
        {
            if (!TileEditorCameraInput.EditorInputActive
                || (!TileEditorCameraInput.MouseCameraLocked
                    && !TileEditorCameraInput
                        .SuppressMouseCameraForWorldEdit))
                return true;

            // Locked editor navigation keeps the predictable keyboard camera
            // while suppressing left-drag pan and right-drag orbit. Q/E are
            // Railroader's LeanLeft/LeanRight movement axis, which the normal
            // strategy camera already uses as yaw input.
            var movement = GameInput.shared == null
                ? Vector3.zero
                : GameInput.shared.GetMovement(
                    __instance.normalSpeed,
                    __instance.fastSpeed,
                    __instance.fasterSpeed);
            ____movementInput = new Vector3(
                movement.x,
                0f,
                movement.z);
            var brushWheelModifier =
                Input.GetKey(KeyCode.LeftAlt)
                || Input.GetKey(KeyCode.RightAlt);
            ____distanceInput =
                TileEditorCameraInput.PointerOverEditorWindow
                || !GameInput.IsMouseOverGameWindow()
                || brushWheelModifier
                    ? 0f
                    : -Input.mouseScrollDelta.y;
            ____angleXInput = 0f;
            ____angleYInput = movement.y / 5f;
            if (_observedLockRevision
                != TileEditorCameraInput.LockRevision)
            {
                _observedLockRevision =
                    TileEditorCameraInput.LockRevision;
                ____distanceVelocity = 0f;
                ____angleXVelocity = 0f;
                ____angleYVelocity = 0f;
            }
            ____moveToTarget = null;
            ____panStartPosition = null;
            ____rotateStarted = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(MouseLookInput), nameof(MouseLookInput.UpdateInput))]
    internal static class TileEditorFirstPersonMouseLookPatch
    {
        private static void Prefix(ref bool selected)
        {
            if (TileEditorCameraInput.EditorInputActive
                && TileEditorCameraInput.MouseCameraLocked)
            {
                // Let the original method run its unselected path so Pitch
                // and Yaw are reset instead of retaining a previous mouse
                // delta.
                selected = false;
            }
        }
    }
}
