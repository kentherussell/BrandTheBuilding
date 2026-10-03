using Unity.Mathematics;

namespace BrandTheBuilding.Models
{
    internal static class BrandPlacementMath
    {
        internal const float DefaultOffset = -0.10f;
        internal const float MaximumHorizontalNormalY = 0.55f;

        internal static float ClampOffset(float value) =>
            math.round(math.clamp(value, -10f, 3f) * 100f) / 100f;

        internal static float InitialOffset(float depth) =>
            math.max(DefaultOffset, -math.floor(depth * 100f) / 100f);

        internal static bool IsRoof(float3 normal) => normal.y > MaximumHorizontalNormalY;

        internal static quaternion Rotation(float3 normal, quaternion buildingRotation, float roofYaw)
        {
            if (IsRoof(normal))
            {
                // Project the building heading onto the roof before applying yaw.
                float3 forward = math.rotate(buildingRotation, new float3(0f, 0f, 1f));
                forward = math.normalizesafe(forward - normal * math.dot(forward, normal));
                return math.mul(quaternion.AxisAngle(normal, roofYaw),
                    quaternion.LookRotationSafe(forward, normal));
            }

            float3 up = math.normalizesafe(new float3(0f, 1f, 0f) - normal * normal.y,
                new float3(0f, 1f, 0f));
            return quaternion.LookRotationSafe(normal, up);
        }

        internal static float3 Position(
            float3 anchor, float3 normal, quaternion rotation,
            float3 boundsMin, float3 boundsMax, float clearance)
        {
            float3 center = (boundsMin + boundsMax) * 0.5f;
            float3 localOffset = IsRoof(normal)
                ? new float3(-center.x, clearance - boundsMin.y, -center.z)
                : new float3(-center.x, -center.y, clearance - boundsMin.z);
            return anchor + math.rotate(rotation, localOffset);
        }
    }
}
