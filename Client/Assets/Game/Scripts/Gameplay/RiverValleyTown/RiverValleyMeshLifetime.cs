using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>场景退出时释放临时外景网格。</summary>
    internal sealed class RiverValleyMeshLifetime : MonoBehaviour
    {
        private void OnDestroy()
        {
            if (TryGetComponent<MeshFilter>(out var filter))
                {
                if (Application.isPlaying) Destroy(filter.sharedMesh);
                else DestroyImmediate(filter.sharedMesh);
            }
        }
    }
}
