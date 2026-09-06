using System;
using UnityEngine;

namespace GameLift.UI.LevelPath
{
    [Serializable]
    public class PipeSettings
    {
        public PipeMode Mode = PipeMode.Smooth;
        public float Width = 10f;
        public Color Color = Color.white;
    }
}
