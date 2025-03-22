using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ItemStacksRewrite;

[BepInPlugin(pluginGUID, pluginName, pluginVersion)]
[BepInIncompatibility("net.mtnewton.itemstacks")]
public class ItemStacksRewrite : BaseUnityPlugin
{
    const string pluginGUID = "fortis.mods.itemstacksrewrite";
    const string pluginName = "ItemStacksRewrite";
    const string pluginVersion = "1.0";

    private static ItemStacksRewrite _instance;

    private static readonly string ISRConfigPath = $"{Path.Combine(Paths.ConfigPath, "ItemStacksRewrite")}";
    private static ConfigFile ISRStacksConfig;
    private static ConfigFile ISRWeightsConfig;
    private static ConfigSync ISRConfigSync = new ConfigSync(pluginGUID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

    private ConfigEntry<T> CreateSyncedGConfig<T>(string section, string key, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = Config.Bind(section, key, value, description);
        SyncedConfigEntry<T> syncedConfigEntry = ISRConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private static ConfigEntry<T> AddConfigToStackSync<T>(string section, string key, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = ISRStacksConfig.Bind(section, key, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = ISRConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private static ConfigEntry<T> AddConfigToWeightSync<T>(string section, string key, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = ISRWeightsConfig.Bind(section, key, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = ISRConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private static int TotalSyncedConfigs = 0;

    // General Config
    private static ConfigEntry<Toggle> LockConfiguration;
    private static ConfigEntry<bool> EnableStacks;
    private static ConfigEntry<bool> EnableWeights;
    private static ConfigEntry<bool> EnableStackMultiplier;
    private static ConfigEntry<float> StackSizeMultiplier;
    private static ConfigEntry<bool> EnableWeightMultiplier;
    private static ConfigEntry<float> WeightMultiplier;
    private static ConfigEntry<bool> EnableModded;

    // Debug Config
    private static ConfigEntry<bool> EnableDebug;

    // Item Stacks Config
    private static Dictionary<string, ConfigEntry<int>> ItemStackSizes;

    // Item Weights Config
    private static Dictionary<string, ConfigEntry<float>> ItemWeights;

    // Harmony
    private Harmony _harmony;

    // Logging
    private static readonly ManualLogSource ISRLogger = BepInEx.Logging.Logger.CreateLogSource(pluginName);
    private static readonly string LogPrefix = $"[ItemStacksRewrite v{pluginVersion}]";

    static ItemStacksRewrite()
    {
        ItemStackSizes = new Dictionary<string, ConfigEntry<int>>();
        ItemWeights = new Dictionary<string, ConfigEntry<float>>();
    }

    public void Awake()
    {
        Log("Initializing...");
        _instance = this;
        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginGUID);
        BindConfigs();
        Log("Initialized.");
    }

    private void OnDestroy()
    {
        _instance = null;
        _harmony.UnpatchSelf();
    }

    private void BindConfigs()
    {
        // General file
        LockConfiguration = CreateSyncedGConfig("1 - Server Sync", "LockConfiguration", Toggle.On, new ConfigDescription("For server admins only, if enabled, enforces the general config values on all connected players"));
        ISRConfigSync.AddLockingConfigEntry(LockConfiguration);

        EnableStacks = CreateSyncedGConfig("2 - General", "EnableStacks", true, new ConfigDescription("Enable setting item stacks"));
        EnableStacks.SettingChanged += ServerSync_SettingChanged;
        EnableWeights = CreateSyncedGConfig("2 - General", "EnableWeights", true, new ConfigDescription("Enable setting item weights"));
        EnableWeights.SettingChanged += ServerSync_SettingChanged;
        EnableStackMultiplier = CreateSyncedGConfig("2 - General", "EnableStackMultiplier", false, new ConfigDescription("If enabled, the multiplier will be enabled. Note this will apply the multiplier to the value set in \"fortis.mods.itemstackrewrite.stacks\""));
        EnableStackMultiplier.SettingChanged += ServerSync_SettingChanged;
        StackSizeMultiplier = CreateSyncedGConfig("2 - General", "StackSizeMultiplier", 1f, new ConfigDescription("If stack multiplier is enabled, the stack size set in \"ItemStackRewrite\\fortis.mods.itemstackrewrite.stacks\" will be multiplied by the value set here", new AcceptableValueRange<float>(1.0f, 10000f)));
        StackSizeMultiplier.SettingChanged += ServerSync_SettingChanged;
        EnableWeightMultiplier = CreateSyncedGConfig("2 - General", "EnableWeightMultiplier", false, new ConfigDescription("If enabled, the multiplier will be enabled. Note this will apply the multiplier to the value set in \"fortis.mods.itemstackrewrite.stacks\""));
        EnableWeightMultiplier.SettingChanged += ServerSync_SettingChanged;
        WeightMultiplier = CreateSyncedGConfig("2 - General", "WeightMultiplier", 1f, new ConfigDescription("If weight multiplier is enabled, the stack size set in \"ItemStackRewrite\\fortis.mods.itemstackrewrite.weights\" will be multiplied by the value set here", new AcceptableValueRange<float>(0.0f, 10000f)));
        WeightMultiplier.SettingChanged += ServerSync_SettingChanged;
        EnableModded = CreateSyncedGConfig("2 - General", "EnableModded", true, new ConfigDescription("If enabled, mod will add config entries for modded items. Disabling this may improve server load and loading world performance. " +
            "If disabling after modded items have already been added, modded items will stay but not be used until you delete file and regenerate it"));
        EnableModded.SettingChanged += ServerSync_SettingChanged;
        EnableDebug = Config.Bind("3 - Debug", "EnableDebug", false, new ConfigDescription("Enable this to print more logging information to the console."));

        if (!Directory.Exists(ISRConfigPath))
            Directory.CreateDirectory(ISRConfigPath);

        ISRStacksConfig = new ConfigFile(Path.Combine(ISRConfigPath, $"{pluginGUID}.stacks.cfg"), false);
        ISRWeightsConfig = new ConfigFile(Path.Combine(ISRConfigPath, $"{pluginGUID}.weights.cfg"), false);
    }

    [HarmonyPatch(typeof(ConfigSync))]
    public static class ServerSyncPatches
    {
        [HarmonyPatch("RPC_FromServerConfigSync")]
        [HarmonyPatch("RPC_FromOtherClientConfigSync")]
        [HarmonyPatch("resetConfigsFromServer")]
        private static void Postfix()
        {
            if (!EnableStacks.Value && !EnableWeights.Value)
                return;

            if (TotalSyncedConfigs <= 0)
                return;

            Log($"ServerSync is applying {TotalSyncedConfigs} values");
            if (ObjectDB.instance.isActiveAndEnabled)
            {
                bool success = ApplyItemChanges();
                if (success)
                    Log("Successfully applied item patches");
                else
                    Log("Failed to patch items");
            }

            TotalSyncedConfigs = 0;
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    public static class ModifyItems
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ObjectDB __instance)
        {
            if (!EnableStacks.Value && !EnableWeights.Value)
                return;

            Log("ObjectDB Awake Postfix fired.", true);

            if (ZNetScene.instance == null)
                return;

            bool success = ApplyItemChanges();
            if (success)
                Log("Successfully applied item patches");
            else
                Log("Failed to patch items");
        }
    }

    private static bool ApplyItemChanges()
    {
        if (ObjectDB.instance.isActiveAndEnabled)
        {
            DateTime before = DateTime.Now;
            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (BlockedItems.Contains(item.name))
                    continue;

                ItemDrop itemDrop = item.GetComponent<ItemDrop>();
                if (itemDrop == null)
                    continue;

                // So far this is the best way I can think of to separate AI item stuff from player item stuff, this and the blocked item list.
                // This does mean modded items that don't have an icon or description will not be compatible.
                if (!ItemHasIcon(itemDrop) || itemDrop.m_itemData.m_shared.m_description == null)   
                    continue;

                // If disabled modded item in config. 
                if (!EnableModded.Value)
                {
                    string lastVanillaItem = "YmirRemains";
                    if (ObjectDB.instance.m_items.IndexOf(item) > ObjectDB.instance.m_items.IndexOf(ObjectDB.instance.m_items.First(x => x.name == lastVanillaItem)))
                        continue;
                }

                if (EnableStacks.Value)
                {
                    bool isValidItem = itemDrop.m_itemData.m_shared.m_maxStackSize > 1;
                    if (itemDrop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility)
                        isValidItem = true;

                    if (isValidItem)
                    {
                        if (!ItemStackSizes.TryGetValue($"{item.name}_max_stack", out ConfigEntry<int> config))
                        {
                            Log($"Creating/Getting stack config for item: {item.name}, original stack size: {item.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize}", true);
                            string itemName = $"{item.name}_max_stack";
                            var syncedItemConfig = AddConfigToStackSync("Item Stacks", itemName, item.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize, new ConfigDescription("Set the max stack size", new AcceptableValueRange<int>(1, int.MaxValue)));
                            syncedItemConfig.SettingChanged += ServerSync_SettingChanged;
                            ItemStackSizes.Add(itemName, syncedItemConfig);
                            item.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize = EnableStackMultiplier.Value ? Mathf.Clamp((int)Math.Round(syncedItemConfig.Value * StackSizeMultiplier.Value), 1, int.MaxValue) : syncedItemConfig.Value;
                            Log($"Item: {item.name} config value: {syncedItemConfig.Value}, item value: {item.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize}", true);
                        }
                        else
                        {
                            Log($"Applying stack config value: {config.Value} on item: {item.name}", true);
                            item.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize = EnableStackMultiplier.Value ? Mathf.Clamp((int)Math.Round(config.Value * StackSizeMultiplier.Value), 1, int.MaxValue) : config.Value;
                        }
                    }
                }
                if (EnableWeights.Value)
                {
                    if (!ItemWeights.TryGetValue($"{item.name}_weight", out ConfigEntry<float> config))
                    {
                        Log($"Creating/Getting weight config for item: {item.name}, original weight: {item.GetComponent<ItemDrop>().m_itemData.m_shared.m_weight}", true);
                        string itemName = $"{item.name}_weight";
                        var syncedItemConfig = AddConfigToWeightSync("Item Weights", itemName, item.GetComponent<ItemDrop>().m_itemData.m_shared.m_weight, new ConfigDescription("Set the item weight", new AcceptableValueRange<float>(0.0f, 2147484f)));
                        syncedItemConfig.SettingChanged += ServerSync_SettingChanged;
                        ItemWeights.Add(itemName, syncedItemConfig);
                        item.GetComponent<ItemDrop>().m_itemData.m_shared.m_weight = EnableWeightMultiplier.Value ? Mathf.Clamp(syncedItemConfig.Value * WeightMultiplier.Value, 0, int.MaxValue) : syncedItemConfig.Value;
                        Log($"Item: {item.name} config value: {syncedItemConfig.Value}, item value: {item.GetComponent<ItemDrop>().m_itemData.m_shared.m_weight}", true);
                    }
                    else
                    {
                        Log($"Applying weight config value: {config.Value} on item: {item.name}", true);
                        item.GetComponent<ItemDrop>().m_itemData.m_shared.m_weight = EnableWeightMultiplier.Value ? Mathf.Clamp(config.Value * WeightMultiplier.Value, 0, int.MaxValue) : config.Value;
                    }
                }
            }

            DateTime after = DateTime.Now;
            var timeToApplyChanges = after - before;
            Log($"Applied patches in {timeToApplyChanges.TotalMilliseconds}ms");

            return true;
        }

        return false;
    }

    private static void ServerSync_SettingChanged(object sender, EventArgs e)
    {
        if (!EnableStacks.Value && !EnableWeights.Value)
            return;
        if (sender == null)
            return;
        
        // ngl this is ugly af TO:DO re-write this shit
        if (sender is ConfigEntry<bool> bConfig)
        {
            if (!ISRConfigSync.IsSourceOfTruth && ISRConfigSync.InitialSyncDone)
                Log($"ServerSync is applying local config entry {bConfig.Definition.Key} new value {bConfig.Value}", true);
            else
                Log($"ServerSync is applying sever config entry {bConfig.Definition.Key} new value {bConfig.Value}", true);
        }
        else if (sender is ConfigEntry<float> fConfig)
        {
            if (!ISRConfigSync.IsSourceOfTruth && ISRConfigSync.InitialSyncDone)
                Log($"ServerSync is applying local config entry {fConfig.Definition.Key} new value {fConfig.Value}", true);
            else
                Log($"ServerSync is applying sever config entry {fConfig.Definition.Key} new value {fConfig.Value}", true);
        }
        else if (sender is ConfigEntry<int> iConfig)
        {
            if (!ISRConfigSync.IsSourceOfTruth && ISRConfigSync.InitialSyncDone)
                Log($"ServerSync is applying local config entry {iConfig.Definition.Key} new value {iConfig.Value}", true);
            else
                Log($"ServerSync is applying sever config entry {iConfig.Definition.Key} new value {iConfig.Value}", true);
        }

        TotalSyncedConfigs++;
    }

    private static bool ItemHasIcon(ItemDrop itemDrop)
    {
        return itemDrop.m_itemData.m_shared.m_icons.Length > 0 ? itemDrop.m_itemData.GetIcon() != null : false;
    }

    private static void Log(string message, bool isDebug = false)
    {
        if (isDebug)
        {
            if (EnableDebug.Value)
                ISRLogger.LogInfo($"{LogPrefix} DEBUG: {message}");
            return;
        }

        ISRLogger.LogInfo($"{LogPrefix} {message}");
    }

    // Not really sure of a better way, these are items that shouldn't be tracked but have an icon and description.
    private static List<string> BlockedItems = new List<string>()
    {
        "charred_bow", "charred_bow_Fader", "charred_bow_volley", "charred_bow_volley_Fader", "Charred_Breastplate", "Charred_Helmet", "Charred_HipCloth",
        "Charred_MageCloths", "charred_magestaff_fire", "charred_magestaff_summon", "charred_twitcher_throw", "draugr_arrow", "draugr_bow", "DvergerArbalest_shoot", "DvergerArbalest_shootAshlands",
        "DvergerHairFemale", "DvergerHairFemale_Redhair", "DvergerHairMale", "DvergerHairMale_Redbeard", "DvergerStaffFire", "DvergerStaffHeal", "DvergerStaffIce", "DvergerStaffSupport", "DvergerSuitArbalest",
        "DvergerSuitArbalest_Ashlands", "DvergerSuitFire", "DvergerSuitIce", "DvergerSuitSupport", "GoblinArmband", "GoblinBrute_ArmGuard", "GoblinBrute_Backbones", "GoblinBrute_ExecutionerCap", "GoblinBrute_HipCloth",
        "GoblinBrute_LegBones", "GoblinBrute_ShoulderGuard", "GoblinClub", "GoblinHelmet", "GoblinLegband", "GoblinLoin", "GoblinShaman_attack_poke", "GoblinShaman_Headdress_antlers", "GoblinShaman_Headdress_feathers",
        "GoblinShaman_Staff_Bones", "GoblinShaman_Staff_Feathers", "GoblinShaman_Staff_Hildir", "GoblinShoulders", "GoblinSpear", "GoblinSword", "GoblinTorch", "HealthUpgrade_Bonemass", "HealthUpgrade_GDKing",
        "skeleton_bow", "skeleton_bow2", "StaminaUpgrade_Greydwarf", "StaminaUpgrade_Troll", "StaminaUpgrade_Wraith", "VegvisirShard_Bonemass"
    };

    private enum Toggle
    {
        On = 1,
        Off = 0
    }
}