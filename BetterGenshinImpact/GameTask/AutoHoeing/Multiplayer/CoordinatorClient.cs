#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

public class CoordinatorClient : IAsyncDisposable
{
    private readonly ILogger<CoordinatorClient> _logger = App.GetLogger<CoordinatorClient>();
    private HubConnection? _connection;
    private Timer? _heartbeatTimer;
    
    // 保存房间信息用于重连
    private string? _currentRoomCode;
    private string? _playerName;
    private string? _playerUid;

    // === 成员异常恢复状态（需求 7）===
    private readonly ConcurrentDictionary<string, MemberStatus> _memberStatuses = new();
    private readonly ConcurrentDictionary<string, long> _memberStatusVersions = new();
    private long _statusVersion;

    public event Action<List<PlayerInfo>>? PlayerListUpdated;
    public event Action<string>? AllArrived;
    public event Action<string>? AllFightDone;
    public event Action<List<string>>? RouteDiffReceived;
    public event Action? RouteVerificationPassed;
    public event Action? OnDegraded;
    public event Action<string>? RoomClosed;
    public event Action? RouteVerificationAllDone;
    public event Action<int>? KazuhaPlayerUpdated;
    public event Action? AllWorldJoined;
    public event Action<bool>? HostReadyChanged;
    public event Action<List<string>>? HostRouteListReady;
    public event Action<string, MemberStatus>? OnMemberStatusChanged;

    public bool IsConnected =>
        _connection?.State == HubConnectionState.Connected;

    public int CurrentRoomPlayerCount { get; private set; }
    public string HostPlayerUid { get; private set; } = "";
    public List<PlayerInfo> CurrentPlayerList { get; private set; } = new();

    /// <summary>是否有成员处于 Fighting 状态</summary>
    public bool HasFightingMembers => _memberStatuses.Values.Any(s => s == MemberStatus.Fighting);
    /// <summary>是否有成员处于 Rejoining 状态</summary>
    public bool HasRejoiningMembers => _memberStatuses.Values.Any(s => s == MemberStatus.Rejoining);
    /// <summary>是否有成员处于 Reviving 状态</summary>
    public bool HasRevivingMembers => _memberStatuses.Values.Any(s => s == MemberStatus.Reviving);
    /// <summary>当前成员状态字典（只读视图）</summary>
    public IReadOnlyDictionary<string, MemberStatus> MemberStatuses => _memberStatuses;

    public async Task<bool> ConnectAsync(string serverUrl, CancellationToken ct)
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(serverUrl)
                .Build();

            _connection.On<List<PlayerInfo>>("PlayerListUpdated",
                list =>
                {
                    CurrentRoomPlayerCount = list.Count;
                    if (list.Count > 0)
                        HostPlayerUid = list[0].PlayerUid;
                    CurrentPlayerList = new List<PlayerInfo>(list);

                    // 清理不在玩家列表中的过期状态条目（需求 7）
                    var activeUids = list.Select(p => p.PlayerUid).ToHashSet();
                    foreach (var key in _memberStatuses.Keys.Where(k => !activeUids.Contains(k)).ToList())
                    {
                        _memberStatuses.TryRemove(key, out _);
                        _memberStatusVersions.TryRemove(key, out _);
                    }

                    PlayerListUpdated?.Invoke(list);
                });

            _connection.On<string>("AllArrived",
                syncPointId => AllArrived?.Invoke(syncPointId));

            _connection.On<string>("AllFightDone",
                syncPointId => AllFightDone?.Invoke(syncPointId));

            _connection.On<List<string>>("RouteDiffReceived",
                diff => RouteDiffReceived?.Invoke(diff));

            _connection.On("RouteVerificationPassed",
                () => RouteVerificationPassed?.Invoke());

            _connection.On<string>("RoomClosed",
                reason => RoomClosed?.Invoke(reason));

            _connection.On("RouteVerificationAllDone",
                () => RouteVerificationAllDone?.Invoke());

            _connection.On<int>("KazuhaPlayerUpdated",
                index => KazuhaPlayerUpdated?.Invoke(index));

