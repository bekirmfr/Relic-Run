using System;
using GameLift.Pooling;
using UnityEngine;
using VContainer;

namespace GameLift.UI.LevelPath
{
    public class NodeSpawner
    {
        private readonly IPools _pools;
        private readonly IObjectResolver _resolver;

        public NodeSpawner(IPools pools, IObjectResolver resolver)
        {
            _pools = pools;
            _resolver = resolver;
        }

        public LevelPathUINode SpawnNode(
            LevelPathUINode prefab,
            RectTransform contentRoot,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            LevelPathUINode node = _pools.Spawn(prefab);
            _resolver.Inject(node);

            RectTransform nodeRect = node.GetComponent<RectTransform>();
            nodeRect.SetParent(contentRoot, false);
            nodeRect.anchorMin = anchorMin;
            nodeRect.anchorMax = anchorMax;

            return node;
        }
    }
}
