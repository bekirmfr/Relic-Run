using System.Collections.Generic;
using UnityEngine;

namespace GameLift.UI.LevelPath
{
    public class DefaultPipeGenerator : IPipeGenerator
    {
        private const int SmoothSegments = 30;

        public void GeneratePipes(
            List<Vector2> positions,
            RectTransform contentRoot,
            PipeSettings settings,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            for (int i = 0; i < positions.Count - 1; i++)
            {
                Vector2 from = positions[i];
                Vector2 to = positions[i + 1];

                bool isAligned = Mathf.Approximately(from.x, to.x) || Mathf.Approximately(from.y, to.y);
                List<Vector2> points = (settings.Mode == PipeMode.Smooth && !isAligned)
                    ? BuildSmoothPoints(from, to)
                    : BuildFlatPoints(from, to);

                CreatePipeObject(contentRoot, points, settings.Width, settings.Color, anchorMin, anchorMax, i);
            }
        }

        private static List<Vector2> BuildFlatPoints(Vector2 from, Vector2 to)
        {
            return new List<Vector2> { from, to };
        }

        private static List<Vector2> BuildSmoothPoints(Vector2 from, Vector2 to)
        {
            List<Vector2> points = new List<Vector2>();

            Vector2 mid = (from + to) * 0.5f;
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            if (perp.y < 0)
                perp = -perp;

            float bendAmount = Vector2.Distance(from, to) * 0.2f;
            Vector2 control = mid + perp * bendAmount;

            for (int j = 0; j <= SmoothSegments; j++)
            {
                float t = (float)j / SmoothSegments;
                float u = 1f - t;
                Vector2 point = u * u * from + 2f * u * t * control + t * t * to;
                points.Add(point);
            }

            return points;
        }

        private static void CreatePipeObject(
            RectTransform parent,
            List<Vector2> points,
            float width,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int index)
        {
            GameObject pipeGo = new GameObject($"Pipe_{index}", typeof(RectTransform));
            RectTransform rect = pipeGo.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = anchorMin;
            rect.SetAsFirstSibling();

            LevelPathPipe pipe = pipeGo.AddComponent<LevelPathPipe>();
            pipe.SetPoints(points, width, color);
        }
    }
}