            _connection.On("AllWorldJoined",
                () => AllWorldJoined?.Invoke());

            _connection.On<bool>("HostReadyChanged",
                ready => HostReadyChanged?.Invoke(ready));

            _connection.On<List<string>>("HostRouteListReady",
                routeNames => HostRouteListReady?.Invoke(routeNames));

            // 成员异常恢复状态变化（需求 7）
            _connection.On<string, string, long>("MemberStatusChanged",
                (playerUid, statusStr, version) =>
                {
                    if (!Enum.TryParse<MemberStatus>(statusStr, out var status)) return;

                    // 版本号检查：只接受更大版本号的更新，防止网络延迟导致的乱序覆盖
                    var accepted = _memberStatusVersions.AddOrUpdate(
                        playerUid,
                        _ => version, // 新条目直接接受
                        (_, oldVersion) => version > oldVersion ? version : oldVersion
                    );
                    if (accepted != version) return; // 版本号不够大，丢弃

                    if (status == MemberStatus.Offline)
                    {
                        _memberStatuses.TryRemove(playerUid, out _);
                        _memberStatusVersions.TryRemove(playerUid, out _);
                    }
                    else
                    {
                        _memberStatuses[playerUid] = status;
                    }

                    OnMemberStatusChanged?.Invoke(playerUid, status);
                });

            _connection.Closed += OnConnectionClosed;

            await _connection.StartAsync(ct);
            _logger.LogInformation("CoordinatorClient 已连接到 {Url}", serverUrl);

            // 启动心跳定时器，每 30 秒发送一次
            _heartbeatTimer = new Timer(async _ =>
            {
                try { await SendHeartbeatAsync(); }
                catch { /* 忽略心跳异常 */ }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CoordinatorClient 连接失败: {Url}", serverUrl);
            return false;
        }
    }

    private async Task OnConnectionClosed(Exception? ex)
    {
        _logger.LogWarning(ex, "CoordinatorClient 连接断开，尝试重连...");
        if (_connection == null) return;

        try
        {
            await _connection.StartAsync();
            _logger.LogInformation("CoordinatorClient 重连成功");
            
            // 重连后重新加入房间
            if (!string.IsNullOrEmpty(_currentRoomCode))
            {
                _logger.LogInformation("重连后重新加入房间: {RoomCode}", _currentRoomCode);
                var rejoined = await JoinRoomAsync(_currentRoomCode, _playerName ?? "", _playerUid ?? "");
                if (rejoined)
                {
                    _logger.LogInformation("重新加入房间成功: {RoomCode}", _currentRoomCode);
                }
                else
                {
                    _logger.LogWarning("重新加入房间失败: {RoomCode}", _currentRoomCode);
                }
            }
        }
        catch (Exception retryEx)
        {
            _logger.LogError(retryEx, "CoordinatorClient 重连失败，触发降级");
            OnDegraded?.Invoke();
        }
    }

    public async Task<string?> CreateRoomAsync(string playerName = "", List<string>? whitelist = null, string playerUid = "", int expectedPlayerCount = 4)
    {
        if (_connection == null) return null;
        try
        {
            var roomCode = await _connection.InvokeAsync<string>("CreateRoom", playerName ?? "", whitelist ?? new List<string>(), playerUid ?? "", expectedPlayerCount);
            if (!string.IsNullOrEmpty(roomCode))
            {
                // 保存房间信息用于重连
                _currentRoomCode = roomCode;
                _playerName = playerName;
                _playerUid = playerUid;
            }
            return roomCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateRoomAsync 失败");
            return null;
        }
    }

    public async Task<bool> JoinRoomAsync(string roomCode, string playerName = "", string playerUid = "")
    {
        if (_connection == null) return false;
        try
        {
            var success = await _connection.InvokeAsync<bool>("JoinRoom", roomCode, playerName ?? "", playerUid ?? "");
            if (success)
            {
                // 保存房间信息用于重连
                _currentRoomCode = roomCode;
                _playerName = playerName;
                _playerUid = playerUid;
            }
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JoinRoomAsync 失败: {RoomCode}", roomCode);
            return false;
        }
    }

