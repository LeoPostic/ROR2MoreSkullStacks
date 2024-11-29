using BepInEx;
using BepInEx.Configuration;
using EntityStates.Bandit2.Weapon;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace MoreSkullStacks
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class MoreSkullStacks : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Kytsop";
        public const string PluginName = "MoreSkullStacks";
        public const string PluginVersion = "1.0.0";

        private static ConfigEntry<float> configBaseDmg;
        private static ConfigEntry<float> configDmgScaling;
        private static ConfigEntry<float> configStackLoss;

        private static Dictionary<PlayerCharacterMasterController, int> stackCache = new Dictionary<PlayerCharacterMasterController, int>();
        private static List<int> ignoredScenesIndex = new List<int> { 9, 43 }; // Stages with no enemies such as the bazaar are ignored 

        public void Awake()
        {                      
            Log.Init(Logger);
            configBaseDmg = Config.Bind("General", "Base damage", 0.6f, "Damage dealt by the skill with no stacks. Original value is 1.");
            configDmgScaling = Config.Bind("General", "Damage scaling", 0.06f, "Damage gain with each stack. Original value is 0.1.");
            configStackLoss = Config.Bind("General", "Stack loss", 0.5f, "The proportion of stack lost each time you die or change stage. 0 means you lose all stacks, 1 means you keep all of your stacks.");
        }


        private void OnEnable()
        {
            On.RoR2.SceneExitController.Begin += OnExitingStage;
            On.RoR2.CharacterBody.OnDeathStart += OnPlayerDeath;
            On.RoR2.CharacterBody.Start += OnBodyStart;
            On.EntityStates.Bandit2.Weapon.FireSidearmSkullRevolver.ModifyBullet += OnSkullRevolverFire;
            On.RoR2.Run.Start += OnRunStart;
        }

        private void OnDisable()
        {
            On.RoR2.SceneExitController.Begin -= OnExitingStage;
            On.RoR2.CharacterBody.OnDeathStart -= OnPlayerDeath;
            On.RoR2.CharacterBody.Start -= OnBodyStart;
            On.EntityStates.Bandit2.Weapon.FireSidearmSkullRevolver.ModifyBullet -= OnSkullRevolverFire;
            On.RoR2.Run.Start -= OnRunStart;
        }

        private static bool IsCurrentStageValid() { return !ignoredScenesIndex.Contains((int)SceneCatalog.FindSceneIndex(SceneManager.GetActiveScene().name)); }

        // Clear the cache at the start of each run to avoid carrying stacks from previous run
        private static void OnRunStart(On.RoR2.Run.orig_Start orig, Run self)
        {
            stackCache.Clear();
            orig(self);
        }

        // On death or stage exit, check if player has stacks and hold on to them
        private static void OnExitingStage(On.RoR2.SceneExitController.orig_Begin orig, SceneExitController self)
        {
            if (!IsCurrentStageValid())
            {
                orig(self);
                return;
            }

            var instances = PlayerCharacterMasterController.instances;
            foreach (var playerCharacterMaster in PlayerCharacterMasterController.instances)
                if (!playerCharacterMaster.master.IsDeadAndOutOfLivesServer() &&  playerCharacterMaster.master.GetBody().HasBuff(RoR2Content.Buffs.BanditSkull))
                    stackCache.TryAdd(playerCharacterMaster, playerCharacterMaster.master.GetBody().GetBuffCount(RoR2Content.Buffs.BanditSkull));    
            orig(self);
        }

        private static void OnPlayerDeath(On.RoR2.CharacterBody.orig_OnDeathStart orig, CharacterBody self)
        {
            if (!IsCurrentStageValid())
            {
                orig(self);
                return;
            }

            if (self.isPlayerControlled)
                if (self.HasBuff(RoR2Content.Buffs.BanditSkull))
                    stackCache.TryAdd(self.master.playerCharacterMasterController, self.GetBuffCount(RoR2Content.Buffs.BanditSkull));
            orig(self);
        }
       
        // Gives half of the stored stacks to the player on spawn
        private static void OnBodyStart(On.RoR2.CharacterBody.orig_Start orig, CharacterBody self)
        {
            orig(self);
            if (!IsCurrentStageValid())
                return;

            if (self.isPlayerControlled)
            {              
                int skullValue = 0;
                float stackLoss = Math.Clamp(configStackLoss.Value, 0, 1f);
                if(stackCache.TryGetValue(self.master.playerCharacterMasterController, out skullValue))
                {
                    self.AddBuff(RoR2Content.Buffs.BanditSkull);
                    self.SetBuffCount(RoR2Content.Buffs.BanditSkull.buffIndex, (int)(skullValue * stackLoss));
                    stackCache.Remove(self.master.playerCharacterMasterController);
                }
            }
        }

        // Replace the damage calculation of the original skill
        // /!\ Does not invoke the original function. Might create conflict with other mods using this skill
        private static void OnSkullRevolverFire(On.EntityStates.Bandit2.Weapon.FireSidearmSkullRevolver.orig_ModifyBullet orig, FireSidearmSkullRevolver self, BulletAttack bulletAttack)
        {
            int num = 0;
            if ((bool)self.characterBody)
            {
                num = self.characterBody.GetBuffCount(RoR2Content.Buffs.BanditSkull);
            }
            bulletAttack.damage *= configBaseDmg.Value + configDmgScaling.Value * (float)num;
            bulletAttack.damageType |= (DamageTypeCombo)DamageType.GiveSkullOnKill;
        }      
    }
}
