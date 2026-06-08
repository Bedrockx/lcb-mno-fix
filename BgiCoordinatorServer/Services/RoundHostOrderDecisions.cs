using System.Collections.Generic;
using System.Linq;
using BgiCoordinatorServer.Models;

namespace BgiCoordinatorServer.Services;

/// <summary>
/// 多世界权威轮换序列生成（纯函数，PBT 友好）。
/// multiplayer-server-authoritative-round-order。
///
/// 由首任房主 MarkRoomStarted（首轮全员已在房间）时生成一次，存入 Room.RoundHostOrder，
/// 客户端查询后据此构造 playerOrder，保证所有客户端轮换序列完全一致。
/// </summary>
public static class RoundHostOrderDecisions
{
    /// <summary>
    /// 生成轮换序列（UID 列表，第 i 项 = 第 i 轮房主 UID）：
    ///   - 首任房主 UID 在第 0 位；
    ///   - 其余玩家按 PlayerUid 序数升序；
    ///   - 过滤空 UID 玩家（无法 UID 寻址）；UID 去重。
    /// hostUid 为空或不在列表 → 仅按 UID 升序（退化，不置顶）。
    /// </summary>
    public static List<string> Build(IEnumerable<PlayerInfo> players, string hostUid)
    {
        var uids = (players ?? Enumerable.Empty<PlayerInfo>())
            .Where(p => p != null && !string.IsNullOrEmpty(p.PlayerUid))
            .Select(p => p.PlayerUid)
            .Distinct()
            .ToList();
        if (uids.Count == 0) return new List<string>();

        var rest = uids.Where(u => u != hostUid)
                       .OrderBy(u => u, System.StringComparer.Ordinal)
                       .ToList();

        var result = new List<string>();
        if (!string.IsNullOrEmpty(hostUid) && uids.Contains(hostUid))
            result.Add(hostUid);
        result.AddRange(rest);
        return result;
    }
}
