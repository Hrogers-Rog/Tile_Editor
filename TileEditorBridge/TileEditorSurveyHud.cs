using System.Globalization;
using System.Text;
using Helpers;
using Map.Runtime;
using Track;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class PointerSurveyInfo
        {
            internal Vector3 UnityWorldPosition;
            internal Vector3 GamePosition;
            internal Vector3 GraphLocalPosition;
            internal bool HasTile;
            internal Vector2Int Tile;
            internal bool HasTileLocalPosition;
            internal Vector3 TileLocalPosition;
            internal bool HasTrack;
            internal string SegmentId = string.Empty;
            internal string NodeA = string.Empty;
            internal string NodeB = string.Empty;
            internal float TrackDistance;
            internal float TrackLength;
            internal float PointerOffset;
            internal float GradePercent;
            internal float HeadingDegrees;
            internal string Gauge = "Standard";
            internal string TrackClass = string.Empty;
            internal string GroupId = string.Empty;
        }

        internal PointerSurveyInfo InspectPointerSurvey(
            Vector3 worldPosition)
        {
            var gamePosition = WorldTransformer.WorldToGame(worldPosition);
            var result = new PointerSurveyInfo
            {
                UnityWorldPosition = worldPosition,
                GamePosition = gamePosition,
                GraphLocalPosition = _graph == null
                    ? gamePosition
                    : _graph.transform.InverseTransformPoint(worldPosition),
            };

            var manager = MapManager.Instance;
            if (manager != null)
            {
                result.HasTile = true;
                result.Tile = manager.TilePositionFromPoint(gamePosition);
                if (manager.TryGetTerrain(
                        result.Tile,
                        out var mapTerrain)
                    && mapTerrain != null
                    && mapTerrain.tileData != null)
                {
                    result.HasTileLocalPosition = true;
                    result.TileLocalPosition =
                        gamePosition - mapTerrain.tileData.Bounds.min;
                }
            }

            if (_graph == null
                || !_graph.TryGetLocationFromGamePoint(
                    gamePosition,
                    60f,
                    out var location)
                || location.segment == null)
            {
                return result;
            }

            var positionRotation = _graph.GetPositionRotation(
                location,
                PositionAccuracy.High);
            var forward = positionRotation.Rotation * Vector3.forward;
            var horizontal = new Vector2(forward.x, forward.z).magnitude;
            result.HasTrack = true;
            result.SegmentId = location.segment.id ?? string.Empty;
            result.NodeA = location.segment.a?.id ?? string.Empty;
            result.NodeB = location.segment.b?.id ?? string.Empty;
            result.TrackDistance = location.DistanceTo(TrackSegment.End.A);
            result.TrackLength = location.segment.GetLength();
            result.PointerOffset = Vector3.Distance(
                gamePosition,
                positionRotation.Position);
            result.GradePercent = horizontal < 0.0001f
                ? 0f
                : forward.y / horizontal * 100f;
            result.HeadingDegrees = Mathf.Repeat(
                Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg,
                360f);
            result.Gauge = GetSegmentGauge(location.segment.id);
            result.TrackClass = location.segment.trackClass.ToString();
            result.GroupId = location.segment.groupId ?? string.Empty;
            return result;
        }
    }

    internal sealed partial class TileEditorBridgePanel
    {
        private const float SurveyRefreshInterval = 0.08f;
        private TileEditorGraphSession.PointerSurveyInfo _surveyInfo;
        private bool _surveyHotkeyHeld;
        private bool _surveyPointerHit;
        private float _nextSurveyRefreshAt;
        private GUIStyle _surveyTitleStyle;
        private GUIStyle _surveyTextStyle;

        private void UpdateSurveyHud()
        {
            _surveyHotkeyHeld = _visible
                && (Input.GetKey(KeyCode.LeftShift)
                    || Input.GetKey(KeyCode.RightShift))
                && Input.GetKey(KeyCode.Slash);
            if (!_surveyHotkeyHeld)
            {
                _surveyInfo = null;
                _surveyPointerHit = false;
                _nextSurveyRefreshAt = 0f;
                return;
            }
            if (Time.unscaledTime < _nextSurveyRefreshAt)
                return;
            _nextSurveyRefreshAt =
                Time.unscaledTime + SurveyRefreshInterval;
            _surveyPointerHit = TryGetPointerSurfaceHit(
                false,
                out var hit);
            _surveyInfo = _surveyPointerHit
                ? _mapEditor?.InspectPointerSurvey(hit.point)
                : null;
        }

        private void DrawSurveyHud()
        {
            if (!_surveyHotkeyHeld)
                return;
            EnsureSurveyStyles();
            var width = Mathf.Min(410f, Mathf.Max(300f, Screen.width - 24f));
            var height = _surveyInfo != null && _surveyInfo.HasTrack
                ? 198f
                : 132f;
            var rect = new Rect(
                Mathf.Max(12f, Screen.width - width - 12f),
                12f,
                width,
                height);
            var oldDepth = GUI.depth;
            GUI.depth = -1000;
            GUI.DrawTexture(
                rect,
                _windowBackgroundTexture,
                ScaleMode.StretchToFill,
                true);
            GUI.DrawTexture(
                new Rect(rect.x, rect.y, rect.width, 2f),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(rect.x, rect.yMax - 2f, rect.width, 2f),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(rect.x, rect.y, 2f, rect.height),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(rect.xMax - 2f, rect.y, 2f, rect.height),
                _windowBorderTexture);
            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f),
                "TRACK SURVEY — RELEASE SHIFT+? TO CLOSE",
                _surveyTitleStyle);
            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 33f, rect.width - 24f, rect.height - 40f),
                BuildSurveyText(),
                _surveyTextStyle);
            GUI.depth = oldDepth;
        }

        private string BuildSurveyText()
        {
            if (!_surveyPointerHit || _surveyInfo == null)
                return "No terrain or track surface under the mouse pointer.";
            var info = _surveyInfo;
            var builder = new StringBuilder(320);
            builder.Append("MAP / GAME   ")
                .Append(FormatSurveyVector(info.GamePosition))
                .AppendLine();
            builder.Append("UNITY WORLD  ")
                .Append(FormatSurveyVector(info.UnityWorldPosition))
                .AppendLine();
            builder.Append("GRAPH LOCAL  ")
                .Append(FormatSurveyVector(info.GraphLocalPosition))
                .AppendLine();
            if (info.HasTile)
            {
                builder.Append("TILE         ")
                    .Append(info.Tile.x.ToString(CultureInfo.InvariantCulture))
                    .Append(", ")
                    .Append(info.Tile.y.ToString(CultureInfo.InvariantCulture));
                if (info.HasTileLocalPosition)
                {
                    builder.Append("   LOCAL ")
                        .Append(FormatSurveyVector(info.TileLocalPosition));
                }
                builder.AppendLine();
            }
            if (!info.HasTrack)
            {
                builder.Append("TRACK        none within 60 m");
                return builder.ToString();
            }
            builder.Append("TRACK        ")
                .Append(info.SegmentId)
                .Append("   ")
                .Append(info.NodeA)
                .Append(" → ")
                .Append(info.NodeB)
                .AppendLine();
            builder.Append("GRADE        ")
                .Append(FormatSurveyNumber(info.GradePercent))
                .Append("%   HEADING ")
                .Append(info.HeadingDegrees.ToString(
                    "0.0°",
                    CultureInfo.InvariantCulture))
                .AppendLine();
            builder.Append("ALONG        ")
                .Append(info.TrackDistance.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture))
                .Append(" / ")
                .Append(info.TrackLength.ToString(
                    "0.0 m",
                    CultureInfo.InvariantCulture))
                .Append("   OFFSET ")
                .Append(info.PointerOffset.ToString(
                    "0.0 m",
                    CultureInfo.InvariantCulture))
                .AppendLine();
            builder.Append("TYPE         ")
                .Append(info.Gauge)
                .Append(" / ")
                .Append(info.TrackClass)
                .Append("   GROUP ")
                .Append(string.IsNullOrWhiteSpace(info.GroupId)
                    ? "(none)"
                    : info.GroupId);
            return builder.ToString();
        }

        private void EnsureSurveyStyles()
        {
            if (_surveyTitleStyle != null)
                return;
            _surveyTitleStyle = new GUIStyle(_onlineStyle)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
            };
            _surveyTextStyle = new GUIStyle(_lineStyle)
            {
                fontSize = 12,
                wordWrap = false,
                richText = false,
            };
        }

        private static string FormatSurveyVector(Vector3 value)
        {
            return value.x.ToString("0.00", CultureInfo.InvariantCulture)
                   + ", "
                   + value.y.ToString("0.00", CultureInfo.InvariantCulture)
                   + ", "
                   + value.z.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FormatSurveyNumber(float value)
        {
            return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        }
    }
}
