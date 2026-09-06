using System.Collections.Generic;
using UnityEngine;

namespace GameLift.UI.LevelPath
{
    public interface ILevelPathGenerator
    {
        List<Vector2> CalculatePositions(int nodeCount, float spacing, float nodeHeight);
    }
}
