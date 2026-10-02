using BrandTheBuilding.Components;
using BrandTheBuilding.Models;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using SubObject = Game.Objects.SubObject;

namespace BrandTheBuilding.Systems
{
    /// <summary>
    /// Three-state, one-placement custom tool. The only custom component created
    /// by this mod is attached to a Temp preview. Place removes that marker
    /// and lets the game's normal tool-apply pass commit the visible preview.
    /// </summary>
    public partial class BrandPlacementToolSystem : ToolBaseSystem
    {
        private const float BillboardFaceClearance = 0.04f;
        private const float FlatFaceClearance = 0.03f;
        private const float MinimumOffset = -10f;
        private const float MaximumOffset = 3f;
        private const float DefaultOffset = -0.10f;
        private const float OffsetStep = 0.05f;
        private const float MaximumHorizontalNormalY = 0.55f;
        private const float SupportProbeReach = 0.35f;
        private const int SupportProbeCount = 5;
        private const int SupportTimeoutFrames = 4;
        private const int CommitTimeoutFrames = 4;
        private const int MaximumOwnerDepth = 8;

        private TerrainSystem m_TerrainSystem;
        private NameSystem m_NameSystem;
        private OverlayRenderSystem m_OverlaySystem;
        private Game.Common.RaycastSystem m_SurfaceRaycastSystem;
        private EntityQuery m_BrandObjectQuery;
        private EntityQuery m_PreviewQuery;
        private InputAction m_ApplyAction;
        private InputAction m_CancelAction;

        private BrandToolMode m_Mode;
        private Entity m_HighlightedBuilding;
        private bool m_AddedHighlight;
        private Entity m_SelectedBuilding;
        private Entity m_SelectedBuildingPrefab;
        private Game.Objects.Transform m_SelectedBuildingTransform;
        private Entity m_SelectedCompany;
        private Entity m_SelectedBrand;
        private Entity m_PreviewEntity;
        private Entity m_CommittingEntity;
        private int m_CommitWaitFrames;
        private Game.Objects.Transform m_CommitTransform;
        private Elevation m_CommitElevation;
        private readonly List<BrandCandidate> m_Candidates = new();
        private int m_CandidateIndex;
        private bool m_IsDragging;
        private bool m_IsRotating;
        private float m_RoofYaw;
        private float m_PendingRoofYaw;
        private float m_RotationDelta;
        private Game.Input.InputBarrier m_RotationCameraBarrier;
        private bool m_IsPointerOverUI;
        private bool m_PlaceRequested;
        private bool m_CancelRequested;
        private int m_AssetStepRequested;
        private float m_Offset;
        private bool m_OffsetChanged;
        private bool m_HasAnchor;
        private float3 m_SurfaceAnchor;
        private float3 m_SurfaceNormal;
        private float m_SurfaceLift;
        private object m_SupportContext;
        private readonly float3[] m_SupportPoints = new float3[SupportProbeCount];
        private bool m_SupportPending;
        private bool m_HasSurfaceAttempt;
        private bool m_RetainValidSurface;
        private int m_SupportWaitFrames;
        private float3 m_PendingAnchor;
        private float3 m_PendingNormal;
        private Game.Objects.Transform m_FinalTransform;
        private bool m_CanPlace;
        private string m_HoverText = string.Empty;
        private string m_StatusText = string.Empty;
        private string m_BuildingName = string.Empty;
        private string m_TargetingFeedback = string.Empty;
        private int m_TargetingFeedbackFrames;

        public override string toolID => "BrandTheBuildingTool";
        public bool IsToolActive => m_ToolSystem.activeTool == this;
        public bool IsEditing => m_Mode == BrandToolMode.Editing;
        public string HoverText => m_HoverText;
        public string StatusText => m_StatusText;
        public string BuildingName => m_BuildingName;
        public bool CanPlace => m_CanPlace && !m_SupportPending && !m_IsRotating && m_RotationDelta == 0f && m_CommittingEntity == Entity.Null;
        public int AssetIndex => m_Candidates.Count == 0 ? 0 : m_CandidateIndex + 1;
        public int AssetCount => m_Candidates.Count;
        public string AssetName => CurrentCandidate?.Name ?? string.Empty;
        public float Offset => m_Offset;
        private BrandCandidate CurrentCandidate =>
            m_CandidateIndex >= 0 && m_CandidateIndex < m_Candidates.Count
                ? m_Candidates[m_CandidateIndex]
                : null;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_OverlaySystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_SurfaceRaycastSystem = World.GetOrCreateSystemManaged<Game.Common.RaycastSystem>();
            m_BrandObjectQuery = GetEntityQuery(
                ComponentType.ReadOnly<BrandObjectData>(),
                ComponentType.ReadOnly<ObjectData>(),
                ComponentType.ReadOnly<ObjectRequirementElement>());
            m_PreviewQuery = GetEntityQuery(
                ComponentType.ReadOnly<BrandPreview>(),
                ComponentType.Exclude<Deleted>());

            m_ApplyAction = new InputAction(
                "BrandTheBuildingApply",
                InputActionType.Button,
                "<Mouse>/leftButton");

            m_CancelAction = new InputAction(
                "BrandTheBuildingCancel",
                InputActionType.Button,
                "<Keyboard>/escape");

            ResetSessionState();
            m_RotationCameraBarrier = Game.Input.InputManager.instance.CreateActionBarrier(
                "Camera", "Rotate", "BrandTheBuildingRoofRotation");
            Enabled = false;
            Mod.Log.Info("Brand placement tool created.");
        }

        protected override void OnDestroy()
        {
            StopRoofRotation();
            m_RotationCameraBarrier?.Dispose();
            m_RotationCameraBarrier = null;
            DestroyPreview();
            SetHighlighted(Entity.Null);

            m_ApplyAction?.Dispose();
            m_ApplyAction = null;
            m_CancelAction?.Dispose();
            m_CancelAction = null;

            base.OnDestroy();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            ResetSessionState();
            m_Mode = BrandToolMode.Targeting;
            m_StatusText = "Choose a building with a company.";
            m_ApplyAction?.Enable();
            m_CancelAction?.Enable();
        }

