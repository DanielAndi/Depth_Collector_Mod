using HarmonyLib;
using UnityEngine;
using Verse;

namespace DebtCollector
{
    public class DebtCollectorMod : Mod
    {
        private static bool loggedInit;
        public static DebtCollectorMod Instance { get; private set; }
        public static DC_Settings Settings => Instance?.settings;
        
        private DC_Settings settings;

        public DebtCollectorMod(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = GetSettings<DC_Settings>();
            
            var harmony = new Harmony("com.yourname.debtcollector");
            harmony.PatchAll();
            
            if (!loggedInit)
            {
                loggedInit = true;
                Log.Message("[DebtCollector] Mod initialized. Harmony patches applied.");
            }
        }

        public override string SettingsCategory()
        {
            return "DC_Settings_Title".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DoSettingsWindowContents(inRect);
        }
    }
}
