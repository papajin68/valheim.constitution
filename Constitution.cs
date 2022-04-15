// Constitution
// Allows the adjustment of base health and stamina as well as adding a "Constitution" skill which will
// adjust health and stamina according to the player's skill level.
// 
// File:    Constitution.cs
// Project: Constitution

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Configs;
using Jotunn.Utils;
using System;

namespace Constitution
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class Constitution : BaseUnityPlugin
    {
        public const string PluginGUID = "papajin68.constitution";
        public const string PluginName = "Constitution";
        public const string PluginVersion = "1.0.0";

        private static Skills.SkillType ConstitutionSkill = Skills.SkillType.None;
        private static float foodUpdate = 0f;
        private static float stamUsage = 0f;

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        internal static Harmony _harmony;

        //private static ConfigEntry<bool> _forceServerConfig;
        private static ConfigEntry<float> _baseHealthAdjust;
        private static ConfigEntry<float> _baseStaminaAdjust;
        private static ConfigEntry<float> _constitutionHealthModifer;
        private static ConfigEntry<float> _constitutionStaminaModifier;
        private static ConfigEntry<float> _healthPerConstitution;
        private static ConfigEntry<float> _staminaPerConstitution;
        private static ConfigEntry<float> _minStamUsageRequired;

        private void Awake()
        {
            Config.SaveOnConfigSet = true;

            /*_forceServerConfig = Config.Bind("General", "ForceServerConfig", true,
                new ConfigDescription("Forces server config to override client config", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));*/
            _baseHealthAdjust = Config.Bind("Constitution", "BaseHealthAdjust", 10f,
                new ConfigDescription("Amount of health added to base health pool", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _baseStaminaAdjust = Config.Bind("Constitution", "BaseStaminaAdjust", 10f,
                new ConfigDescription("Amount of stamina added to base stamina pool", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _constitutionHealthModifer = Config.Bind("Constitution", "ConstitutionPerFoodHealth", 0.1f,
                new ConfigDescription("Constitution gained health modifier when eating (Food Health / Food Duration * Modifier)", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _constitutionStaminaModifier = Config.Bind("Constitution", "ConstitutionPerFoodStamina", 0.1f,
                new ConfigDescription("Constitution gained stamina modifier when eating (Food Stamina / Food Duration * Modifier)", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _healthPerConstitution = Config.Bind("Constitution", "HealthPerConstitution", 0.5f,
                new ConfigDescription("Amount of health gained per point of constitution", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _staminaPerConstitution = Config.Bind("Constitution", "StaminaPerConstitution", 0.5f,
                new ConfigDescription("Amount of stamina gained per point of constitution", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _minStamUsageRequired = Config.Bind("Constitution", "MinimumStaminaUsageRequiredForSkillup", 50f,
                new ConfigDescription("Minimum stamina usage requirement per 60 seconds to gain constitution skill", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));

            _harmony = new Harmony(Info.Metadata.GUID);
            _harmony.PatchAll();

            ConstitutionSkill = SkillManager.Instance.AddSkill(new SkillConfig
            {
                Identifier = PluginGUID,
                Name = "Constitution",
                Description = "Increases your body's constitution",
                IncreaseStep = 1f
            });

            Jotunn.Logger.LogInfo("Loaded successfully.");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetBaseFoodHP))]
        class PlayerGetBaseFoodHPPatch
        {
            static void Postfix(Player __instance, ref float __result)
            {
                Skills.Skill skill = __instance.GetSkills().GetSkill(ConstitutionSkill);
                __result += _baseHealthAdjust.Value + skill.m_level * _healthPerConstitution.Value;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetTotalFoodValue))]
        class PlayerGetTotalFoodValuePatch
        {
            public static void Postfix(Player __instance, ref float hp, ref float stamina)
            {
                Skills.Skill skill = __instance.GetSkills().GetSkill(ConstitutionSkill);
                hp += _baseHealthAdjust.Value + skill.m_level * _healthPerConstitution.Value;
                stamina += _baseStaminaAdjust.Value + skill.m_level * _staminaPerConstitution.Value;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateFood))]
        class PlayerUpdateFoodPatch
        {
            static void Prefix(Player __instance, float dt)
            {
                if (__instance.m_foods.Count > 0)
                {
                    foodUpdate += dt;
                    if (foodUpdate >= 60f && stamUsage > _minStamUsageRequired.Value)
                    {
                        Jotunn.Logger.LogInfo($"Constitution Update: Food Time = {foodUpdate}, Stamina = {stamUsage}");
                        foodUpdate = 0f;
                        stamUsage = 0f;
                        UpdateConstitutionSkill(__instance);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
        class PlayerUseStaminaPatch
        {
            static void Prefix(Player __instance, float v)
            {
                if (v > 0f && __instance.m_foods.Count > 0)
                {
                    stamUsage += v;
                    if (foodUpdate >= 60f && stamUsage > _minStamUsageRequired.Value)
                    {
                        Jotunn.Logger.LogInfo($"Constitution Update: Food Time = {foodUpdate}, Stamina = {stamUsage}");
                        foodUpdate = 0f;
                        stamUsage = 0f;
                        UpdateConstitutionSkill(__instance);
                    }
                }
            }
        }

        private static void UpdateConstitutionSkill(Player player)
        {
            if (player.GetSkills().GetSkill(ConstitutionSkill).m_level >= 100f)
            {
                return;
            }

            float skillRaise = 0f;
            foreach (Player.Food food in player.m_foods)
            {
                skillRaise += (food.m_item.m_shared.m_food * _constitutionHealthModifer.Value + food.m_item.m_shared.m_foodStamina * _constitutionStaminaModifier.Value) /
                        (food.m_item.m_shared.m_foodBurnTime / 60f);
                Jotunn.Logger.LogDebug($"Update Food: {food.m_name}|{food.m_health}/{food.m_item.m_shared.m_food}|{food.m_stamina}/{food.m_item.m_shared.m_foodStamina}|{food.m_item.m_shared.m_foodBurnTime}/{food.m_time}");
            }

            if (skillRaise > 0)
            {
                Skills.Skill skill = player.GetSkills().GetSkill(ConstitutionSkill);
                float roundRaise = (float)Math.Round(skillRaise, 2);
                Jotunn.Logger.LogInfo($"Constitution Skill: Level = {skill.m_level}, Accumlator = {skill.m_accumulator}, Raise = {roundRaise}");
                player.RaiseSkill(ConstitutionSkill, roundRaise);
            }
        }


    }
}