        protected override void OnStopRunning()
        {
            m_ApplyAction?.Disable();
            m_CancelAction?.Disable();
            DestroyPreview();
            SetHighlighted(Entity.Null);
            ResetSessionState();
            base.OnStopRunning();
        }

        public void ActivateTool()
        {
            if (m_ToolSystem.activeTool == this)
            {
                return;
            }

            Enabled = true;
            m_ToolSystem.selected = Entity.Null;
            m_ToolSystem.activeTool = this;
        }

        public void RequestCancel()
        {
            m_CancelRequested = true;
        }

        public void RequestPlace()
        {
            if (m_CommittingEntity == Entity.Null)
            {
                m_PlaceRequested = true;
            }
        }

        public void RequestPreviousAsset()
        {
            m_AssetStepRequested = -1;
        }

        public void RequestNextAsset()
        {
            m_AssetStepRequested = 1;
        }

        public void SetOffset(float value)
        {
            if (m_Mode != BrandToolMode.Editing || m_CommittingEntity != Entity.Null || !math.isfinite(value))
            {
                return;
            }

            float next = math.round(math.clamp(value, MinimumOffset, MaximumOffset) * 100f) / 100f;
            if (next != m_Offset)
            {
                m_Offset = next;
                // UI callbacks only queue changes. ECS writes belong to the tool update.
                m_OffsetChanged = true;
            }
        }

        public void StepOffset(int direction)
        {
            if (direction != 0)
            {
                SetOffset(m_Offset + math.clamp(direction, -10, 10) * OffsetStep);
            }
        }

        private void ReapplyPreviewTransform()
        {
            if (m_HasAnchor && CurrentCandidate != null)
            {
                ApplyPreviewTransform(m_SurfaceAnchor, m_SurfaceNormal, CurrentCandidate);
            }
        }

        public void SetPointerOverUI(bool value)
        {
            m_IsPointerOverUI = value;
            if (value)
            {
                m_IsDragging = false;
                StopRoofRotation();
            }
        }

