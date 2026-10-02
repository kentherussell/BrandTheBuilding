using Unity.Entities;
using Unity.Mathematics;

namespace BrandTheBuilding.Models
{
    public enum BrandToolMode
    {
        Inactive = 0,
        Targeting = 1,
        Editing = 2
    }

    internal sealed class BrandCandidate
    {
        public Entity Prefab;
        public string Name = string.Empty;
        public float3 BoundsMin;
        public float3 BoundsMax;
        public bool IsFlat;
        public float Score;
    }
}
