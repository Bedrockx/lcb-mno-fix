namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 阿蕾奇诺元素爆发红血门控的纯函数决策（PBT 友好，无外部依赖、无屏幕采样）。
/// 详见 .kiro/specs/arlecchino-q-low-hp-gate/design.md。
/// </summary>
public static class ArlecchinoBurstGateDecisions
{
    /// <summary>
    /// 阿蕾奇诺角色名（硬编码）。
    /// </summary>
    public const string ArlecchinoName = "阿蕾奇诺";

    /// <summary>
    /// 是否应对本次 Q 释放执行红血门控（即"先检测红血、非红血则跳过 Q"）。
    /// 仅当 config 非 null 且开关开启且角色名为阿蕾奇诺时返回 true。
    /// 返回 false 表示不门控，按既有逻辑正常放 Q（开关关闭 / 非阿蕾奇诺 / config 为 null）。
    /// </summary>
    public static bool ShouldGate(AutoFightConfig? config, string? avatarName)
    {
        if (config == null)
        {
            return false;
        }

        if (!config.ArlecchinoBurstLowHpGateEnabled)
        {
            return false;
        }

        return avatarName == ArlecchinoName;
    }
}
