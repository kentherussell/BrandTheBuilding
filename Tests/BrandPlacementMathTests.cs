using BrandTheBuilding.Models;
using Unity.Mathematics;
using Xunit;

namespace BrandTheBuilding.Tests
{
    public class BrandPlacementMathTests
    {
        [Theory]
        [InlineData(-20f, -10f)]
        [InlineData(4f, 3f)]
        [InlineData(-10f, -10f)]
        [InlineData(3f, 3f)]
        [InlineData(-2.756f, -2.76f)]
        [InlineData(0.054f, 0.05f)]
        public void Offset_is_limited_to_the_supported_range_and_centimeters(float value, float expected)
        {
            Assert.Equal(expected, BrandPlacementMath.ClampOffset(value));
        }

        [Theory]
        [InlineData(0.8f, -0.10f)]
        [InlineData(0.1f, -0.10f)]
        [InlineData(0.039f, -0.03f)]
        [InlineData(0.005f, 0f)]
        [InlineData(0f, 0f)]
        public void Initial_offset_does_not_bury_a_thin_sign(float depth, float expected)
        {
            float offset = BrandPlacementMath.InitialOffset(depth);

            Assert.Equal(expected, offset);
            Assert.True(depth + offset >= 0f, "The front face must remain visible.");
        }

        [Fact]
        public void Roof_threshold_keeps_steep_surfaces_in_wall_mode()
        {
            Assert.False(BrandPlacementMath.IsRoof(new float3(0f, 0.55f, 0f)));
            Assert.True(BrandPlacementMath.IsRoof(new float3(0f, 0.551f, 0f)));
        }

        [Theory]
        [InlineData(0f, 0f, 1f)]
        [InlineData(1f, 0f, 0f)]
        [InlineData(0f, 0.4f, -1f)]
        public void Wall_sign_faces_outward_and_centers_its_nearest_geometry(float x, float y, float z)
        {
            float3 normal = math.normalize(new float3(x, y, z));
            float3 anchor = new float3(42f, 18f, -7f);
            float3 min = new float3(-2f, -0.5f, -0.2f);
            float3 max = new float3(5f, 3.5f, 0.8f);
            quaternion rotation = BrandPlacementMath.Rotation(normal, quaternion.identity, 0f);
            float3 position = BrandPlacementMath.Position(anchor, normal, rotation, min, max, 0.04f);

            Near(normal, math.rotate(rotation, new float3(0f, 0f, 1f)));
            float3 faceCenter = new float3(1.5f, 1.5f, min.z);
            Near(anchor + normal * 0.04f, position + math.rotate(rotation, faceCenter));
            Assert.True(math.dot(math.rotate(rotation, new float3(0f, 1f, 0f)), new float3(0f, 1f, 0f)) > 0f);
        }

        [Fact]
        public void Flat_roof_uses_the_building_heading()
        {
            quaternion building = quaternion.RotateY(math.PI / 2f);
            quaternion rotation = BrandPlacementMath.Rotation(new float3(0f, 1f, 0f), building, 0f);

            Near(new float3(1f, 0f, 0f), math.rotate(rotation, new float3(0f, 0f, 1f)));
            Near(new float3(0f, 1f, 0f), math.rotate(rotation, new float3(0f, 1f, 0f)));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(90f)]
        [InlineData(-90f)]
        [InlineData(180f)]
        [InlineData(720f)]
        public void Rotating_on_a_sloped_roof_keeps_the_base_at_the_anchor(float degrees)
        {
            float3 normal = math.normalize(new float3(0.5f, 1f, 0.4f));
            float3 anchor = new float3(-30f, 12f, 80f);
            float3 min = new float3(-1f, -0.2f, -2f);
            float3 max = new float3(3.4f, 4f, 0.4f);
            quaternion rotation = BrandPlacementMath.Rotation(normal, quaternion.RotateY(0.7f), math.radians(degrees));
            float3 position = BrandPlacementMath.Position(anchor, normal, rotation, min, max, 0.04f);

            Near(normal, math.rotate(rotation, new float3(0f, 1f, 0f)));
            float3 baseCenter = new float3(1.2f, min.y, -0.8f);
            Near(anchor + normal * 0.04f, position + math.rotate(rotation, baseCenter));
        }

        [Fact]
        public void Positive_roof_yaw_turns_the_sign_in_the_requested_direction()
        {
            quaternion rotation = BrandPlacementMath.Rotation(
                new float3(0f, 1f, 0f), quaternion.identity, math.PI / 2f);

            Near(new float3(1f, 0f, 0f), math.rotate(rotation, new float3(0f, 0f, 1f)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Changing_clearance_moves_only_along_the_surface_normal(bool roof)
        {
            float3 normal = roof ? math.normalize(new float3(0.3f, 1f, 0.2f)) : new float3(1f, 0f, 0f);
            quaternion rotation = BrandPlacementMath.Rotation(normal, quaternion.identity, 0.6f);
            float3 min = new float3(-2f, -1f, -0.2f);
            float3 max = new float3(4f, 3f, 0.6f);
            float3 inset = BrandPlacementMath.Position(float3.zero, normal, rotation, min, max, -2.75f);
            float3 raised = BrandPlacementMath.Position(float3.zero, normal, rotation, min, max, 0.25f);

            Near(normal * 3f, raised - inset);
        }

        private static void Near(float3 expected, float3 actual)
        {
            Assert.True(math.distance(expected, actual) < 0.0001f, $"Expected {expected}, got {actual}");
        }
    }
}
