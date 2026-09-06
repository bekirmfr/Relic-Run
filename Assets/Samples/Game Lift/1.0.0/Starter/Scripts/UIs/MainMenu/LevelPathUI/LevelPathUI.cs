using System.Collections.Generic;
using GameLift.Levels;
using GameLift.Pooling;
using UnityEngine;
using VContainer;

namespace GameLift.UI.LevelPath
{
    public class LevelPathUI : MonoBehaviour
    {
        [Header("Level Path Settings")]
        [SerializeField] private LevelPathUINode _nodePrefab;
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private int _nodeCount = 10;
        [SerializeField] private float _nodeSpacing = 20f;
        [SerializeField] private NodeAnchorPreset _nodeAnchor = NodeAnchorPreset.BottomCenter;

        [Header("Pipe Settings")]
        [SerializeField] private PipeSettings _pipeSettings;

        private LevelService<BaseLevelData> _levelService;
        private NodeSpawner _nodeSpawner;
        private ILevelPathGenerator _pathGenerator;
        private IPipeGenerator _pipeGenerator;

        [Inject]
        private void Construct(IPools pools, LevelService<BaseLevelData> levelService, IObjectResolver resolver)
        {
            _levelService = levelService;
            _nodeSpawner = new NodeSpawner(pools, resolver);
            //_pathGenerator = new SineWaveLevelGenerator(amplitude: 150f, frequency: 1f);
            _pathGenerator = new LinearLevelPathGenerator();
            _pipeGenerator = new DefaultPipeGenerator();
        }

        private void Start()
        {
            GenerateLevelPath();
        }

        private void GenerateLevelPath()
        {
            int levelStartIndex = _levelService.CurrentLevel;

            var (anchorMin, anchorMax) = _nodeAnchor.GetAnchorValues();

            // Spawn a temporary node to get the height, then use it as the first node
            LevelPathUINode firstNode = _nodeSpawner.SpawnNode(_nodePrefab, _contentRoot, anchorMin, anchorMax);
            firstNode.Initialize(levelStartIndex, null, isNextLevel: true);

            float nodeHeight = firstNode.GetComponent<RectTransform>().rect.height;

            // Calculate positions
            List<Vector2> positions = _pathGenerator.CalculatePositions(_nodeCount, _nodeSpacing, nodeHeight);

            // Place first node and spawn the rest
            firstNode.GetComponent<RectTransform>().anchoredPosition = positions[0];
            List<LevelPathUINode> nodes = new List<LevelPathUINode> { firstNode };

            for (int i = 1; i < positions.Count; i++)
            {
                int levelIndex = levelStartIndex + i;
                LevelPathUINode node = _nodeSpawner.SpawnNode(_nodePrefab, _contentRoot, anchorMin, anchorMax);
                node.Initialize(levelIndex, null);

                node.GetComponent<RectTransform>().anchoredPosition = positions[i];
                nodes.Add(node);
            }

            // Generate pipes
            _pipeGenerator.GeneratePipes(positions, _contentRoot, _pipeSettings, anchorMin, anchorMax);
        }
    }
}