        private void DeactivateTool()
        {
            DestroyPreview();
            SetHighlighted(Entity.Null);
            m_ToolSystem.selected = Entity.Null;

            if (m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }

            Enabled = false;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects;
            m_ToolRaycastSystem.collisionMask =
                CollisionMask.OnGround | CollisionMask.Overground;
            m_ToolRaycastSystem.raycastFlags = RaycastFlags.SubElements;
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            try
            {
                // Place has already been explicitly requested. Finish that one
                // transaction before handling more input or releasing the tool.
                if (m_CommittingEntity != Entity.Null)
                {
                    FinishPlacement();
                    return inputDeps;
                }

                if (m_CancelRequested ||
                    (m_CancelAction != null && m_CancelAction.WasPressedThisFrame()) ||
                    (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
                {
                    m_CancelRequested = false;
                    DeactivateTool();
                    return inputDeps;
                }

                if (m_Mode == BrandToolMode.Editing)
                {
                    return UpdateEditing(inputDeps);
                }

                return UpdateTargeting(inputDeps);
            }
            catch (Exception exception)
            {
                Mod.Log.Error(exception);
                DeactivateTool();
                return inputDeps;
            }
        }

        private JobHandle UpdateTargeting(JobHandle inputDeps)
        {
            m_CanPlace = false;
            m_HoverText = string.Empty;

            if (m_TargetingFeedbackFrames > 0)
            {
                m_TargetingFeedbackFrames--;
                m_HoverText = m_TargetingFeedback;
            }

            if (!GetRaycastResult(out Entity hitEntity, out RaycastHit hit))
            {
                SetHighlighted(Entity.Null);
                return inputDeps;
            }

            Entity building = ResolveBuilding(hitEntity);
            if (!IsUsableBuilding(building))
            {
                SetHighlighted(Entity.Null);
                return inputDeps;
            }

            CompanyResolution resolution = ResolveCompany(building, logUnexpectedMultiple: false);

            if (!resolution.Success)
            {
                SetHighlighted(Entity.Null);
                m_HoverText = resolution.MultipleCompanies
                    ? "Can't apply branding: multiple companies were found."
                    : resolution.CompanyCount == 1
                        ? "Can't apply branding: this company has no current brand."
                        : "Can't apply branding to a building without a company.";
                return inputDeps;
            }

            SetHighlighted(building);
            if (m_TargetingFeedbackFrames <= 0)
            {
                m_HoverText = "Add Company Branding Here";
            }

            if (!m_IsPointerOverUI &&
                m_ApplyAction != null &&
                m_ApplyAction.WasPressedThisFrame())
            {
                BeginEditing(building, resolution, hit);
            }

            return inputDeps;
        }

        private JobHandle UpdateEditing(JobHandle inputDeps)
        {
            if (m_PreviewEntity == Entity.Null ||
                !EntityManager.Exists(m_PreviewEntity) ||
                !EntityManager.HasComponent<BrandPreview>(m_PreviewEntity))
            {
                Mod.Log.Warn("Branding preview disappeared before Place; cancelling.");
                DeactivateTool();
                return inputDeps;
            }

            if (!IsUsableBuilding(m_SelectedBuilding) ||
                !EntityManager.TryGetComponent(m_SelectedBuilding, out PrefabRef currentBuildingPrefab) ||
                currentBuildingPrefab.m_Prefab != m_SelectedBuildingPrefab ||
                !EntityManager.TryGetComponent(m_SelectedBuilding, out Game.Objects.Transform buildingTransform) ||
                !math.all(buildingTransform.m_Position == m_SelectedBuildingTransform.m_Position) ||
                !math.all(buildingTransform.m_Rotation.value == m_SelectedBuildingTransform.m_Rotation.value))
            {
                Mod.Log.Warn("Selected building was removed or changed while editing; cancelling.");
                DeactivateTool();
                return inputDeps;
            }

            CompanyResolution resolution = ResolveCompany(
                m_SelectedBuilding,
                logUnexpectedMultiple: true);

            if (!resolution.Success ||
                resolution.Company != m_SelectedCompany ||
                resolution.Brand != m_SelectedBrand)
            {
                Mod.Log.Warn("Selected building or company changed while editing; cancelling.");
                DeactivateTool();
                return inputDeps;
            }

            BrandCandidate currentCandidate = CurrentCandidate;
            if (currentCandidate == null ||
                !EntityManager.Exists(currentCandidate.Prefab) ||
                !EntityManager.HasComponent<ObjectData>(currentCandidate.Prefab))
            {
                Mod.Log.Warn("Selected branding prefab became unavailable; cancelling.");
                DeactivateTool();
                return inputDeps;
            }

            CompleteSurfaceValidation();

            if (m_AssetStepRequested != 0)
            {
                int step = m_AssetStepRequested;
                m_AssetStepRequested = 0;
                if (!SelectAsset(step))
                {
                    return inputDeps;
                }
            }

            if (m_OffsetChanged)
            {
                m_OffsetChanged = false;
                ReapplyPreviewTransform();
            }

            UpdateRoofRotation();

            if (m_PlaceRequested)
            {
                m_PlaceRequested = false;
                if (CanPlace)
                {
                    CommitPlacement();
                    return inputDeps;
                }
            }

            if (m_IsPointerOverUI || m_IsRotating || m_RotationDelta != 0f)
            {
                m_IsDragging = false;
                DrawPreviewOutline();
                return inputDeps;
            }

            if (m_ApplyAction != null && m_ApplyAction.WasPressedThisFrame())
            {
                m_IsDragging = true;
            }

            if (m_ApplyAction != null && m_ApplyAction.WasReleasedThisFrame())
            {
                m_IsDragging = false;
            }

            if (m_IsDragging && !m_SupportPending &&
                GetRaycastResult(out Entity hitEntity, out RaycastHit hit) &&
                ResolveBuilding(hitEntity) == m_SelectedBuilding)
            {
                if (TryReadBuildingSurfaceHit(hit, out float3 anchor, out float3 normal))
                {
                    if (!m_HasSurfaceAttempt ||
                        !math.all(anchor == m_PendingAnchor) ||
                        !math.all(normal == m_PendingNormal))
                    {
                        TryMovePreview(anchor, normal, retainLastValidOnFailure: true);
                    }
                }
            }

            DrawPreviewOutline();
            return inputDeps;
        }

        private void BeginEditing(
            Entity building,
            CompanyResolution resolution,
            RaycastHit hit)
        {
            if (!TryReadBuildingSurfaceHit(hit, out float3 anchor, out float3 normal))
            {
                m_TargetingFeedback = "Choose a building wall or an upward-facing roof.";
                m_TargetingFeedbackFrames = 180;
                m_HoverText = m_TargetingFeedback;
                return;
            }

            if (!EntityManager.TryGetComponent(building, out Game.Objects.Transform buildingTransform) ||
                !EntityManager.TryGetComponent(building, out PrefabRef buildingPrefabRef))
            {
                m_TargetingFeedback = "This building's geometry is unavailable for placement.";
                m_TargetingFeedbackFrames = 180;
                m_HoverText = m_TargetingFeedback;
                return;
            }

            LoadBrandCandidates(resolution.Brand);
            if (m_Candidates.Count == 0)
            {
                m_StatusText = "No compatible branding assets are available for this company.";
                m_TargetingFeedback = m_StatusText;
                m_TargetingFeedbackFrames = 180;
                m_HoverText = m_TargetingFeedback;
                return;
            }

            m_SelectedBuilding = building;
            m_SelectedBuildingPrefab = buildingPrefabRef.m_Prefab;
            m_SelectedBuildingTransform = buildingTransform;
            m_SelectedCompany = resolution.Company;
            m_SelectedBrand = resolution.Brand;
            m_BuildingName = BuildingUtils.GetAddress(EntityManager, building, out Entity road, out int number)
                ? $"{number} {m_NameSystem.GetRenderedLabelName(road)}"
                : m_NameSystem.GetRenderedLabelName(building);
            if (string.IsNullOrWhiteSpace(m_BuildingName))
            {
                m_BuildingName = SafePrefabName(buildingPrefabRef.m_Prefab);
            }
            m_CandidateIndex = 0;
            // Start thick billboards 10 cm closer. Thin signs must retain a
            // visible face; rounding toward zero avoids burying sub-cm geometry.
            float depth = CurrentCandidate.BoundsMax.z - CurrentCandidate.BoundsMin.z;
            m_Offset = math.max(DefaultOffset, -math.floor(depth * 100f) / 100f);
            m_Mode = BrandToolMode.Editing;
            m_IsDragging = false;
            m_HoverText = string.Empty;
            SetHighlighted(Entity.Null);

            CreatePreview();
            if (m_PreviewEntity == Entity.Null)
            {
                Mod.Log.Warn("Branding preview could not be created; cancelling.");
                DeactivateTool();
                return;
            }
            TryMovePreview(anchor, normal, retainLastValidOnFailure: false);
        }

        private bool SelectAsset(int step)
        {
            if (m_Candidates.Count == 0 || step == 0)
            {
                return true;
            }

            m_CandidateIndex =
                (m_CandidateIndex + step + m_Candidates.Count) % m_Candidates.Count;

            DestroyPreview();
            CreatePreview();
            if (m_PreviewEntity == Entity.Null)
            {
                Mod.Log.Warn("The selected branding asset could not create a preview; cancelling.");
                DeactivateTool();
                return false;
            }

            if (m_HasAnchor)
            {
                TryMovePreview(
                    m_SurfaceAnchor,
                    m_SurfaceNormal,
                    retainLastValidOnFailure: false);
            }

            return true;
        }

        private void CommitPlacement()
        {
            BrandCandidate candidate = CurrentCandidate;
            if (candidate == null ||
                !EntityManager.Exists(candidate.Prefab) ||
                !EntityManager.HasComponent<ObjectData>(candidate.Prefab))
            {
                Mod.Log.Warn("Selected branding prefab disappeared before placement; cancelling.");
                DeactivateTool();
                return;
            }

            if (m_PreviewEntity == Entity.Null ||
                !EntityManager.Exists(m_PreviewEntity) ||
                !m_HasAnchor ||
                !EntityManager.TryGetComponent(m_PreviewEntity, out Game.Objects.Transform previewTransform) ||
                !math.all(math.isfinite(previewTransform.m_Position)) ||
                !math.all(math.isfinite(previewTransform.m_Rotation.value)))
            {
                Mod.Log.Warn("The final preview transform was unavailable at Place; cancelling.");
                DeactivateTool();
                return;
            }

            // Capture the live preview, not the last requested transform. Game
            // systems can adjust a Temp object after ApplyPreviewTransform; using
            // m_FinalTransform here would jump away from what the user sees.
            TerrainHeightData terrainHeightData = m_TerrainSystem.GetHeightData(waitForPending: true);
            if (!terrainHeightData.isCreated)
            {
                Mod.Log.Warn("Terrain height data was unavailable at Place; retaining preview.");
                m_StatusText = "Placement could not read terrain data. Try Place again.";
                return;
            }
            float terrainHeight = TerrainUtils.SampleHeight(
                ref terrainHeightData, previewTransform.m_Position);
            float elevation = previewTransform.m_Position.y - terrainHeight;
            if (!math.isfinite(elevation))
            {
                Mod.Log.Warn("Calculated sign elevation was invalid; cancelling.");
                DeactivateTool();
                return;
            }
            // Commit the object already rendered by the game. ApplyObjectsSystem
            // owns Temp removal and native registration; no replacement request
            // or freshly allocated rendering components are needed.
            RegisterBuildingProp(m_PreviewEntity);
            m_CommittingEntity = m_PreviewEntity;
            m_CommitWaitFrames = 0;
            m_CommitTransform = previewTransform;
            m_CommitElevation = new Elevation(elevation, (ElevationFlags)0);
            RemovePlacementAlignment(m_CommittingEntity);
            SetOrAdd(m_CommittingEntity, m_CommitElevation);
            SetOrAdd(m_CommittingEntity, new Temp(Entity.Null, TempFlags.Create));
            EntityManager.RemoveComponent<BrandPreview>(m_CommittingEntity);
            EntityManager.RemoveComponent<Highlighted>(m_CommittingEntity);
            EnsureTag<BatchesUpdated>(m_CommittingEntity);
            m_PreviewEntity = Entity.Null;
            m_IsDragging = false;
            m_StatusText = "Placing sign…";
            applyMode = ApplyMode.Apply;
        }

        private void FinishPlacement()
        {
            Entity entity = m_CommittingEntity;
            if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity))
            {
                RemoveBuildingPropReference(m_SelectedBuilding, entity);
                Mod.Log.Warn($"Native apply removed preview {entity}; returning to editing.");
                m_CommittingEntity = Entity.Null;
                CreatePreview();
                TryMovePreview(m_SurfaceAnchor, m_SurfaceNormal, retainLastValidOnFailure: false);
                m_StatusText = "The game did not keep the sign. Adjust it and try Place again.";
                return;
            }

            if (EntityManager.HasComponent<Temp>(entity))
            {
                if (++m_CommitWaitFrames < CommitTimeoutFrames)
                {
                    DrawPreviewOutline();
                    return;
                }
                // No native commit happened: keep the same visible preview.
                UnregisterTemporaryBuildingProp(entity);
                m_PreviewEntity = entity;
                m_CommittingEntity = Entity.Null;
                EnsureTag<BrandPreview>(entity);
                EnsureTag<Highlighted>(entity);
                SetOrAdd(entity, new Temp(Entity.Null, TempFlags.Create | TempFlags.Select));
                EnsureTag<BatchesUpdated>(entity);
                m_StatusText = "Placement did not complete. Try Place again or Cancel.";
                Mod.Log.Warn($"Native apply timed out for {entity}; preview retained.");
                return;
            }

            // Native apply can add Updated/Created. Prevent an alignment pass
            // from replacing the captured wall transform after we release it.
            RegisterBuildingProp(entity);
            RemovePlacementAlignment(entity);
            SetOrAdd(entity, m_CommitTransform);
            SetOrAdd(entity, m_CommitElevation);
            EnsureTag<Updated>(entity);
            EnsureTag<BatchesUpdated>(entity);
            m_CommittingEntity = Entity.Null;
            DeactivateTool();
        }

