using System.Collections.Generic;
using UnityEngine;

namespace GameLift.UI.LevelPath
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class LevelPathPipe : MonoBehaviour
    {
        private CanvasRenderer _canvasRenderer;

        private void Awake()
        {
            _canvasRenderer = GetComponent<CanvasRenderer>();
        }

        public void SetPoints(List<Vector2> points, float width, Color pipeColor)
        {
            _canvasRenderer.SetMaterial(Canvas.GetDefaultCanvasMaterial(), null);
            _canvasRenderer.SetColor(pipeColor);
            _canvasRenderer.SetMesh(BuildMesh(points, width));
        }

        private Mesh BuildMesh(List<Vector2> points, float width)
        {
            Mesh mesh = new Mesh();

            if (points.Count < 2)
                return mesh;

            int segmentCount = points.Count - 1;
            Vector3[] vertices = new Vector3[segmentCount * 4];
            int[] triangles = new int[segmentCount * 6];

            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];

                Vector2 dir = (b - a).normalized;
                Vector2 perp = new Vector2(-dir.y, dir.x) * (width * 0.5f);

                int vi = i * 4;
                vertices[vi] = a + perp;
                vertices[vi + 1] = a - perp;
                vertices[vi + 2] = b - perp;
                vertices[vi + 3] = b + perp;

                int ti = i * 6;
                triangles[ti] = vi;
                triangles[ti + 1] = vi + 1;
                triangles[ti + 2] = vi + 2;
                triangles[ti + 3] = vi;
                triangles[ti + 4] = vi + 2;
                triangles[ti + 5] = vi + 3;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            return mesh;
        }
    }
}
