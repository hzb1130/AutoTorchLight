#nullable disable
using ModSettings;
using System.Reflection;
using UnityEngine;

namespace AutoTorchLight
{
    internal class AutoTorchSettings : JsonModSettings
    {
        [Section("General | 总设置")]

        [Name("Enable Auto Torch | 开启自动续火把")]
        [Description("Enable manual and automatic torch switching features. | 开启手动和自动更换火把功能。")]
        public bool enableFeature = false;

        [Section("Switch Method | 换火把方式")]

        [Name("Enable Manual Switch | 启用手动更换")]
        [Description("Allow switching the burning torch manually with a configured key. | 允许通过指定按键手动更换正在燃烧的火把。")]
        public bool enableManualSwitch = false;

        [Name("Manual Switch Key | 手动更换按键")]
        [Description("Press this key to replace the currently burning torch. | 按下此键更换当前正在燃烧的火把。")]
        public KeyCode manualSwitchKey = KeyCode.Keypad1;

        [Name("Automatic Switch Mode | 自动更换方式")]
        [Description("Choose when the torch should be replaced automatically. | 选择自动更换火把的触发方式。")]
        [Choice("Off | 关闭", "Burnout | 烧完", "Low Condition | 低耐久")]
        public int automaticSwitchMode = 0;

        [Name("Low Condition Threshold | 低耐久阈值")]
        [Description("Replace the burning torch when its condition reaches or falls below this percentage. | 当正在燃烧的火把耐久达到或低于该百分比时进行更换。")]
        [Slider(1, 20, 20)]
        public int lowConditionThreshold = 5;

        [Section("Switch Settings | 换火把设置")]

        [Name("Current Torch Action | 当前火把处理")]
        [Description("Choose whether to drop the old torch or return it to your inventory. | 选择丢弃旧火把，或者将其收回背包。")]
        [Choice("Drop | 丢弃", "Holster | 收回")]
        public int currentTorchAction = 0;

        [Name("Current Torch Action | 当前火把处理")]
        [Description("Low-condition switching can only discard the current torch. | 低耐久更换只能丢弃当前火把。")]
        [Choice("Drop | 丢弃", "Drop | 丢弃")]
        public int lowConditionTorchAction = 0;

        [Name("Inventory Torch Preference | 背包火把选择偏好")]
        [Description("Choose whether to use the lowest-condition or highest-condition available torch first. | 选择优先使用耐久最低或耐久最高的可用火把。")]
        [Choice("Lowest Condition | 最低耐久", "Highest Condition | 最高耐久")]
        public int inventoryTorchPreference = 0;

        [Name("Last Torch Warning | 最后火把提醒")]
        [Description("Show a warning when no other usable torch remains after switching. | 更换后没有其他可用火把时显示提醒。")]
        public bool warnWhenNoBackupTorch = true;

        protected override void OnChange(FieldInfo field, object oldValue, object newValue)
        {
            base.OnChange(field, oldValue, newValue);

            if (field.Name == nameof(automaticSwitchMode) &&
                automaticSwitchMode == 2)
            {
                currentTorchAction = 0;
                lowConditionTorchAction = 0;
            }

            if (field.Name == nameof(enableFeature) ||
                field.Name == nameof(enableManualSwitch) ||
                field.Name == nameof(automaticSwitchMode))
            {
                RefreshAll();
            }
        }

        protected override void OnConfirm()
        {
            if (automaticSwitchMode == 2)
            {
                currentTorchAction = 0;
                lowConditionTorchAction = 0;
            }

            base.OnConfirm();
            RefreshAll();
        }

        public void RefreshAll()
        {
            bool enabled = enableFeature;
            bool lowConditionMode = enabled && automaticSwitchMode == 2;

            SetFieldVisible(nameof(enableManualSwitch), enabled);
            SetFieldVisible(nameof(manualSwitchKey), enabled && enableManualSwitch);
            SetFieldVisible(nameof(automaticSwitchMode), enabled);
            SetFieldVisible(nameof(lowConditionThreshold), lowConditionMode);

            SetFieldVisible(nameof(currentTorchAction), enabled && !lowConditionMode);
            SetFieldVisible(nameof(lowConditionTorchAction), lowConditionMode);
            SetFieldVisible(nameof(inventoryTorchPreference), enabled);
            SetFieldVisible(nameof(warnWhenNoBackupTorch), enabled);
        }
    }

    internal static class Settings
    {
        public static AutoTorchSettings options;

        public static void OnLoad()
        {
            options = new AutoTorchSettings();
            options.AddToModSettings("Auto Torch Light | 自动续火把");
            options.RefreshAll();
        }
    }
}