using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private string _waterId = "water:new";
        private string _selectedWaterId = string.Empty;
        private string _waterLoadedId = string.Empty;
        private string _waterWidth = "200";
        private string _waterLength = "200";
        private string _waterHeight = string.Empty;
        private string _waterSourcePath = string.Empty;
        private string _waterMaterialName = string.Empty;
        private bool _waterLockHeight = true;
        private bool _waterSnapToTerrain;
        private bool _waterEnableCollider = true;
        private string _waterUvScale = "1";
        private string _waterTriangleDensity = "0.2";
        private string _waterMaximumTriangleArea = "50";
        private string _waterYOffset = "0";
        private int _waterPointIndex;
        private string _waterPointX = "0";
        private string _waterPointY = "0";
        private string _waterPointZ = "0";
        private readonly List<Vector3> _waterEditingPoints = new List<Vector3>();
        private int _baseLakeIndex;

        private void DrawWaterPanel()
        {
            if (_mapEditor == null || !_mapEditor.Available || !_mapEditor.GraphOpen)
                return;

            GUILayout.Label("WATER SURFACES", _titleStyle);
            GUILayout.Label(
                "Water mask painting marks terrain coverage. This workspace creates the visible lake polygon itself.",
                _lineStyle);
            var native = _mapEditor.FuseOperationsDocument;
            if (!native)
            {
                GUILayout.Label("NATIVE FUSE ONLY", _offlineStyle);
                GUILayout.Label(
                    "RailLoader JSON has no lake-polygon schema. These controls are disabled so a legacy export cannot silently lose the water surface.",
                    _mutedStyle);
                GUI.enabled = false;
                DrawWaterCreateForm();
                GUI.enabled = true;
                return;
            }

            DrawPointerPlacementStatus();
            DrawAuthoredWaterList();
            DrawWaterCreateForm();
            DrawBaseLakeSources();
            DrawSelectedWaterEditor();
        }

        private void DrawAuthoredWaterList()
        {
            var surfaces = _mapEditor.WaterSurfaces;
            GUILayout.Space(5f);
            GUILayout.Label("AUTHORED WATER", _titleStyle);
            if (surfaces.Count == 0)
            {
                GUILayout.Label("This package has no native water surfaces yet.", _mutedStyle);
                return;
            }
            foreach (var surface in surfaces.Take(16))
            {
                var old = GUI.backgroundColor;
                if (string.Equals(surface.Id, _selectedWaterId, StringComparison.OrdinalIgnoreCase))
                    GUI.backgroundColor = new Color(0.10f, 0.58f, 0.82f, 1f);
                if (GUILayout.Button(surface.Id + "  (" + surface.Points.Length + " points)", GUILayout.Height(25f)))
                {
                    _selectedWaterId = surface.Id;
                    LoadWaterForm(surface);
                }
                GUI.backgroundColor = old;
            }
            if (surfaces.Count > 16)
                GUILayout.Label("Showing the first 16; use distinct IDs and split very large projects into layers.", _mutedStyle);
        }

        private void DrawWaterCreateForm()
        {
            GUILayout.Space(8f);
            GUILayout.Label("CREATE A RECTANGULAR LAKE", _titleStyle);
            DrawTextField("Water ID", ref _waterId);
            DrawTextField("Width (m)", ref _waterWidth);
            DrawTextField("Length (m)", ref _waterLength);
            DrawTextField("Elevation (blank = click)", ref _waterHeight);
            DrawWaterMaterialFields();
            if (GUILayout.Button("PLACE WATER SURFACE WITH POINTER", GUILayout.Height(36f)))
                ArmPointerPlacement(PointerPlacementKind.WaterSurface, string.Empty, false);
        }

        private void DrawWaterMaterialFields()
        {
            DrawTextField("Source lake path", ref _waterSourcePath);
            DrawTextField("Material name", ref _waterMaterialName);
            _waterLockHeight = GUILayout.Toggle(_waterLockHeight, " Lock all boundary points to one elevation");
            _waterSnapToTerrain = GUILayout.Toggle(_waterSnapToTerrain, " Snap mesh vertices to terrain");
            _waterEnableCollider = GUILayout.Toggle(_waterEnableCollider, " Water collider / buoyancy surface");
            DrawTextField("UV scale", ref _waterUvScale);
            DrawTextField("Triangle density (0-1)", ref _waterTriangleDensity);
            DrawTextField("Maximum triangle area", ref _waterMaximumTriangleArea);
            DrawTextField("Vertical offset", ref _waterYOffset);
            GUILayout.Label(
                "Leave source/material blank to reuse a loaded stock water material. A named source must resolve to a LakePolygon.",
                _mutedStyle);
        }

        private void DrawBaseLakeSources()
        {
            var lakes = _mapEditor.BaseLakes;
            GUILayout.Space(8f);
            GUILayout.Label("EXISTING BASE LAKES", _titleStyle);
            if (lakes.Count == 0)
            {
                GUILayout.Label("No editable base LakePolygon is loaded on this map.", _mutedStyle);
                return;
            }
            _baseLakeIndex = Mathf.Clamp(_baseLakeIndex, 0, lakes.Count - 1);
            var lake = lakes[_baseLakeIndex];
            GUILayout.BeginHorizontal();
            GUI.enabled = _baseLakeIndex > 0;
            if (GUILayout.Button("<", GUILayout.Width(36f))) _baseLakeIndex--;
            GUI.enabled = true;
            GUILayout.Label(Shorten(lake.Path, 54), _lineStyle);
            GUI.enabled = _baseLakeIndex + 1 < lakes.Count;
            if (GUILayout.Button(">", GUILayout.Width(36f))) _baseLakeIndex++;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(lake.Points.Length + " boundary points  |  material " + (string.IsNullOrWhiteSpace(lake.MaterialName) ? "automatic" : lake.MaterialName), _mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("USE AS MATERIAL SOURCE"))
            {
                _waterSourcePath = lake.Path;
                _waterMaterialName = lake.MaterialName;
            }
            if (GUILayout.Button("REPLACE WITH EDITABLE COPY"))
            {
                RunGameAction(() => _mapEditor.ReplaceBaseLake(_waterId, lake));
                _selectedWaterId = _waterId;
                _waterLoadedId = string.Empty;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSelectedWaterEditor()
        {
            if (string.IsNullOrWhiteSpace(_selectedWaterId))
                return;
            var selected = _mapEditor.WaterSurfaces.FirstOrDefault(surface => string.Equals(surface.Id, _selectedWaterId, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
                return;
            if (!string.Equals(_waterLoadedId, selected.Id, StringComparison.OrdinalIgnoreCase))
                LoadWaterForm(selected);

            GUILayout.Space(8f);
            GUILayout.Label("EDIT " + selected.Id, _titleStyle);
            DrawWaterMaterialFields();
            if (_waterEditingPoints.Count >= 3)
            {
                _waterPointIndex = Mathf.Clamp(_waterPointIndex, 0, _waterEditingPoints.Count - 1);
                GUILayout.Label("BOUNDARY POINT " + (_waterPointIndex + 1) + " / " + _waterEditingPoints.Count, _titleStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous"))
                {
                    _waterPointIndex = (_waterPointIndex + _waterEditingPoints.Count - 1) % _waterEditingPoints.Count;
                    LoadWaterPointFields();
                }
                if (GUILayout.Button("Next"))
                {
                    _waterPointIndex = (_waterPointIndex + 1) % _waterEditingPoints.Count;
                    LoadWaterPointFields();
                }
                GUILayout.EndHorizontal();
                DrawTextField("Point X", ref _waterPointX);
                DrawTextField("Point Y", ref _waterPointY);
                DrawTextField("Point Z", ref _waterPointZ);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SET POINT"))
                {
                    _waterEditingPoints[_waterPointIndex] = new Vector3(
                        ParseFloat(_waterPointX, "water point X"),
                        ParseFloat(_waterPointY, "water point Y"),
                        ParseFloat(_waterPointZ, "water point Z"));
                }
                if (GUILayout.Button("ADD AFTER"))
                {
                    var next = (_waterPointIndex + 1) % _waterEditingPoints.Count;
                    var value = (_waterEditingPoints[_waterPointIndex] + _waterEditingPoints[next]) * 0.5f;
                    _waterEditingPoints.Insert(_waterPointIndex + 1, value);
                    _waterPointIndex++;
                    LoadWaterPointFields();
                }
                GUI.enabled = _waterEditingPoints.Count > 3;
                if (GUILayout.Button("REMOVE"))
                {
                    _waterEditingPoints.RemoveAt(_waterPointIndex);
                    _waterPointIndex = Mathf.Clamp(_waterPointIndex, 0, _waterEditingPoints.Count - 1);
                    LoadWaterPointFields();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("APPLY WATER CHANGES", GUILayout.Height(34f)))
            {
                var updated = BuildWaterInfo(selected.Id);
                RunGameAction(() => _mapEditor.UpdateWaterSurface(updated));
                _waterLoadedId = string.Empty;
            }
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.28f, 0.20f);
            if (GUILayout.Button("DELETE WATER SURFACE", GUILayout.Height(30f)))
            {
                RunGameAction(() => _mapEditor.DeleteWaterSurface(selected.Id));
                _selectedWaterId = string.Empty;
                _waterLoadedId = string.Empty;
            }
            GUI.backgroundColor = old;
        }

        private void LoadWaterForm(TileEditorGraphSession.WaterSurfaceInfo info)
        {
            _waterLoadedId = info.Id;
            _waterSourcePath = info.SourceLakePath;
            _waterMaterialName = info.MaterialName;
            _waterLockHeight = info.LockHeight;
            _waterSnapToTerrain = info.SnapToTerrain;
            _waterEnableCollider = info.EnableCollider;
            _waterUvScale = FormatWaterNumber(info.UvScale);
            _waterTriangleDensity = FormatWaterNumber(info.TriangleDensity);
            _waterMaximumTriangleArea = FormatWaterNumber(info.MaximumTriangleArea);
            _waterYOffset = FormatWaterNumber(info.YOffset);
            _waterEditingPoints.Clear();
            _waterEditingPoints.AddRange(info.Points ?? Array.Empty<Vector3>());
            _waterPointIndex = 0;
            LoadWaterPointFields();
        }

        private TileEditorGraphSession.WaterSurfaceInfo BuildWaterInfo(string id)
        {
            return new TileEditorGraphSession.WaterSurfaceInfo
            {
                Id = id,
                SourceLakePath = _waterSourcePath,
                MaterialName = _waterMaterialName,
                LockHeight = _waterLockHeight,
                SnapToTerrain = _waterSnapToTerrain,
                EnableCollider = _waterEnableCollider,
                UvScale = ParseFloat(_waterUvScale, "water UV scale"),
                TriangleDensity = ParseFloat(_waterTriangleDensity, "water triangle density"),
                MaximumTriangleArea = ParseFloat(_waterMaximumTriangleArea, "water maximum triangle area"),
                YOffset = ParseFloat(_waterYOffset, "water vertical offset"),
                Points = _waterEditingPoints.ToArray(),
            };
        }

        private void LoadWaterPointFields()
        {
            if (_waterEditingPoints.Count == 0)
                return;
            var point = _waterEditingPoints[Mathf.Clamp(_waterPointIndex, 0, _waterEditingPoints.Count - 1)];
            _waterPointX = FormatWaterNumber(point.x);
            _waterPointY = FormatWaterNumber(point.y);
            _waterPointZ = FormatWaterNumber(point.z);
        }

        private static string FormatWaterNumber(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