        private void RemovePlacementAlignment(Entity entity)
        {
            // These are per-instance instructions to AlignSystem and
            // AttachPositionSystem, not prefab data. Ownership is retained, but
            // this wall sign keeps its world transform without re-snapping.
            EntityManager.RemoveComponent<Aligned>(entity);
            EntityManager.RemoveComponent<Attached>(entity);
        }

        private void RegisterBuildingProp(Entity sign)
        {
            if (!EntityManager.Exists(m_SelectedBuilding) || EntityManager.HasComponent<Deleted>(m_SelectedBuilding))
                throw new InvalidOperationException("The selected building disappeared during prop registration.");

            // Instance-only native relationship; never edit the prefab's authored
            // SubObject list. Do not add Attached: that has separate alignment semantics.
            SetOrAdd(sign, new Owner(m_SelectedBuilding));
            if (!EntityManager.HasBuffer<SubObject>(m_SelectedBuilding))
                EntityManager.AddBuffer<SubObject>(m_SelectedBuilding);
            DynamicBuffer<SubObject> children = EntityManager.GetBuffer<SubObject>(m_SelectedBuilding);
            bool found = false;
            for (int i = children.Length - 1; i >= 0; i--)
            {
                if (children[i].m_SubObject != sign) continue;
                if (found) children.RemoveAt(i);
                else found = true;
            }
            if (!found) children.Add(new SubObject(sign));
        }

        private void UnregisterTemporaryBuildingProp(Entity sign)
        {
            if (!EntityManager.Exists(sign) || !EntityManager.HasComponent<Temp>(sign) ||
                !EntityManager.TryGetComponent(sign, out Owner owner)) return;
            RemoveBuildingPropReference(owner.m_Owner, sign);
            EntityManager.RemoveComponent<Owner>(sign);
        }

