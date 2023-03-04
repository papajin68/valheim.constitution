// Constitution - Some honest labor, and a hearty meal, will improve the constitution of any Viking!
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
        public const string PluginVersion = "1.0.3";

        private static Skills.SkillType ConstitutionSkill = Skills.SkillType.None;
        private static float stamUsage = 0f;
        private static float skillRaise = 0f;
        private static int foodUpdate = 0;

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        internal static Harmony _harmony;

        //private static ConfigEntry<bool> _forceServerConfig;
        private static ConfigEntry<float> _baseHealthAdjust;
        private static ConfigEntry<float> _baseStaminaAdjust;
        private static ConfigEntry<float> _constitutionGainHealthModifer;
        private static ConfigEntry<float> _constitutionGainStaminaModifier;
        private static ConfigEntry<float> _healthPerConstitution;
        private static ConfigEntry<float> _staminaPerConstitution;
        private static ConfigEntry<float> _activityToSkillModifier;

        private void Awake()
        {
            Config.SaveOnConfigSet = true;

            _baseHealthAdjust = Config.Bind("Constitution", "BaseHealthAdjust", 10f,
                new ConfigDescription("Amount of health added to base health pool", null,
                //new AcceptableValueRange<float>(-25f, 1000f),
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _baseStaminaAdjust = Config.Bind("Constitution", "BaseStaminaAdjust", 10f,
                new ConfigDescription("Amount of stamina added to base stamina pool", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _constitutionGainHealthModifer = Config.Bind("Constitution", "ConstitutionGainHealthModifier", 0.25f,
                new ConfigDescription("Modifier applied to food-health based constitution gain (higher = faster skill gain, scales with quality of food)", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _constitutionGainStaminaModifier = Config.Bind("Constitution", "ConstitutionGainStaminaModifier", 0.25f,
                new ConfigDescription("Modifier applied to food-stamina based constitution gain (higher = faster skill gain, scales with quality of food)", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _healthPerConstitution = Config.Bind("Constitution", "HealthPerConstitution", 0.5f,
                new ConfigDescription("Amount of health gained per point of constitution", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _staminaPerConstitution = Config.Bind("Constitution", "StaminaPerConstitution", 0.5f,
                new ConfigDescription("Amount of stamina gained per point of constitution", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _activityToSkillModifier = Config.Bind("Constitution", "ActivityToSkillModifer", 1.0f,
                new ConfigDescription("Stamina usage required as a percentage of current skill (lower = less activity required to gain skill)", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));

            // Add sanity checks for configuration values?

            _harmony = new Harmony(Info.Metadata.GUID);
            _harmony.PatchAll();

            ConstitutionSkill = SkillManager.Instance.AddSkill(new SkillConfig
            {
                Identifier = PluginGUID,
                Name = "Constitution",
                Description = "Increases your body's strength and stamina.",
                IncreaseStep = 1f
            });

            Jotunn.Logger.LogInfo("Loaded successfully.");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetBaseFoodHP))]
        class p68_PlayerGetBaseFoodHPPatch
        {
            static void Postfix(Player __instance, ref float __result)
            {
                Skills.Skill skill = __instance.GetSkills().GetSkill(ConstitutionSkill);
                __result += _baseHealthAdjust.Value + skill.m_level * _healthPerConstitution.Value;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetTotalFoodValue))]
        class p68_PlayerGetTotalFoodValuePatch
        {
            public static void Postfix(Player __instance, ref float hp, ref float stamina)
            {
                Skills.Skill skill = __instance.GetSkills().GetSkill(ConstitutionSkill);
                hp += _baseHealthAdjust.Value + skill.m_level * _healthPerConstitution.Value;
                stamina += _baseStaminaAdjust.Value + skill.m_level * _staminaPerConstitution.Value;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateFood))]
        class p68_PlayerUpdateFoodPatch
        {
            static void Prefix(Player __instance, float dt)
            {
                if (foodUpdate < 1500 && __instance.m_foods.Count > 0)
                {
                    if (++foodUpdate % 50 == 0)
                    {
                        foreach (Player.Food food in __instance.m_foods)
                        {
                            skillRaise += (food.m_item.m_shared.m_food * _constitutionGainHealthModifer.Value + food.m_item.m_shared.m_foodStamina *
                            _constitutionGainStaminaModifier.Value) / food.m_item.m_shared.m_foodBurnTime;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
        class p68_PlayerUseStaminaPatch
        {
            static void Prefix(Player __instance, float v)
            {
                if (foodUpdate >= 1500 && v > 0f && skillRaise > 0)
                {
                    float stamRequired = __instance.GetSkills().GetSkill(ConstitutionSkill).m_level * _activityToSkillModifier.Value;
                    stamUsage += v;
                    if (stamUsage >= stamRequired)
                    {
                        Jotunn.Logger.LogDebug($"Update Skill: Stamina Required = {stamRequired}, Used = {stamUsage}");
                        p68_UpdateConstitutionSkill(__instance);
                        foodUpdate = 0;
                        stamUsage = 0f;
                        skillRaise = 0f;
                    }
                }
            }
        }

        private static void p68_UpdateConstitutionSkill(Player player)
        {
            if (player.GetSkills().GetSkill(ConstitutionSkill).m_level >= 100f)
            {
                return;
            }

            if (skillRaise > 0)
            {
                Skills.Skill skill = player.GetSkills().GetSkill(ConstitutionSkill);
                float roundRaise = (float)Math.Round(skillRaise, 2);
                player.RaiseSkill(ConstitutionSkill, roundRaise);
            }
        }


    }
}