using BrandTheBuilding.Systems;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System.Collections.Generic;

namespace BrandTheBuilding
{
    public sealed class Mod : IMod
    {
        public static BrandTheBuildingSettings Settings { get; private set; }
        private MemorySource m_Locale;

        public static readonly ILog Log = LogManager
            .GetLogger($"{ModAssemblyInfo.Name}.{nameof(Mod)}")
            .SetShowsErrorsInUI(false)
            .SetShowsStackTraceAboveLevels(Level.Error);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{ModAssemblyInfo.Title} v{ModAssemblyInfo.Version} loading.");

            Settings = new BrandTheBuildingSettings(this);
            m_Locale = new MemorySource(new Dictionary<string, string>
            {
                [Settings.GetSettingsLocaleID()] = ModAssemblyInfo.Title,
                [Settings.GetOptionTabLocaleID(BrandTheBuildingSettings.GeneralTab)] = "General",
                [Settings.GetOptionGroupLocaleID(BrandTheBuildingSettings.HotkeysGroup)] = "Hotkey",
                [Settings.GetOptionLabelLocaleID(nameof(BrandTheBuildingSettings.ActivateTool))] = "Activate tool",
                [Settings.GetOptionDescLocaleID(nameof(BrandTheBuildingSettings.ActivateTool))] = "Activate the Brand the Building placement tool. No key is assigned by default.",
                [Settings.GetBindingKeyLocaleID(nameof(BrandTheBuildingSettings.ActivateTool))] = "Activate Brand the Building",
                [Settings.GetBindingMapLocaleID()] = ModAssemblyInfo.Title
            });
            GameManager.instance.localizationManager.AddSource("en-US", m_Locale);
            AssetDatabase.global.LoadSettings(
                ModAssemblyInfo.Name,
                Settings,
                new BrandTheBuildingSettings(this));
            Settings.RegisterKeyBindings();
            Settings.RegisterInOptionsUI();

            updateSystem.UpdateAt<BrandPlacementToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<BrandTheBuildingUISystem>(SystemUpdatePhase.UIUpdate);

            Log.Info($"{ModAssemblyInfo.Title} loaded.");
        }

        public void OnDispose()
        {
            Settings?.UnregisterInOptionsUI();
            Settings = null;
            if (m_Locale != null && GameManager.instance != null)
            {
                GameManager.instance.localizationManager.RemoveSource("en-US", m_Locale);
                m_Locale = null;
            }
            Log.Info($"{ModAssemblyInfo.Title} disposed.");
        }
    }
}