        private void RemoveBuildingPropReference(Entity building, Entity sign)
        {
            if (EntityManager.Exists(building) && EntityManager.HasBuffer<SubObject>(building))
            {
                DynamicBuffer<SubObject> children = EntityManager.GetBuffer<SubObject>(building);
                for (int i = children.Length - 1; i >= 0; i--)
                    if (children[i].m_SubObject == sign) children.RemoveAt(i);
            }
        }

        private void TryMovePreview(
            float3 anchor,
            float3 normal,
            bool retainLastValidOnFailure)
        {
            BrandCandidate candidate = CurrentCandidate;
            if (candidate == null || !EntityManager.Exists(candidate.Prefab))
            {
                m_CanPlace = false;
                m_StatusText = "The selected branding asset is unavailable.";
                return;
            }

            m_RetainValidSurface = retainLastValidOnFailure && m_CanPlace;
            if (!m_RetainValidSurface)
            {
                m_CanPlace = false;
                m_SurfaceLift = 0f;
                ApplyPreviewTransform(anchor, normal, candidate);
                m_SurfaceAnchor = anchor;
                m_SurfaceNormal = normal;
                m_HasAnchor = true;
            }
            QueueSurfaceValidation(anchor, normal, candidate);
        }

        private void ApplyPreviewTransform(
            float3 anchor,
            float3 normal,
            BrandCandidate candidate)
        {
            quaternion rotation = PlacementRotation(normal);

            // Bounds do not identify which depth contains the visible artwork.
            // Keep the nearest authored geometry outside the wall at zero offset,
            // including thin neon/circular signs with an off-center pivot.
            float3 center = (candidate.BoundsMin + candidate.BoundsMax) * 0.5f;
            float clearance = FaceClearance(candidate) + m_SurfaceLift + m_Offset;
            float3 localOffset = IsRoof(normal)
                ? new float3(-center.x, clearance - candidate.BoundsMin.y, -center.z)
                : new float3(-center.x, -center.y, clearance - candidate.BoundsMin.z);
            float3 position = anchor + math.rotate(rotation, localOffset);
            m_FinalTransform = new Game.Objects.Transform(position, rotation);

            if (m_PreviewEntity != Entity.Null && EntityManager.Exists(m_PreviewEntity))
            {
                SetOrAdd(m_PreviewEntity, m_FinalTransform);
                EnsureTag<BatchesUpdated>(m_PreviewEntity);
                EnsureTag<Updated>(m_PreviewEntity);
            }
        }

        private bool TryReadBuildingSurfaceHit(
            RaycastHit hit,
            out float3 anchor,
            out float3 normal)
        {
            anchor = hit.m_HitPosition;
            normal = math.normalizesafe(hit.m_HitDirection, float3.zero);
            if (!math.all(math.isfinite(anchor)) ||
                !math.all(math.isfinite(normal)) || math.lengthsq(normal) < 0.5f)
            {
                return false;
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                float3 cameraToHit = anchor - (float3)camera.transform.position;
                if (math.dot(normal, cameraToHit) > 0f)
                {
                    normal = -normal;
                }
            }

            // Accept walls and upward-facing roofs, but not undersides/ceilings.
            return normal.y >= -MaximumHorizontalNormalY;
        }

        private static bool IsRoof(float3 normal) => normal.y > MaximumHorizontalNormalY;

        private quaternion PlacementRotation(float3 normal, float? roofYaw = null)
        {
            if (IsRoof(normal))
            {
                // Stand on the roof, using the building's heading rather than
                // camera direction. Project it onto sloped roofs deterministically.
                float3 forward = math.rotate(m_SelectedBuildingTransform.m_Rotation, new float3(0f, 0f, 1f));
                forward = math.normalizesafe(forward - normal * math.dot(forward, normal));
                return math.mul(quaternion.AxisAngle(normal, roofYaw ?? m_RoofYaw),
                    quaternion.LookRotationSafe(forward, normal));
            }
            float3 up = math.normalizesafe(new float3(0f, 1f, 0f) - normal * normal.y,
                new float3(0f, 1f, 0f));
            return quaternion.LookRotationSafe(normal, up);
        }

        private void QueueSurfaceValidation(
            float3 anchor,
            float3 normal,
            BrandCandidate candidate,
            float? roofYaw = null)
        {
            m_PendingRoofYaw = roofYaw ?? m_RoofYaw;
            quaternion rotation = PlacementRotation(normal, m_PendingRoofYaw);
            float3 up = IsRoof(normal)
                ? math.rotate(rotation, new float3(0f, 0f, 1f))
                : math.normalizesafe(new float3(0f, 1f, 0f) - normal * normal.y, new float3(0f, 1f, 0f));
            float3 right = IsRoof(normal)
                ? math.rotate(rotation, new float3(1f, 0f, 0f))
                : math.normalizesafe(math.cross(up, normal), new float3(1f, 0f, 0f));
            float3 halfSize = (candidate.BoundsMax - candidate.BoundsMin) * 0.5f;
            if (IsRoof(normal)) halfSize.y = halfSize.z;
            m_SupportPoints[0] = anchor;
            m_SupportPoints[1] = anchor - right * halfSize.x - up * halfSize.y;
            m_SupportPoints[2] = anchor - right * halfSize.x + up * halfSize.y;
            m_SupportPoints[3] = anchor + right * halfSize.x - up * halfSize.y;
            m_SupportPoints[4] = anchor + right * halfSize.x + up * halfSize.y;
            // A unique context prevents a completed older batch from validating
            // a newly switched asset or editing session. The raycaster owns results.
            m_SupportContext = new object();
            m_PendingAnchor = anchor;
            m_PendingNormal = normal;
            m_SupportWaitFrames = 0;
            m_SupportPending = true;
            m_HasSurfaceAttempt = true;
            m_StatusText = "Checking building surface…";
            for (int i = 0; i < SupportProbeCount; i++)
            {
                m_SurfaceRaycastSystem.AddInput(m_SupportContext, new RaycastInput
                {
                    m_Line = new Line3.Segment(
                        m_SupportPoints[i] + normal * SupportProbeReach,
                        m_SupportPoints[i] - normal * SupportProbeReach),
                    m_TypeMask = TypeMask.StaticObjects,
                    m_CollisionMask = CollisionMask.OnGround | CollisionMask.Overground,
                    m_Flags = RaycastFlags.SubElements
                });
            }
        }

