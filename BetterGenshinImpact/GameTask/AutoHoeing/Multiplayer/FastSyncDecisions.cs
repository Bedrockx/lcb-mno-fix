using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 快速同步点抢报决策的纯函数集合。
///
/// 与 bgi-implementation-patterns.md §1 "决策函数纯化" 模式对齐：
/// - 仅依赖输入参数（AutoHoeingConfig 的字段值 / 直接参数），无外部依赖
/// - 不持有 logger / 不读 SignalR 状态 / 不调 TaskContext
/// - 便于 PBT 直接撒输入跑性质（详见 design.md §6 / §8.1）
///
/// internal static 可见性 — 复用 BetterGenshinImpact/AssemblyInfo.cs 中已配置的
/// [InternalsVisibleTo("BetterGenshinImpact.UnitTest")]，单元测试可直接调用。
/// </summary>
internal static class FastSyncDecisions
{
    /// <summary>
    /// 判定"路径同步点抢报看门狗"是否应该启动。
    ///
    /// 真值表（Validates: requirements FR6 / FR23 / FR24）：
    ///   isMultiplayer=false  → false（单机零回归）
    ///   isConnected=false    → false（断线时不启动）
    ///   syncIdNonNull=false  → false（waypoint 不在 _syncPointMap 中）
    ///   FastSyncPointEnabled=false → false（主开关短路）
    ///   全部 true            → true
    /// </summary>
    public static bool ShouldArmPathingWatcher(
        AutoHoeingConfig? config,
        bool isMultiplayer,
        bool isConnected,
        bool syncIdNonNull)
    {
        if (config == null) return false;
        if (!isMultiplayer) return false;
        if (!isConnected) return false;
        if (!syncIdNonNull) return false;
        return config.FastSyncPointEnabled;
    }

    /// <summary>
    /// 判定"当前距离 + 阈值 + gate 状态"是否应该触发抢报。
    ///
    /// 边界条件：
    /// - gateAlreadyArmed=true → false（OR 门已被另一路径抢，不重复发）
    /// - distance=NaN          → false（Navigation 失败兜底，不抢报）
    /// - distance=±Infinity    → false（防御性）
    /// - distance&lt;0          → false（防御性）
    /// - 0 ≤ distance ≤ threshold AND !gateAlreadyArmed → true
    ///
    /// Validates: requirements FR7 / FR15 / FR18
    /// </summary>
    public static bool ShouldFastReport(
        double distance,
        double threshold,
        bool gateAlreadyArmed)
    {
        if (gateAlreadyArmed) return false;
        if (double.IsNaN(distance)) return false;
        if (double.IsInfinity(distance)) return false;
        if (distance < 0) return false;
        return distance <= threshold;
    }

    /// <summary>
    /// 把 FastSyncPathingDistance 持久化值 clamp 到合法范围 [5.0, 30.0]。
    /// NaN / Infinity → 默认 10.0（与 AutoHoeingConfig 字段默认值一致）。
    /// Validates: requirements FR25 / FR27
    /// </summary>
    public static double ClampPathingDistance(double raw)
    {
        if (double.IsNaN(raw) || double.IsInfinity(raw)) return 10.0;
        return Math.Clamp(raw, 5.0, 30.0);
    }

    /// <summary>
    /// 把 FastSyncTeleportLoadingDelayMs 持久化值 clamp 到合法范围 [0, 3000]。
    /// Validates: requirements FR26 / FR27
    /// </summary>
    public static int ClampTeleportDelay(int raw)
    {
        return Math.Clamp(raw, 0, 3000);
    }
}
