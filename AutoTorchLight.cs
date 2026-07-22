#nullable disable
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp; // 引入 The Long Dark 的 Il2Cpp 命名空间

[assembly: MelonInfo(typeof(AutoTorchLight.AutoTorchMain), "AutoTorchLight", "1.0.0", "hzb1130")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace AutoTorchLight
{
    public class AutoTorchMain : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Settings.OnLoad();
            // MelonLogger.Msg("AutoTorchLight (自动火把) 已加载！");
        }

        public override void OnUpdate()
        {
            if (Settings.options == null)
                return;

            /* 快捷键检测（如需要可自行取消注释）
            if (Settings.options.enableAutoTorch && Input.GetKeyDown(KeyCode.G))
            {
                EquipAndLightNextTorch(null);
            }
            */
        }

        // 核心功能：寻找合适的备用火把，装备并点燃
        // excludeItem: 传入需要排除的火把（即刚刚熄灭的那把）
        private static void EquipAndLightNextTorch(GearItem excludeItem = null)
        {
            PlayerManager playerManager = GameManager.GetPlayerManagerComponent();
            Inventory inventory = GameManager.GetInventoryComponent();
            if (playerManager == null || inventory == null) return;

            GearItem itemInHands = playerManager.m_ItemInHands;
            GearItem targetTorch = null;
            
            // 0 = 最高耐久, 1 = 最低耐久
            float targetCondition = Settings.options.selectionMode == 0 ? -1f : 101f;

            // 1. 遍历寻找最符合设置偏好的火把
            for (int i = 0; i < inventory.m_Items.Count; i++)
            {
                GearItem item = inventory.m_Items[i];
                
                // 排除空物品、当前拿在手里的物品、以及【刚熄灭的那个火把】
                if (item == null || item == itemInHands || item == excludeItem)
                    continue;

                TorchItem torch = item.m_TorchItem;
                if (torch == null)
                    continue;

                float cond = item.GetNormalizedCondition() * 100f;
                
                // 耐久度 <= 1% 的已报废火把直接过滤掉
                if (cond <= 1f)
                    continue;

                bool isBetter = false;
                
                // 0 代表选择“最高耐久”
                if (Settings.options.selectionMode == 0 && cond > targetCondition)
                {
                    isBetter = true;
                }
                // 1 代表选择“最低耐久”
                else if (Settings.options.selectionMode == 1 && cond < targetCondition)
                {
                    isBetter = true;
                }

                if (isBetter)
                {
                    targetCondition = cond;
                    targetTorch = item;
                }
            }

            // 2. 完美的装备与点燃连招
            if (targetTorch != null)
            {
                float originalHP = targetTorch.m_CurrentHP; 
                float normalizedCond = targetTorch.GetNormalizedCondition();
                
                playerManager.EquipItem(targetTorch, false);
                
                targetTorch.m_TorchItem.Ignite();
                
                targetTorch.m_CurrentHP = originalHP;
                
                if (targetTorch.m_TorchItem.m_BurnLifetimeMinutes > 0f)
                {
                    float burnTime = targetTorch.m_TorchItem.m_BurnLifetimeMinutes * (1f - normalizedCond);
                    targetTorch.m_TorchItem.m_ElapsedBurnMinutes = burnTime;
                }
            }
        }
        
        // 当玩家手中的火把熄灭时被调用
        public static void HandleTorchExtinguished(TorchItem extinguishedTorch)
        {
            if (Settings.options == null || !Settings.options.enableAutoTorch)
                return;

            // 获取刚刚熄灭的火把对应的 GearItem
            GearItem extinguishedGear = extinguishedTorch != null ? extinguishedTorch.GetComponent<GearItem>() : null;
            
            // 将刚刚熄灭的火把传入排除参数中
            EquipAndLightNextTorch(extinguishedGear);
        }
    }

    // =================================================================
    // Harmony Patch - 监听火把熄灭事件
    // =================================================================
    [HarmonyPatch(typeof(TorchItem), nameof(TorchItem.Extinguish))]
    internal class TorchItem_Extinguish_Patch
    {
        // 移除导致报错的 TorchOutReason 参数，只保留 __instance
        static void Postfix(TorchItem __instance)
        {
            // 【核心修复】：直接调用游戏原生的 IsBurnedOut() 方法！
            // 只有耐久耗尽自然熄灭时才会返回 true。
            // 完美过滤掉：手动扑灭、按H收起、遇水、被风吹灭等所有其他情况。
            if (!__instance.IsBurnedOut())
            {
                return;
            }

            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            
            // 安全判定：只有当玩家拿在手里的物品就是刚刚熄灭的火把时，才触发接力。
            if (pm != null && pm.m_ItemInHands != null && pm.m_ItemInHands.m_TorchItem == __instance)
            {
                AutoTorchMain.HandleTorchExtinguished(__instance);
            }
        }
    }
}