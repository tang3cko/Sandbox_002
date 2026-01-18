using UnityEngine;

namespace Sandbox_002.VAT
{
    /// <summary>
    /// Stores baking metadata for VAT playback.
    /// </summary>
    [System.Serializable]
    public class VATData
    {
        public int vertexCount;
        public int frameCount;
        public float duration;
        public Vector3 positionMin;
        public Vector3 positionMax;
    }
}
