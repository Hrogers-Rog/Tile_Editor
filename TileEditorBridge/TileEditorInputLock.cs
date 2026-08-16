using System;
using System.Collections.Generic;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private static readonly HashSet<string> EditorAllowedGameActions =
            new HashSet<string>(
                new[]
                {
                    "Move",
                    "Run",
                    "VeryFast",
                    "LeanLeft",
                    "LeanRight",
                    "ActivatePrimary",
                },
                StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<InputAction> _editorDisabledInputActions =
            new HashSet<InputAction>();
        private InputActionAsset _editorLockedInputAsset;
        private bool _editorWorldInputBlocked;
        private float _nextEditorInputLockCheckAt;

        private void SetGameInputLock(bool locked)
        {
            TileEditorCameraInput.EditorInputActive =
                locked && _runtimeEnabled && _visible;
            if (!locked)
            {
                ReleaseGameInputLock();
                return;
            }
            MaintainGameInputLock(force: true);
        }

        private void MaintainGameInputLock(bool force = false)
        {
            if (!_runtimeEnabled || !_visible)
            {
                ReleaseGameInputLock();
                return;
            }
            var worldInputBlocked =
                TileEditorCameraInput.EditorWorldInputBlocked;
            if (worldInputBlocked
                != _editorWorldInputBlocked)
            {
                _editorWorldInputBlocked = worldInputBlocked;
                force = true;
            }
            if (!force
                && Time.unscaledTime
                   < _nextEditorInputLockCheckAt)
            {
                return;
            }
            _nextEditorInputLockCheckAt =
                Time.unscaledTime + 0.5f;

            var asset = GameInput.shared?.inputActions;
            if (asset == null)
                return;
            if (_editorLockedInputAsset != null
                && _editorLockedInputAsset != asset)
            {
                ReleaseGameInputLock();
            }
            _editorLockedInputAsset = asset;

            var gameActions = asset.FindActionMap(
                "Game",
                throwIfNotFound: false);
            if (gameActions != null)
            {
                foreach (var action in gameActions.actions)
                {
                    var allowAction =
                        EditorAllowedGameActions.Contains(
                            action.name);
                    if (string.Equals(
                            action.name,
                            "ActivatePrimary",
                            StringComparison.OrdinalIgnoreCase)
                        && (_editorWorldInputBlocked
                            || _panelTab == PanelTab.Objects
                            || _panelTab == PanelTab.Terrain))
                    {
                        // These workspaces read the mouse pointer directly.
                        // Do not let the same click activate switches,
                        // locomotives, or other normal gameplay objects.
                        allowAction = false;
                    }
                    if (allowAction)
                    {
                        if (_editorDisabledInputActions.Remove(
                                action))
                        {
                            action.Enable();
                        }
                        continue;
                    }
                    DisableGameAction(action);
                }
            }

            DisableGameAction(asset.FindAction(
                "Global/ShowPauseMenu",
                throwIfNotFound: false));
        }

        private void DisableGameAction(InputAction action)
        {
            if (action == null || !action.enabled)
                return;
            action.Disable();
            _editorDisabledInputActions.Add(action);
        }

        private void ReleaseGameInputLock()
        {
            TileEditorCameraInput.SetMouseCameraLocked(false);
            TileEditorCameraInput.EditorInputActive = false;
            foreach (var action in
                     _editorDisabledInputActions)
            {
                try
                {
                    action?.Enable();
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not restore game input action: "
                        + ex.Message);
                }
            }
            _editorDisabledInputActions.Clear();
            _editorLockedInputAsset = null;
            _editorWorldInputBlocked = false;
            TileEditorCameraInput.PointerOverEditorWindow = false;
            TileEditorCameraInput.WorldEditPointerActive = false;
            _nextEditorInputLockCheckAt = 0f;
        }
    }
}