    public async Task ReportArrivalAsync(string syncPointId)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ReportArrival", syncPointId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportArrivalAsync 失败: {SyncPointId}", syncPointId);
        }
    }

    public async Task ReportFightDoneAsync(string syncPointId)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ReportFightDone", syncPointId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportFightDoneAsync 失败: {SyncPointId}", syncPointId);
        }
    }

    public async Task ReportRouteListAsync(List<RouteHash> routes)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ReportRouteList", routes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportRouteListAsync 失败");
        }
    }

    public async Task ReportRouteVerificationDoneAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ReportRouteVerificationDone");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportRouteVerificationDoneAsync 失败");
        }
    }

    public async Task ReportWorldJoinedAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ReportWorldJoined");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportWorldJoinedAsync 失败");
        }
    }

    public async Task SetKazuhaPlayerAsync(int index)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("SetKazuhaPlayer", index);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetKazuhaPlayerAsync 失败");
        }
    }

    public async Task UpdateWhitelistAsync(List<string> whitelist)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("UpdateWhitelist", whitelist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateWhitelistAsync 失败");
        }
    }

    public async Task<List<RoomSummary>> GetOnlineRoomsAsync()
    {
        if (_connection == null) return [];
        try
        {
            return await _connection.InvokeAsync<List<RoomSummary>>("GetOnlineRooms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetOnlineRoomsAsync 失败");
            return [];
        }
    }

    public async Task SetRoomConfigAsync(RoomConfig config)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("SetRoomConfig", config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetRoomConfigAsync 失败");
        }
    }

    public async Task<RoomConfig?> GetRoomConfigAsync()
    {
        if (_connection == null) return null;
        try
        {
            return await _connection.InvokeAsync<RoomConfig?>("GetRoomConfig");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRoomConfigAsync 失败");
            return null;
        }
    }

    public async Task ReportHostReadyAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ReportHostReady");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportHostReadyAsync 失败");
        }
    }

    public async Task<bool> IsHostReadyAsync()
    {
        if (_connection == null) return false;
        try
        {
            return await _connection.InvokeAsync<bool>("IsHostReady");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IsHostReadyAsync 失败");
            return false;
        }
    }

    public async Task SetHostRouteListAsync(List<string> routeNames)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("SetHostRouteList", routeNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetHostRouteListAsync 失败");
        }
    }

    public async Task<List<string>> GetHostRouteListAsync()
    {
        if (_connection == null) return [];
        try
        {
            return await _connection.InvokeAsync<List<string>>("GetHostRouteList");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHostRouteListAsync 失败");
            return [];
        }
    }

    public async Task SendHeartbeatAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("Heartbeat");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SendHeartbeatAsync 失败");
        }
    }

    public async Task CloseRoomAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("CloseRoom");
            _logger.LogInformation("CloseRoomAsync 已发送关闭房间请求");
            // 关闭房间后清空房间码，防止重连时重新加入已关闭的旧房间
            _currentRoomCode = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CloseRoomAsync 失败");
        }
    }

    public async Task ResetWorldJoinedAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ResetWorldJoined");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResetWorldJoinedAsync 失败");
        }
    }

    /// <summary>
    /// 上报成员异常恢复状态。断线时静默失败，不抛异常。
    /// 携带递增版本号，接收方只接受更大版本号的更新，防止网络延迟导致的乱序覆盖。
    /// </summary>
    public async Task ReportMemberStatusAsync(MemberStatus status)
    {
        if (_connection == null || !IsConnected) return; // 断线时静默失败
        try
        {
            var version = Interlocked.Increment(ref _statusVersion);
            await _connection.InvokeAsync("ReportMemberStatus", status.ToString(), version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportMemberStatusAsync 失败（静默忽略），状态: {Status}", status);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection == null) return;
        try
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _connection.Closed -= OnConnectionClosed;
            await _connection.StopAsync();
            _logger.LogInformation("CoordinatorClient 已断开连接");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DisconnectAsync 时发生异常");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        if (_connection != null)
        {
            _connection.Closed -= OnConnectionClosed;
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
