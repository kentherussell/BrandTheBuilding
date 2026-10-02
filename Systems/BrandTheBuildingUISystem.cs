using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.UI;

namespace BrandTheBuilding.Systems
{
    public partial class BrandTheBuildingUISystem : UISystemBase
    {
        private BrandPlacementToolSystem m_Tool;
        private ProxyAction m_ActivationAction;

        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<BrandPlacementToolSystem>();
            m_ActivationAction = Mod.Settings?.GetAction(nameof(BrandTheBuildingSettings.ActivateTool));
            if (m_ActivationAction != null)
            {
                m_ActivationAction.shouldBeEnabled = true;
            }
            else
            {
                Mod.Log.Warn("The Brand the Building activation hotkey was not registered.");
            }
            string group = BrandTheBuildingUIBindingConstants.Group;

            AddUpdateBinding(new GetterValueBinding<bool>(
                group,
                BrandTheBuildingUIBindingConstants.IsToolActive,
                () => m_Tool?.IsToolActive ?? false));

            AddUpdateBinding(new GetterValueBinding<bool>(
                group,
                BrandTheBuildingUIBindingConstants.IsEditing,
                () => m_Tool?.IsEditing ?? false));

            AddUpdateBinding(new GetterValueBinding<string>(
                group,
                BrandTheBuildingUIBindingConstants.HoverText,
                () => m_Tool?.HoverText ?? string.Empty));

            AddUpdateBinding(new GetterValueBinding<string>(
                group,
                BrandTheBuildingUIBindingConstants.StatusText,
                () => m_Tool?.StatusText ?? string.Empty));

            AddUpdateBinding(new GetterValueBinding<string>(
                group,
                BrandTheBuildingUIBindingConstants.BuildingName,
                () => m_Tool?.BuildingName ?? string.Empty));

            AddUpdateBinding(new GetterValueBinding<string>(
                group,
                BrandTheBuildingUIBindingConstants.AssetName,
                () => m_Tool?.AssetName ?? string.Empty));

            AddUpdateBinding(new GetterValueBinding<int>(
                group,
                BrandTheBuildingUIBindingConstants.AssetIndex,
                () => m_Tool?.AssetIndex ?? 0));

            AddUpdateBinding(new GetterValueBinding<int>(
                group,
                BrandTheBuildingUIBindingConstants.AssetCount,
                () => m_Tool?.AssetCount ?? 0));

            AddUpdateBinding(new GetterValueBinding<float>(
                group,
                BrandTheBuildingUIBindingConstants.Offset,
                () => m_Tool?.Offset ?? 0f));

            AddUpdateBinding(new GetterValueBinding<bool>(
                group,
                BrandTheBuildingUIBindingConstants.CanPlace,
                () => m_Tool?.CanPlace ?? false));

            AddBinding(new TriggerBinding(
                group,
                BrandTheBuildingUIBindingConstants.Activate,
                () => m_Tool?.ActivateTool()));

            AddBinding(new TriggerBinding(
                group,
                BrandTheBuildingUIBindingConstants.Cancel,
                () => m_Tool?.RequestCancel()));

            AddBinding(new TriggerBinding(
                group,
                BrandTheBuildingUIBindingConstants.Place,
                () => m_Tool?.RequestPlace()));

            AddBinding(new TriggerBinding(
                group,
                BrandTheBuildingUIBindingConstants.PreviousAsset,
                () => m_Tool?.RequestPreviousAsset()));

            AddBinding(new TriggerBinding(
                group,
                BrandTheBuildingUIBindingConstants.NextAsset,
                () => m_Tool?.RequestNextAsset()));

            AddBinding(new TriggerBinding<float>(
                group,
                BrandTheBuildingUIBindingConstants.SetOffset,
                value => m_Tool?.SetOffset(value)));

            AddBinding(new TriggerBinding<int>(
                group,
                BrandTheBuildingUIBindingConstants.StepOffset,
                value => m_Tool?.StepOffset(value)));

            AddBinding(new TriggerBinding<bool>(
                group,
                BrandTheBuildingUIBindingConstants.SetPointerOverUI,
                value => m_Tool?.SetPointerOverUI(value)));
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Tool != null && !m_Tool.IsToolActive &&
                    m_ActivationAction?.WasPressedThisFrame() == true)
                {
                    m_Tool.ActivateTool();
                }
            }
            catch (System.Exception exception)
            {
                Mod.Log.Error(exception);
            }

            base.OnUpdate();
        }

        protected override void OnDestroy()
        {
            m_Tool?.SetPointerOverUI(false);
            if (m_ActivationAction != null)
            {
                m_ActivationAction.shouldBeEnabled = false;
                m_ActivationAction = null;
            }
            m_Tool = null;
            base.OnDestroy();
        }
    }
}
