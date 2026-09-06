using System.Collections.Generic;
using UnityEngine;

namespace GameLift.UI.LevelPath
{
    public class SineWaveLevelGenerator : ILevelPathGenerator
    {
        private readonly float _amplitude;
        private readonly float _frequency;

        public SineWaveLevelGenerator(float amplitude = 150f, float frequency = 1f)
        {
            _amplitude = amplitude;
            _frequency = frequency;
        }

        public List<Vector2> CalculatePositions(int nodeCount, float spacing, float nodeHeight)
        {
            List<Vector2> positions = new List<Vector2>();
            float currentY = nodeHeight;

            for (int i = 0; i < nodeCount; i++)
            {
                float t = (float)i / Mathf.Max(1, nodeCount - 1);
                float x = Mathf.Sin(t * _frequency * Mathf.PI * 2f) * _amplitude;

                positions.Add(new Vector2(x, currentY));
                currentY += nodeHeight + spacing;
            }

            return positions;
        }
    }
}
