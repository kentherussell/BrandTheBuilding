using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;

namespace BrandTheBuilding
{
    [FileLocation("ModsSettings/BrandTheBuilding/BrandTheBuilding")]
    [SettingsUIGroupOrder(HotkeysGroup)]
    [SettingsUIShowGroupName(HotkeysGroup)]
    public sealed class BrandTheBuildingSettings : ModSetting
    {
        public const string GeneralTab = "General";
        public const string HotkeysGroup = "Hotkeys";

        public BrandTheBuildingSettings(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(GeneralTab, HotkeysGroup)]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ActivateTool { get; set; }

        public override void SetDefaults()
        {
            // The no-argument keyboard binding has no assigned key by default.
        }
    }
}
