namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 多世界轮换"空轮预跳过"判定（纯函数，PBT 友好，无外部依赖）。
/// multiplayer-host-empty-round-preskip-before-world-join。
///
/// 与 RunSingleWorldCoreAsync 步骤1 的 CD 过滤规则保持完全一致：
///   StartRouteIndex > 0 || !IsOnCooldown(route)
/// 使"进世界前预判"与"进世界后步骤1判空"对同一输入产生一致结论。
/// </summary>
public static class EmptyRoundPreSkipDecisions
{
    /// <summary>
    /// 计算房主本轮"实际会执行"的线路文件名集合，与执行端 ProcessRoutesByGroup 对齐：
    ///   1) CD 过滤：StartRouteIndex>0 旁路 CD（全部计入），否则剔除 CD 中线路（与步骤1 SetHostRouteList 一致）；
    ///   2) 起始偏移：再 Skip(Math.Max(0, StartRouteIndex-1))（与 groupRoutes.Skip(startIndex) 一致）。
    /// 二者叠加后为空 → 本轮进世界也无线路可跑 → Empty_Round。
    /// multiplayer-preskip-ignores-startrouteindex-offset-fix：
    /// StartRouteIndex 超过线路数导致 Skip 后为 0 条，也必须在进世界前判空。
    /// </summary>
    /// <param name="routeFileNames">本轮分组过滤后（Group==targetGroup && Selected）的线路文件名，顺序保留。</param>
    /// <param name="startRouteIndex">_config.StartRouteIndex；>0 时旁路 CD，且作为 1-based 起始偏移 Skip(startRouteIndex-1)。</param>
    /// <param name="isOnCooldown">线路文件名 → 是否在 CD 的委托。</param>
    public static List<string> FilterHostRouteSet(
        IEnumerable<string> routeFileNames, int startRouteIndex, Func<string, bool> isOnCooldown)
    {
        if (routeFileNames == null) return new List<string>();

        // 1) CD 过滤（与步骤1 SetHostRouteList 规则一致）
        List<string> afterCd;
        if (startRouteIndex > 0)
        {
            afterCd = routeFileNames.ToList(); // StartRouteIndex>0 旁路 CD，全部线路计入
        }
        else
        {
            var cd = isOnCooldown ?? (_ => false);
            afterCd = routeFileNames.Where(name => !cd(name)).ToList();
        }

        // 2) 起始偏移 Skip（与执行端 ProcessRoutesByGroup 的 groupRoutes.Skip(Math.Max(0, StartRouteIndex-1)) 对齐）。
        // StartRouteIndex 跨过全部线路时 Skip 后为空 → 实际无线路可跑 → Empty_Round。
        var skip = Math.Max(0, startRouteIndex - 1);
        return skip > 0 ? afterCd.Skip(skip).ToList() : afterCd;
    }

    /// <summary>
    /// 本轮房主是否为 Empty_Round（CD 过滤后无可跑线路）。
    /// </summary>
    public static bool IsEmptyRound(
        IEnumerable<string> routeFileNames, int startRouteIndex, Func<string, bool> isOnCooldown)
        => FilterHostRouteSet(routeFileNames, startRouteIndex, isOnCooldown).Count == 0;
}
