using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal static class TileEditorOverlayVisuals
    {
        private static Material _sharedLineMaterial;

        internal static Material SharedLineMaterial
        {
            get
            {
                if (_sharedLineMaterial == null)
                {
                    var shader = Shader.Find("Sprites/Default")
                                 ?? Shader.Find(
                                     "Universal Render Pipeline/Unlit")
                                 ?? Shader.Find(
                                     "Universal Render Pipeline/Lit");
                    _sharedLineMaterial = new Material(shader)
                    {
                        color = Color.white,
                    };
                }
                return _sharedLineMaterial;
            }
        }

        internal static void SetColor(
            LineRenderer renderer,
            Color color)
        {
            if (renderer == null)
                return;
            renderer.startColor = color;
            renderer.endColor = color;
        }
    }
}
