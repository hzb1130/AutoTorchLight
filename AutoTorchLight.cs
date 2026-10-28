#nullable disable
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(AutoTorchLight.AutoTorchMain), "AutoTorchLight", "1.2.4", "hzb1130")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace AutoTorchLight
{
    public class AutoTorchMain : MelonMod
    {
        private enum SwitchReason
        {
            Manual,
            Burnout,
            LowCondition
        }

        private enum SwitchState
        {
            Idle,
            Queued,
            WaitingForEquip,
            WaitingToDropOldTorch
        }

        private static SwitchState switchState = SwitchState.Idle;
        private static SwitchReason switchReason;

        private static GearItem oldGear;
        private static TorchItem oldTorch;
        private static GearItem targetGear;
        private static TorchItem targetTorch;
        private static GearItem warnedTorch;

        private static float targetOriginalHP;
        private static float targetOriginalElapsedBurn;
        private static float stateStartTime;
        private static float idleStartTime = -1f;
        private static float nextAutomaticCheckTime;

        private static int earliestStartFrame;
        private static bool targetIgnited;

        // 加载设置并初始化状态。
        public override void OnInitializeMelon()
        {
            Settings.OnLoad();
            ResetState();
        }

        // 切换场景时清除失效的游戏对象引用。
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            ResetState();
            warnedTorch = null;
            nextAutomaticCheckTime = 0f;
        }

        // 更新当前切换流程并检查手动和低耐久触发。
        public override void OnUpdate()
        {
            if (Settings.options == null) return;

            if (!Settings.options.enableFeature)
            {
                ResetState();
                return;
            }

            if (switchState != SwitchState.Idle)
            {
                UpdateSwitch();
                return;
            }

            CheckManualSwitch();
            if (switchState == SwitchState.Idle) CheckLowConditionSwitch();
        }

        // 检查手动更换按键。
        private static void CheckManualSwitch()
        {
            if (!Settings.options.enableManualSwitch || !Input.GetKeyDown(Settings.options.manualSwitchKey)) return;

            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            GearItem gear = pm != null ? pm.m_ItemInHands : null;
            TorchItem torch = gear != null ? gear.m_TorchItem : null;

            if (torch == null || !torch.IsBurning())
            {
                Log("手动更换取消：手中没有正在燃烧的火把。");
                return;
            }

            RequestSwitch(gear, torch, SwitchReason.Manual);
        }

        // 定时检查当前火把是否达到低耐久阈值。
        private static void CheckLowConditionSwitch()
        {
            if (Settings.options.automaticSwitchMode != 2 || Time.unscaledTime < nextAutomaticCheckTime) return;

            nextAutomaticCheckTime = Time.unscaledTime + 0.25f;

            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            GearItem gear = pm != null ? pm.m_ItemInHands : null;
            TorchItem torch = gear != null ? gear.m_TorchItem : null;

            if (torch == null || !torch.IsBurning()) return;
            if (warnedTorch != null && warnedTorch != gear) warnedTorch = null;
            if (GetConditionPercent(gear) <= Settings.options.lowConditionThreshold) RequestSwitch(gear, torch, SwitchReason.LowCondition);
        }

        // 接收火把自然燃尽事件。
        public static void HandleBurnedOutTorch(TorchItem torch)
        {
            if (Settings.options == null || !Settings.options.enableFeature || Settings.options.automaticSwitchMode != 1) return;
            if (switchState != SwitchState.Idle || torch == null || !torch.IsBurnedOut()) return;

            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            GearItem gear = torch.GetComponent<GearItem>();

            if (pm == null || gear == null || pm.m_ItemInHands != gear) return;

            RequestSwitch(gear, torch, SwitchReason.Burnout);
        }

        // 保存一项经过验证的更换请求。
        private static void RequestSwitch(GearItem currentGear, TorchItem currentTorch, SwitchReason reason)
        {
            if (!IsValidSourceTorch(currentGear, currentTorch, reason)) return;

            GearItem candidate = FindBestTorch(currentGear, reason);

            if (candidate == null)
            {
                WarnNoBackup(currentGear);
                return;
            }

            oldGear = currentGear;
            oldTorch = currentTorch;
            targetGear = candidate;
            targetTorch = candidate.m_TorchItem;
            switchReason = reason;
            earliestStartFrame = Time.frameCount + 1;
            targetIgnited = false;

            SetState(SwitchState.Queued);
        }

        // 执行当前更换阶段。
        private static void UpdateSwitch()
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();

            if (pm == null)
            {
                FailSwitch("无法获取玩家组件。");
                return;
            }

            switch (switchState)
            {
                case SwitchState.Queued:
                    UpdateQueued(pm);
                    break;
                case SwitchState.WaitingForEquip:
                    UpdateWaitingForEquip(pm);
                    break;
                case SwitchState.WaitingToDropOldTorch:
                    UpdateWaitingToDropOldTorch(pm);
                    break;
            }
        }

        // 等待玩家稳定空闲后提交一次原生装备请求。
        private static void UpdateQueued(PlayerManager pm)
        {
            if (Time.frameCount < earliestStartFrame) return;

            if (!IsValidSourceTorch(oldGear, oldTorch, switchReason) || pm.m_ItemInHands != oldGear)
            {
                FailSwitch("触发更换的火把已经发生变化。");
                return;
            }

            if (Input.anyKeyDown || IsPlayerBusy(pm))
            {
                idleStartTime = -1f;
                return;
            }

            if (!HasStableIdle(0.35f)) return;

            targetGear = FindBestTorch(oldGear, switchReason);

            if (targetGear == null)
            {
                WarnNoBackup(oldGear);
                FailSwitch("没有符合条件的备用火把。");
                return;
            }

            targetTorch = targetGear.m_TorchItem;

            if (!pm.ItemCanEquipInHands(targetGear))
            {
                idleStartTime = -1f;
                return;
            }

            targetOriginalHP = targetGear.m_CurrentHP;
            targetOriginalElapsedBurn = targetTorch.m_ElapsedBurnMinutes;

            Log("开始" + GetReasonName() + "。旧火把耐久=" + GetConditionPercent(oldGear).ToString("0.00") + "%，新火把耐久=" + GetConditionPercent(targetGear).ToString("0.00") + "%");

            SetState(SwitchState.WaitingForEquip);
            pm.EquipItem(targetGear, false);
            Log("已将火把切换交给 PlayerManager 原生装备流程。");
        }

        // 等待原生动作链把目标火把装备到手中。
        private static void UpdateWaitingForEquip(PlayerManager pm)
        {
            if (pm.m_ItemInHands == targetGear)
            {
                if (!IgniteTargetTorch())
                {
                    FailSwitch("目标火把已经进入手中，但无法点燃。");
                    return;
                }

                if (ShouldDropBurnedOutTorch())
                {
                    SetState(SwitchState.WaitingToDropOldTorch);
                    return;
                }

                CompleteSwitch();
                return;
            }

            GearItem held = pm.m_ItemInHands;

            if (held != null && held != oldGear && held != targetGear && !IsPlayerBusy(pm))
            {
                FailSwitch("玩家已经装备了其他物品。");
                return;
            }

            if (Time.unscaledTime - stateStartTime >= 30f)
            {
                FailSwitch("原生装备流程等待超时，未修改游戏动作状态。");
            }
        }

        // 等待原生装备动作结束后丢弃已经不在手中的燃尽火把。
        private static void UpdateWaitingToDropOldTorch(PlayerManager pm)
        {
            if (pm.m_ItemInHands != targetGear)
            {
                FailSwitch("丢弃旧火把前，手中的目标火把已经发生变化。");
                return;
            }

            if (!IsInInventory(oldGear))
            {
                CompleteSwitch();
                return;
            }

            if (Input.anyKeyDown || IsPlayerBusy(pm))
            {
                idleStartTime = -1f;
                return;
            }

            if (!HasStableIdle(0.35f)) return;

            oldGear.Drop(1, false, true, false);
            Log("燃尽火把已在离开手部系统后丢弃。");
            CompleteSwitch();
        }

        // 点燃已经由游戏原生流程装备完成的目标火把。
        private static bool IgniteTargetTorch()
        {
            if (targetIgnited) return true;
            if (targetGear == null || targetTorch == null || !targetTorch.CanBeLit()) return false;

            targetTorch.Ignite();
            targetGear.m_CurrentHP = targetOriginalHP;
            targetTorch.m_ElapsedBurnMinutes = targetOriginalElapsedBurn;
            targetIgnited = true;

            Log("新火把已经点燃。");
            return true;
        }

        // 检查触发更换的旧火把是否仍符合要求。
        private static bool IsValidSourceTorch(GearItem gear, TorchItem torch, SwitchReason reason)
        {
            if (gear == null || torch == null) return false;
            return reason == SwitchReason.Burnout ? torch.IsBurnedOut() : torch.IsBurning();
        }

        // 判断当前流程是否需要在切换成功后丢弃燃尽火把。
        private static bool ShouldDropBurnedOutTorch()
        {
            return switchReason == SwitchReason.Burnout && Settings.options.currentTorchAction == 0;
        }

        // 检查玩家是否处于不适合启动新装备动作的状态。
        private static bool IsPlayerBusy(PlayerManager pm)
        {
            return pm.IsActionInProgress ||
                   pm.m_QueuedEquipItem != null ||
                   pm.IsClickHoldActive() ||
                   pm.WillRestoreItemInHandsAfterInteraction() ||
                   pm.m_IsForcedInteractionActive ||
                   pm.m_IsHoldInteractionActive ||
                   Input.GetMouseButton(0) ||
                   Input.GetMouseButton(1);
        }

        // 要求玩家连续保持指定时间的空闲状态。
        private static bool HasStableIdle(float seconds)
        {
            if (idleStartTime < 0f)
            {
                idleStartTime = Time.unscaledTime;
                return false;
            }

            return Time.unscaledTime - idleStartTime >= seconds;
        }

        // 根据当前模式和耐久偏好选择备用火把。
        private static GearItem FindBestTorch(GearItem excludedGear, SwitchReason reason)
        {
            Inventory inventory = GameManager.GetInventoryComponent();
            if (inventory == null) return null;

            GearItem best = null;
            float bestCondition = Settings.options.inventoryTorchPreference == 0 ? 101f : -1f;

            for (int i = 0; i < inventory.m_Items.Count; i++)
            {
                GearItem item = inventory.m_Items[i];
                if (!IsUsableTorch(item, excludedGear, reason)) continue;

                float condition = GetConditionPercent(item);
                bool better = Settings.options.inventoryTorchPreference == 0 ? condition < bestCondition : condition > bestCondition;

                if (!better) continue;

                best = item;
                bestCondition = condition;
            }

            return best;
        }

        // 检查背包火把是否满足当前更换方式。
        private static bool IsUsableTorch(GearItem item, GearItem excludedGear, SwitchReason reason)
        {
            if (item == null || item == excludedGear || item.m_TorchItem == null) return false;

            float condition = GetConditionPercent(item);
            if (condition <= 1f || !item.m_TorchItem.CanBeLit()) return false;

            bool usesThreshold = reason == SwitchReason.Manual || reason == SwitchReason.LowCondition;
            return !usesThreshold || condition > Settings.options.lowConditionThreshold;
        }

        // 检查指定物品是否仍在玩家背包列表中。
        private static bool IsInInventory(GearItem gear)
        {
            if (gear == null) return false;

            Inventory inventory = GameManager.GetInventoryComponent();
            if (inventory == null) return false;

            for (int i = 0; i < inventory.m_Items.Count; i++)
            {
                GearItem item = inventory.m_Items[i];
                if (item == gear) return true;
            }

            return false;
        }

        // 完成更换并检查剩余备用火把。
        private static void CompleteSwitch()
        {
            Log(GetReasonName() + "完成。当前火把耐久=" + GetConditionPercent(targetGear).ToString("0.00") + "%");
            CheckLastTorchWarning();
            warnedTorch = null;
            ResetState();
        }

        // 结束模组状态，但不取消或强制清理游戏原生动作。
        private static void FailSwitch(string reason)
        {
            Log("更换结束：" + reason);
            ResetState();
            nextAutomaticCheckTime = Time.unscaledTime + 0.5f;
        }

        // 切换状态并重置空闲计时。
        private static void SetState(SwitchState state)
        {
            switchState = state;
            stateStartTime = Time.unscaledTime;
            idleStartTime = -1f;
        }

        // 清除模组自己保存的更换状态。
        private static void ResetState()
        {
            switchState = SwitchState.Idle;
            oldGear = null;
            oldTorch = null;
            targetGear = null;
            targetTorch = null;
            targetOriginalHP = 0f;
            targetOriginalElapsedBurn = 0f;
            stateStartTime = 0f;
            idleStartTime = -1f;
            earliestStartFrame = 0;
            targetIgnited = false;
        }

        // 在没有备用火把时只提醒一次。
        private static void WarnNoBackup(GearItem currentGear)
        {
            if (warnedTorch == currentGear) return;

            warnedTorch = currentGear;
            Log("没有符合条件的备用火把。");
        }

        // 更换完成后检查是否还存在其他可用火把。
        private static void CheckLastTorchWarning()
        {
            if (!Settings.options.warnWhenNoBackupTorch || targetGear == null) return;
            if (FindBestTorch(targetGear, switchReason) == null) ShowLastTorchWarning();
        }

        // 在游戏界面显示最后一根火把提醒。
        private static void ShowLastTorchWarning()
        {
            PlayerDamageEvent.SpawnDamageEvent(
                // damageEventName: "Last Torch",
                damageEventName: Localization.Language == "Simplified Chinese" ? "最后的火把" : "Last Torch",
                damageEventType: "",
                iconName: "ico_injury_warmingUp",
                tint: new Color(1f, 0.85f, 0.25f, 1f),
                fadeout: true,
                displayTime: 5f,
                fadeoutTime: 0.5f);
        }

        // 返回物品的百分比耐久。
        private static float GetConditionPercent(GearItem gear)
        {
            return gear != null ? gear.GetNormalizedCondition() * 100f : 0f;
        }

        // 返回当前更换原因的名称。
        private static string GetReasonName()
        {
            switch (switchReason)
            {
                case SwitchReason.Manual:
                    return "手动更换";
                case SwitchReason.Burnout:
                    return "燃尽自动更换";
                default:
                    return "低耐久自动更换";
            }
        }

        // 输出可选的调试日志。
        private static void Log(string message)
        {
            // MelonLogger.Msg("[自动续火把] " + message);
        }
    }

    [HarmonyPatch(typeof(TorchItem), nameof(TorchItem.Extinguish))]
    internal static class TorchItem_Extinguish_Patch
    {
        // 在火把自然燃尽后通知更换状态机。
        private static void Postfix(TorchItem __instance)
        {
            if (__instance.IsBurnedOut()) AutoTorchMain.HandleBurnedOutTorch(__instance);
        }
    }
}