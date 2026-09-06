using System.Collections.Generic;
using UnityEngine;

namespace GameLift.UI.LevelPath
{
    public class LinearLevelPathGenerator : ILevelPathGenerator
    {
        public List<Vector2> CalculatePositions(int nodeCount, float spacing, float nodeHeight)
        {
            List<Vector2> positions = new List<Vector2>();
            float currentY = nodeHeight;

            for (int i = 0; i < nodeCount; i++)
            {
                positions.Add(new Vector2(0, currentY));
                currentY += nodeHeight + spacing;
            }

            return positions;
        }
    }
}
