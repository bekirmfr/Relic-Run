using UnityEngine;

namespace GameLift.UI.LevelPath
{
    public enum NodeAnchorPreset
    {
        BottomCenter,
        MiddleCenter,
        TopCenter,
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight,
        MiddleLeft,
        MiddleRight
    }

    public static class NodeAnchorPresetExtensions
    {
        public static (Vector2 min, Vector2 max) GetAnchorValues(this NodeAnchorPreset preset)
        {
            return preset switch
            {
                NodeAnchorPreset.BottomCenter => (new Vector2(0.5f, 0f), new Vector2(0.5f, 0f)),
                NodeAnchorPreset.MiddleCenter => (new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)),
                NodeAnchorPreset.TopCenter => (new Vector2(0.5f, 1f), new Vector2(0.5f, 1f)),
                NodeAnchorPreset.BottomLeft => (new Vector2(0f, 0f), new Vector2(0f, 0f)),
                NodeAnchorPreset.BottomRight => (new Vector2(1f, 0f), new Vector2(1f, 0f)),
                NodeAnchorPreset.TopLeft => (new Vector2(0f, 1f), new Vector2(0f, 1f)),
                NodeAnchorPreset.TopRight => (new Vector2(1f, 1f), new Vector2(1f, 1f)),
                NodeAnchorPreset.MiddleLeft => (new Vector2(0f, 0.5f), new Vector2(0f, 0.5f)),
                NodeAnchorPreset.MiddleRight => (new Vector2(1f, 0.5f), new Vector2(1f, 0.5f)),
                _ => (new Vector2(0.5f, 0f), new Vector2(0.5f, 0f))
            };
        }
    }
}