        private void CompleteSurfaceValidation()
        {
            if (!m_SupportPending)
            {
                return;
            }

            // Borrowed memory: GetResult returns a view into the game's buffer.
            // Consume it synchronously, and never dispose or retain it.
            NativeArray<RaycastResult> results = m_SurfaceRaycastSystem.GetResult(m_SupportContext);
            if (!results.IsCreated || results.Length != SupportProbeCount)
            {
                if (++m_SupportWaitFrames < SupportTimeoutFrames)
                {
                    return;
                }
                Mod.Log.Warn("Surface validation timed out; placement was not approved.");
                RejectPendingSurface();
                return;
            }

            float lift = 0f;
            for (int i = 0; i < SupportProbeCount; i++)
            {
                RaycastResult result = results[i];
                float3 hitNormal = math.normalizesafe(result.m_Hit.m_HitDirection);
                if (ResolveBuilding(result.m_Owner) != m_SelectedBuilding ||
                    !math.all(math.isfinite(result.m_Hit.m_HitPosition)) ||
                    !math.all(math.isfinite(hitNormal)) ||
                    math.abs(math.dot(hitNormal, m_PendingNormal)) < 0.5f)
                {
                    RejectPendingSurface();
                    return;
                }
                lift = math.max(lift, math.dot(
                    result.m_Hit.m_HitPosition - m_SupportPoints[i], m_PendingNormal));
            }

            m_SupportPending = false;
            m_SupportContext = null;
            m_SurfaceAnchor = m_PendingAnchor;
            m_SurfaceNormal = m_PendingNormal;
            m_SurfaceLift = lift;
            m_HasAnchor = true;
            m_RoofYaw = m_PendingRoofYaw;
            ApplyPreviewTransform(m_SurfaceAnchor, m_SurfaceNormal, CurrentCandidate);
            m_CanPlace = true;
            m_StatusText = IsRoof(m_SurfaceNormal)
                ? "Drag to move; hold right-click and drag left/right to rotate, then Place."
                : "Drag on the building surface, choose an asset, then Place.";
        }

        private void RejectPendingSurface()
        {
            m_SupportPending = false;
            m_SupportContext = null;
            m_CanPlace = m_RetainValidSurface;
            m_StatusText = m_CanPlace
                ? "Last valid position retained; move back onto this building to adjust it."
                : "The sign needs building surface behind its center and corners.";
        }

        private void DrawPreviewOutline()
        {
            BrandCandidate candidate = CurrentCandidate;
            if (!m_HasAnchor || candidate == null)
            {
                return;
            }

            // Temp objects have their own color path. Draw an explicit editing
            // frame as well, so selection remains visible on neon/thin materials.
            OverlayRenderSystem.Buffer buffer = m_OverlaySystem.GetBuffer(out JobHandle writers);
            writers.Complete();
            float3 Corner(float x, float y) => m_FinalTransform.m_Position +
                math.rotate(m_FinalTransform.m_Rotation,
                    new float3(x, y, candidate.BoundsMax.z + 0.015f));
            float3 a = Corner(candidate.BoundsMin.x, candidate.BoundsMin.y);
            float3 b = Corner(candidate.BoundsMax.x, candidate.BoundsMin.y);
            float3 c = Corner(candidate.BoundsMax.x, candidate.BoundsMax.y);
            float3 d = Corner(candidate.BoundsMin.x, candidate.BoundsMax.y);
            UnityEngine.Color color = new UnityEngine.Color(0.25f, 0.85f, 1f, 1f);
            buffer.DrawLine(color, new Line3.Segment(a, b), 0.04f);
            buffer.DrawLine(color, new Line3.Segment(b, c), 0.04f);
            buffer.DrawLine(color, new Line3.Segment(c, d), 0.04f);
            buffer.DrawLine(color, new Line3.Segment(d, a), 0.04f);
        }

        private void CreatePreview()
        {
            BrandCandidate candidate = CurrentCandidate;
            if (candidate == null ||
                !EntityManager.Exists(candidate.Prefab) ||
                !EntityManager.TryGetComponent(candidate.Prefab, out ObjectData objectData) ||
                !objectData.m_Archetype.Valid)
            {
                m_CanPlace = false;
                return;
            }

            // Preserve the prefab's rendering components (including MeshBatch and
            // CullingInfo), but remove its live-object creation marker before any
            // game update can register it as a committed prop or subobject.
            Entity preview = EntityManager.CreateEntity(objectData.m_Archetype);
            m_PreviewEntity = preview;
            try
            {
                EnsureTag<BrandPreview>(preview);
                SetOrAdd(preview, new Temp(Entity.Null, TempFlags.Create | TempFlags.Select));
                if (EntityManager.HasComponent<Created>(preview))
                {
                    EntityManager.RemoveComponent<Created>(preview);
                }
                if (EntityManager.HasComponent<Owner>(preview))
                {
                    EntityManager.RemoveComponent<Owner>(preview);
                }
                EnsureTag<Highlighted>(preview);
                SetOrAdd(preview, new PrefabRef(candidate.Prefab));
                EnsureTag<BatchesUpdated>(preview);
            }
            catch
            {
                DiscardPreviewEntity(preview);
                m_PreviewEntity = Entity.Null;
                throw;
            }
        }

        private void DestroyPreview()
        {
            StopRoofRotation();
            // A submitted preview can outlive a tool switch. Only cancel it if
            // it is still temporary; never delete a completed ordinary prop.
            if (m_CommittingEntity != Entity.Null && EntityManager.Exists(m_CommittingEntity) &&
                EntityManager.HasComponent<Temp>(m_CommittingEntity))
            {
                DiscardPreviewEntity(m_CommittingEntity);
            }
            m_CommittingEntity = Entity.Null;

            // Rendering and object-search systems need to observe Deleted before
            // the game's cleanup pass destroys a rendered entity. Immediate ECS
            // destruction can leave an unselectable rendered instance behind.
            DiscardPreviewEntity(m_PreviewEntity);
            try
            {
                if (!m_PreviewQuery.IsEmptyIgnoreFilter)
                {
                    using NativeArray<Entity> previews =
                        m_PreviewQuery.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < previews.Length; i++)
                    {
                        DiscardPreviewEntity(previews[i]);
                    }
                }
            }
            catch (Exception exception)
            {
                Mod.Log.Error(exception);
            }

