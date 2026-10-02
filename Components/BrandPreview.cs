using Unity.Entities;

namespace BrandTheBuilding.Components
{
    /// <summary>
    /// Session-only marker. It is only attached to a Temp preview and is never
    /// copied to the committed prop.
    /// </summary>
    public struct BrandPreview : IComponentData
    {
    }
}
