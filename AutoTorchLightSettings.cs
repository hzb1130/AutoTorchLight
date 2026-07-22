#nullable disable
using ModSettings;
using System.Reflection;

namespace AutoTorchLight
{
    internal class AutoTorchSettings : JsonModSettings
    {
        [Section("自动火把设置 | Auto Torch Settings")]

        [Name("开启自动火把 (Enable Auto Torch)")]
        [Description("火把熄灭后自动点燃下一个（不消耗火柴）。Automatically ignite the next torch after the current one burns out, no matches consumed.")]
        public bool enableAutoTorch = false;

        [Name("火把选择偏好 (Torch Selection)")]
        [Description("从背包中取出下一个火把时，优先选择最高耐久还是最低耐久。Choose to prioritize highest or lowest remaining burn time when grabbing torches from inventory.")]
        [Choice("最高耐久|Max Remaining Time", "最低耐久|Min Remaining Time")]
        public int selectionMode = 1; // 0 = 最高耐久, 1 = 最低耐久 (默认选择 1)


        // ==================== 动态折叠逻辑 ====================

        protected override void OnChange(FieldInfo field, object oldValue, object newValue)
        {
            base.OnChange(field, oldValue, newValue);

            // 当 enableAutoTorch 的值在菜单中被玩家点击改变时，触发刷新逻辑
            if (field.Name == nameof(enableAutoTorch))
            {
                RefreshAll();
            }
        }

        public void RefreshAll()
        {
            UpdateAllVisibility(enableAutoTorch);
        }

        public void UpdateAllVisibility(bool isEnable)
        {
            // 如果总开关为 true，则显示 selectionMode；为 false 则隐藏
            SetFieldVisible(nameof(selectionMode), isEnable);
        }

        protected override void OnConfirm()
        {
            base.OnConfirm();
            RefreshAll();
        }
    }

    internal static class Settings
    {
        public static AutoTorchSettings options;

        public static void OnLoad()
        {
            options = new AutoTorchSettings();
            options.AddToModSettings("Auto Torch Light/自动续火把");

            // 初始化时调用一次刷新，确保如果一开始是关闭状态，子选项也能正确隐藏
            options.RefreshAll();
        }
    }
}