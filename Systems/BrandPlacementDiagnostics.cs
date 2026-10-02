using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BrandTheBuilding.Models;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BrandTheBuilding.Systems
{
    // Diagnostic-only, managed memory. Never adds components or changes game state.
    public partial class BrandPlacementToolSystem
    {
        private sealed class PlacementTrace
        {
            public int Id;
            public Entity Sign, Building;
            public Game.Objects.Transform Preview;
            public float3 Anchor, Normal, Min, Max;
            public float Started;
            public int Updates, Sample;
        }

        private static readonly float[] DiagnosticSeconds = { 0f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 20f, 30f };
        private readonly List<PlacementTrace> m_PlacementTraces = new();
        private int m_NextTraceId;
        private Entity m_LastDiagnosticPreview;
        private float m_NextPreviewSample;

        private void TracePreviewLifecycle(string stage, Entity entity)
        {
            try
            {
                Mod.Log.Info($"[BTB-DIAG] preview-stage={stage} frame={UnityEngine.Time.frameCount} toolActive={IsToolActive} " +
                    $"roofYaw={DiagnosticValue(m_RoofYaw)} pendingYaw={DiagnosticValue(m_PendingRoofYaw)} " +
                    $"supportPending={m_SupportPending} offset={DiagnosticValue(m_Offset)} " +
                    $"requestedTransform={DiagnosticValue(m_FinalTransform)} " +
                    $"\nPREVIEW {DiagnosticEntity(entity)}\nBUILDING {DiagnosticEntity(m_SelectedBuilding)}");
            }
            catch (Exception exception) { DiagnosticFailure(exception); }
        }

        private void BeginPlacementDiagnostics(BrandCandidate candidate,
            Game.Objects.Transform preview, float terrain, float elevation)
        {
            try
            {
                // Bounded even when the user places many signs in quick succession.
                if (m_PlacementTraces.Count == 4)
                {
                    Mod.Log.Info($"[BTB-DIAG] trace={m_PlacementTraces[0].Id} observation-ended capacity-limit");
                    m_PlacementTraces.RemoveAt(0);
                }
                var trace = new PlacementTrace
                {
                    Id = ++m_NextTraceId, Sign = m_PreviewEntity, Building = m_SelectedBuilding,
                    Preview = preview, Anchor = m_SurfaceAnchor, Normal = m_SurfaceNormal,
                    Min = candidate.BoundsMin, Max = candidate.BoundsMax,
                    Started = UnityEngine.Time.realtimeSinceStartup
                };
                m_PlacementTraces.Add(trace);
                Mod.Log.Info($"[BTB-DIAG] trace={trace.Id} BEGIN version={ModAssemblyInfo.Version} " +
                    $"unity={Application.unityVersion} asset={candidate.Name} prefab={candidate.Prefab} " +
                    $"company={m_SelectedCompany} brand={m_SelectedBrand} buildingPrefab={m_SelectedBuildingPrefab} " +
                    $"flat={candidate.IsFlat} offset={DiagnosticValue(m_Offset)} lift={DiagnosticValue(m_SurfaceLift)} " +
                    $"clearance={DiagnosticValue(FaceClearance(candidate))} terrain={DiagnosticValue(terrain)} " +
                    $"calculatedElevation={DiagnosticValue(elevation)} anchor={DiagnosticValue(trace.Anchor)} " +
                    $"normal={DiagnosticValue(trace.Normal)} boundsMin={DiagnosticValue(trace.Min)} boundsMax={DiagnosticValue(trace.Max)} " +
                    $"requestedTransform={DiagnosticValue(m_FinalTransform)} supportPending={m_SupportPending} canPlace={m_CanPlace}");
                WritePlacementSnapshot(trace, "preview-before-any-commit-writes");
                Mod.Log.Info($"[BTB-DIAG] trace={trace.Id} prefab-state {DiagnosticEntity(candidate.Prefab)}");
            }
            catch (Exception exception) { DiagnosticFailure(exception); }
        }

        private void TracePlacementDiagnostics(Entity entity, string stage)
        {
            try
            {
                for (int i = m_PlacementTraces.Count - 1; i >= 0; i--)
                {
                    if (m_PlacementTraces[i].Sign != entity) continue;
                    WritePlacementSnapshot(m_PlacementTraces[i], stage);
                    break;
                }
            }
            catch (Exception exception) { DiagnosticFailure(exception); }
        }

        // Called by the existing UI update so observation continues after tool exit,
        // including when simulation is paused. No queries/scans and no retained native memory.
        public void UpdatePlacementDiagnostics()
        {
            try
            {
                if (m_PreviewEntity != Entity.Null && m_Mode == BrandToolMode.Editing)
                {
                    if (m_LastDiagnosticPreview != m_PreviewEntity || UnityEngine.Time.realtimeSinceStartup >= m_NextPreviewSample)
                    {
                        m_LastDiagnosticPreview = m_PreviewEntity;
                        m_NextPreviewSample = UnityEngine.Time.realtimeSinceStartup + 2f;
                        TracePreviewLifecycle("editing-sample", m_PreviewEntity);
                    }
                }
                else m_LastDiagnosticPreview = Entity.Null;
            }
            catch (Exception exception) { DiagnosticFailure(exception); }
            for (int i = m_PlacementTraces.Count - 1; i >= 0; i--)
            {
                PlacementTrace trace = m_PlacementTraces[i];
                try
                {
                    trace.Updates++;
                    float elapsed = UnityEngine.Time.realtimeSinceStartup - trace.Started;
                    bool due = elapsed >= DiagnosticSeconds[trace.Sample];
                    if (trace.Updates <= 8 || due)
                        WritePlacementSnapshot(trace, "post-place-ui-observation");
                    if (due) trace.Sample++;
                    if (trace.Sample == DiagnosticSeconds.Length || !EntityManager.Exists(trace.Sign))
                    {
                        Mod.Log.Info($"[BTB-DIAG] trace={trace.Id} observation-ended elapsed={DiagnosticValue(elapsed)} exists={EntityManager.Exists(trace.Sign)}");
                        m_PlacementTraces.RemoveAt(i);
                    }
                }
                catch (Exception exception)
                {
                    m_PlacementTraces.RemoveAt(i);
                    DiagnosticFailure(exception);
                }
            }
        }

        private void WritePlacementSnapshot(PlacementTrace trace, string stage)
        {
            var details = new StringBuilder();
            int childReferences = 0;
            if (EntityManager.Exists(trace.Building) && EntityManager.HasBuffer<Game.Objects.SubObject>(trace.Building))
            {
                var children = EntityManager.GetBuffer<Game.Objects.SubObject>(trace.Building, true);
                for (int i = 0; i < children.Length; i++)
                    if (children[i].m_SubObject == trace.Sign) childReferences++;
            }
            bool signExists = EntityManager.Exists(trace.Sign);
            bool ownerMatches = signExists && EntityManager.HasComponent<Game.Common.Owner>(trace.Sign) &&
                EntityManager.GetComponentData<Game.Common.Owner>(trace.Sign).m_Owner == trace.Building;
            details.Append($" ownerMatchesBuilding={ownerMatches} buildingChildReferences={childReferences} " +
                $"overridden={(signExists && EntityManager.HasComponent<Game.Common.Overridden>(trace.Sign))} " +
                $"deleted={(signExists && EntityManager.HasComponent<Game.Common.Deleted>(trace.Sign))}");
            if (EntityManager.Exists(trace.Building) && EntityManager.HasBuffer<Game.Objects.SubObject>(trace.Building))
            {
                var children = EntityManager.GetBuffer<Game.Objects.SubObject>(trace.Building, true);
                details.Append($" buildingChildCount={children.Length} firstChildIds=[");
                for (int i = 0; i < math.min(children.Length, 64); i++)
                    details.Append(children[i].m_SubObject).Append(';');
                details.Append("]");
            }
            if (EntityManager.Exists(trace.Sign) && EntityManager.HasComponent<Game.Objects.Transform>(trace.Sign))
            {
                var transform = EntityManager.GetComponentData<Game.Objects.Transform>(trace.Sign);
                float3 delta = transform.m_Position - trace.Preview.m_Position;
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                for (int corner = 0; corner < 8; corner++)
                {
                    float3 local = new float3((corner & 1) == 0 ? trace.Min.x : trace.Max.x,
                        (corner & 2) == 0 ? trace.Min.y : trace.Max.y,
                        (corner & 4) == 0 ? trace.Min.z : trace.Max.z);
                    float distance = math.dot(transform.m_Position + math.rotate(transform.m_Rotation, local) - trace.Anchor, trace.Normal);
                    min = math.min(min, distance);
                    max = math.max(max, distance);
                }
                details.Append($" positionDelta={DiagnosticValue(delta)} rotationDelta={DiagnosticValue(transform.m_Rotation.value - trace.Preview.m_Rotation.value)}");
                details.Append($" boundsSignedDistanceToOriginalWall=[{DiagnosticValue(min)},{DiagnosticValue(max)}] (negative=behind; bounds-not-artwork)");
            }
            Mod.Log.Info($"[BTB-DIAG] trace={trace.Id} stage={stage} frame={UnityEngine.Time.frameCount} updates={trace.Updates} " +
                $"elapsed={DiagnosticValue(UnityEngine.Time.realtimeSinceStartup - trace.Started)} toolActive={IsToolActive} mode={m_Mode} applyMode={applyMode}" +
                details + $"\nSIGN {DiagnosticEntity(trace.Sign)}\nBUILDING {DiagnosticEntity(trace.Building)}");
        }

        private string DiagnosticEntity(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity)) return $"{entity} missing";
            var result = new StringBuilder(entity.ToString());
            using NativeArray<ComponentType> types = EntityManager.GetComponentTypes(entity, Allocator.Temp);
            // Inventory all component names; inspect only relevant value components.
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i].GetManagedType();
                result.Append(" | ").Append(type?.FullName ?? types[i].ToString());
                if (type == null || !type.IsValueType || !typeof(IComponentData).IsAssignableFrom(type)) continue;
                string ns = type.Namespace ?? string.Empty;
                if (ns != "Game.Objects" && ns != "Game.Rendering" && ns != "Game.Common" &&
                    ns != "Game.Tools" && type.Name != "PrefabRef" && type.Name != "ObjectGeometryData" &&
                    type.Name != "PlaceableObjectData") continue;
                try
                {
                    // Version-tolerant reads for rendering/attachment data. No setters,
                    // property getters, buffer access or assumptions about field layout.
                    object value = typeof(BrandPlacementToolSystem)
                        .GetMethod(nameof(ReadDiagnosticComponent), BindingFlags.NonPublic | BindingFlags.Instance)
                        .MakeGenericMethod(type).Invoke(this, new object[] { entity });
                    result.Append('=').Append(DiagnosticValue(value));
                }
                catch (Exception exception)
                {
                    result.Append("=<read failed: ").Append(exception.GetBaseException().Message).Append('>');
                }
            }
            return result.ToString();
        }

        private object ReadDiagnosticComponent<T>(Entity entity) where T : unmanaged, IComponentData
            => EntityManager.GetComponentData<T>(entity);

        private static string DiagnosticValue(object value, int depth = 0)
        {
            if (value == null) return "null";
            Type type = value.GetType();
            if (value is float number) return number.ToString("R", CultureInfo.InvariantCulture);
            if (type.IsPrimitive || type.IsEnum || value is string || value is Entity) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (depth >= 3) return value.ToString();
            var text = new StringBuilder("{");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                text.Append(field.Name).Append('=').Append(DiagnosticValue(field.GetValue(value), depth + 1)).Append(';');
            return text.Append('}').ToString();
        }

        private static void DiagnosticFailure(Exception exception)
        {
            // A failed observation must never cancel placement or change the tool.
            Mod.Log.Warn($"[BTB-DIAG] Diagnostic read failed (placement unchanged): {exception}");
        }
    }
}