            m_PreviewEntity = Entity.Null;
            m_CanPlace = false;
            m_SupportPending = false;
            m_HasSurfaceAttempt = false;
            m_SupportContext = null;
        }

        private void DiscardPreviewEntity(Entity preview)
        {
            if (preview == Entity.Null || !EntityManager.Exists(preview))
            {
                return;
            }

            try
            {
                UnregisterTemporaryBuildingProp(preview);
                if (EntityManager.HasComponent<Deleted>(preview)) return;
                if (EntityManager.HasComponent<Temp>(preview))
                {
                    EntityManager.SetComponentData(
                        preview,
                        new Temp(Entity.Null, TempFlags.Cancel));
                }
                EnsureTag<Deleted>(preview);
                EnsureTag<BatchesUpdated>(preview);
            }
            catch (Exception exception)
            {
                Mod.Log.Error(exception);
            }
        }

        private void LoadBrandCandidates(Entity brand)
        {
            m_Candidates.Clear();
            if (brand == Entity.Null || !EntityManager.Exists(brand))
            {
                return;
            }

            using NativeArray<Entity> prefabs =
                m_BrandObjectQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < prefabs.Length; i++)
            {
                Entity prefab = prefabs[i];
                try
                {
                    DynamicBuffer<ObjectRequirementElement> requirements =
                        EntityManager.GetBuffer<ObjectRequirementElement>(prefab, true);

                    bool matches = false;
                    for (int r = 0; r < requirements.Length; r++)
                    {
                        if (requirements[r].m_Requirement == brand)
                        {
                            matches = true;
                            break;
                        }
                    }

                    if (!matches ||
                        !EntityManager.TryGetComponent(prefab, out ObjectGeometryData geometry) ||
                        !HasFiniteBounds(geometry) ||
                        !math.all(math.isfinite(geometry.m_Size)) ||
                        math.any(geometry.m_Size < 0f) ||
                        geometry.m_Bounds.max.x <= geometry.m_Bounds.min.x ||
                        geometry.m_Bounds.max.y <= geometry.m_Bounds.min.y)
                    {
                        continue;
                    }

                    string name = SafePrefabName(prefab);
                    string normalizedName = name
                        .Replace("_", string.Empty)
                        .Replace(" ", string.Empty)
                        .ToLowerInvariant();

                    // The MVP picker is deliberately limited to billboard signs.
                    // Neon and circular billboard variants have thin geometry;
                    // posters and decals are not eligible.
                    bool isBillboard = normalizedName.Contains("billboard");
                    bool isNeon = normalizedName.Contains("neon");
                    bool isCircle = normalizedName.Contains("circle");
                    if (normalizedName.Contains("decal") ||
                        normalizedName.Contains("poster") ||
                        !(isBillboard || isNeon || isCircle))
                    {
                        continue;
                    }

                    float3 absoluteSize = math.abs(geometry.m_Size);
                    float minimumDimension = math.cmin(absoluteSize);
                    float maximumDimension = math.cmax(absoluteSize);
                    float flatness = maximumDimension > 0.001f
                        ? minimumDimension / maximumDimension
                        : 1f;
                    float score = flatness * 10f + maximumDimension * 0.01f;

                    if (normalizedName.Contains("billboardlarge"))
                    {
                        score -= 1000f;
                    }
                    else if (normalizedName.Contains("billboard"))
                    {
                        score -= 100f;
                    }
                    else if (isNeon || isCircle)
                    {
                        score -= 50f;
                    }

                    m_Candidates.Add(new BrandCandidate
                    {
                        Prefab = prefab,
                        Name = name,
                        BoundsMin = geometry.m_Bounds.min,
                        BoundsMax = geometry.m_Bounds.max,
                        IsFlat = isNeon || isCircle,
                        Score = score
                    });
                }
                catch (Exception exception)
                {
                    Mod.Log.Warn(
                        $"Skipping branding prefab {SafePrefabName(prefab)}: " +
                        exception.Message);
                }
            }

            m_Candidates.Sort((left, right) =>
            {
                int scoreOrder = left.Score.CompareTo(right.Score);
                return scoreOrder != 0
                    ? scoreOrder
                    : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            });
        }

        private CompanyResolution ResolveCompany(
            Entity building,
            bool logUnexpectedMultiple)
        {
            CompanyResolution result = default;
            if (!IsUsableBuilding(building) || !EntityManager.HasBuffer<Renter>(building))
            {
                return result;
            }

            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(building, true);
            for (int i = 0; i < renters.Length; i++)
            {
                Entity renter = renters[i].m_Renter;
                if (renter == Entity.Null ||
                    !EntityManager.Exists(renter) ||
                    !EntityManager.TryGetComponent(renter, out CompanyData companyData))
                {
                    continue;
                }

                result.CompanyCount++;
                result.Company = renter;
                result.Brand = companyData.m_Brand;
            }

            result.MultipleCompanies = result.CompanyCount > 1;
            result.Success = result.CompanyCount == 1 && result.Brand != Entity.Null;

            if (result.MultipleCompanies && logUnexpectedMultiple)
            {
                Mod.Log.Warn(
                    $"Building {building} exposed {result.CompanyCount} company renters; " +
                    "Brand the Building failed closed.");
            }

            return result;
        }

        private Entity ResolveBuilding(Entity entity)
        {
            Entity current = entity;
            for (int i = 0; i < MaximumOwnerDepth; i++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current))
                {
                    return Entity.Null;
                }

                if (EntityManager.HasComponent<Building>(current))
                {
                    return current;
                }

                if (!EntityManager.TryGetComponent(current, out Owner owner) ||
                    owner.m_Owner == current)
                {
                    return Entity.Null;
                }

                current = owner.m_Owner;
            }

            return Entity.Null;
        }

        private bool IsUsableBuilding(Entity building)
        {
            return building != Entity.Null &&
                   EntityManager.Exists(building) &&
                   EntityManager.HasComponent<Building>(building) &&
                   !EntityManager.HasComponent<Deleted>(building) &&
                   !EntityManager.HasComponent<Destroyed>(building) &&
                   !EntityManager.HasComponent<UnderConstruction>(building);
        }

        private void SetHighlighted(Entity building)
        {
            if (m_HighlightedBuilding == building)
            {
                return;
            }

            if (m_HighlightedBuilding != Entity.Null &&
                EntityManager.Exists(m_HighlightedBuilding))
            {
                if (m_AddedHighlight &&
                    EntityManager.HasComponent<Highlighted>(m_HighlightedBuilding))
                {
                    EntityManager.RemoveComponent<Highlighted>(m_HighlightedBuilding);
                    EnsureTag<BatchesUpdated>(m_HighlightedBuilding);
                }
            }

            m_HighlightedBuilding = building;
            m_AddedHighlight = false;
            if (building != Entity.Null && EntityManager.Exists(building))
            {
                if (!EntityManager.HasComponent<Highlighted>(building))
                {
                    EntityManager.AddComponent<Highlighted>(building);
                    EnsureTag<BatchesUpdated>(building);
                    m_AddedHighlight = true;
                }
            }
        }

        private static float FaceClearance(BrandCandidate candidate) =>
            candidate.IsFlat ? FlatFaceClearance : BillboardFaceClearance;

        private static bool HasFiniteBounds(ObjectGeometryData geometry) =>
            math.all(math.isfinite(geometry.m_Bounds.min)) &&
            math.all(math.isfinite(geometry.m_Bounds.max)) &&
            math.all(geometry.m_Bounds.min <= geometry.m_Bounds.max);

        private void SetOrAdd<T>(Entity entity, T data)
            where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(entity))
            {
                EntityManager.SetComponentData(entity, data);
            }
            else
            {
                EntityManager.AddComponentData(entity, data);
            }
        }

        private void EnsureTag<T>(Entity entity)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
            {
                EntityManager.AddComponent<T>(entity);
            }
        }

        private string SafePrefabName(Entity prefab)
        {
            if (prefab == Entity.Null)
            {
                return string.Empty;
            }

            try
            {
                return m_PrefabSystem.GetPrefabName(prefab) ?? prefab.ToString();
            }
            catch
            {
                return prefab.ToString();
            }
        }

        private void ResetSessionState()
        {
            StopRoofRotation();
            m_RoofYaw = 0f;
            m_PendingRoofYaw = 0f;
            applyMode = ApplyMode.None;
            m_Mode = BrandToolMode.Inactive;
            m_CommittingEntity = Entity.Null;
            m_CommitWaitFrames = 0;
            m_CommitTransform = default;
            m_CommitElevation = default;
            m_SelectedBuilding = Entity.Null;
            m_SelectedBuildingPrefab = Entity.Null;
            m_SelectedBuildingTransform = default;
            m_SelectedCompany = Entity.Null;
            m_SelectedBrand = Entity.Null;
            m_Candidates.Clear();
            m_CandidateIndex = 0;
            m_IsDragging = false;
            m_IsPointerOverUI = false;
            m_PlaceRequested = false;
            m_CancelRequested = false;
            m_AssetStepRequested = 0;
            m_Offset = DefaultOffset;
            m_OffsetChanged = false;
            m_HasAnchor = false;
            m_SurfaceAnchor = float3.zero;
            m_SurfaceNormal = float3.zero;
            m_SurfaceLift = 0f;
            m_SupportPending = false;
            m_HasSurfaceAttempt = false;
            m_SupportContext = null;
            m_SupportWaitFrames = 0;
            m_RetainValidSurface = false;
            m_FinalTransform = default;
            m_CanPlace = false;
            m_HoverText = string.Empty;
            m_StatusText = string.Empty;
            m_BuildingName = string.Empty;
            m_TargetingFeedback = string.Empty;
            m_TargetingFeedbackFrames = 0;
        }

        private void StopRoofRotation()
        {
            m_IsRotating = false;
            m_RotationDelta = 0f;
            if (m_RotationCameraBarrier != null) m_RotationCameraBarrier.blocked = false;
        }

        private void UpdateRoofRotation()
        {
            Mouse mouse = Mouse.current;
            if (m_IsPointerOverUI || !m_HasAnchor || !IsRoof(m_SurfaceNormal) ||
                CurrentCandidate == null || mouse == null || !Application.isFocused)
            {
                StopRoofRotation();
                return;
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                m_IsRotating = true;
                m_IsDragging = false;
                if (m_RotationCameraBarrier != null) m_RotationCameraBarrier.blocked = true;
            }
            if (m_IsRotating && mouse.rightButton.isPressed)
            {
                float delta = mouse.delta.ReadValue().x;
                if (math.isfinite(delta)) m_RotationDelta += math.radians(delta * 0.3f);
            }
            if (!mouse.rightButton.isPressed)
            {
                m_IsRotating = false;
                if (m_RotationCameraBarrier != null) m_RotationCameraBarrier.blocked = false;
            }
            if (!m_SupportPending && m_RotationDelta != 0f)
            {
                float requestedYaw = math.atan2(math.sin(m_RoofYaw + m_RotationDelta),
                    math.cos(m_RoofYaw + m_RotationDelta));
                m_RotationDelta = 0f;
                m_RetainValidSurface = m_CanPlace;
                // Validate the rotated base first. Failure retains the last valid
                // angle and transform; pending input never changes the saved pose.
                QueueSurfaceValidation(m_SurfaceAnchor, m_SurfaceNormal, CurrentCandidate, requestedYaw);
            }
        }

        private struct CompanyResolution
        {
            public bool Success;
            public bool MultipleCompanies;
            public int CompanyCount;
            public Entity Company;
            public Entity Brand;
        }
    }
}
