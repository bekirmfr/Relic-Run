using System.Collections.Generic;
using UnityEngine;

namespace GameLift.UI.LevelPath
{
    public interface IPipeGenerator
    {
        void GeneratePipes(List<Vector2> positions, RectTransform contentRoot,
            PipeSettings settings, Vector2 anchorMin, Vector2 anchorMax);
    }
}
