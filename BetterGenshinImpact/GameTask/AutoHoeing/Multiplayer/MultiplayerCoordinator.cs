#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

public class MultiplayerCoordinator : IAsyncDisposable
{
    private readonly ILogger<MultiplayerCoordinator> _logger = App.GetLogger<MultiplayerCoordinator>();
    private readonly CoordinatorClient _client;
    private readonly SyncBarrier _barrier;
    private readonly SyncPointResolver _resolver;
    private readonly int _minPlayersToSync;
    private readonly int _syncTimeoutSeconds;

    private int _kazuhaPlayerIndex;
    private int _myPlayerIndex; // 1-based

    public bool IsActive { get; private set; } = true;
    public bool IsKazuhaPlayer => _kazuhaPlayerIndex > 0 && _myPlayerIndex == _kazuhaPlayerIndex;

    public event Action<string>? OnDegraded;

    public MultiplayerCoordinator(
        CoordinatorClient client,
        SyncBarrier barrier,
        SyncPointResolver resolver,
        int minPlayersToSync = 0,
        int syncTimeoutSeconds = 60)
    {
        _client = client;
        _barrier = barrier;
        _resolver = resolver;
        _minPlayersToSync = minPlayersToSync;
        _syncTimeoutSeconds = syncTimeoutSeconds;

        _client.OnDegraded += () => Degrade("连接断开且重连失败");
        _client.KazuhaPlayerUpdated += idx =>
        {
            _kazuhaPlayerIndex = idx;
            _logger.LogInformation("[联机] 万叶玩家索引更新为 {Idx}", idx);
        };
        _client.PlayerListUpdated += OnPlayerListUpdated;
    }

    private void OnPlayerListUpdated(List<PlayerInfo> players)
    {
        // 根据 ConnectionId 或 PlayerName 确定自己的序号（1-based）
        // 由于我们无法直接获取 ConnectionId，用列表顺序作为序号
        // 服务器广播的列表顺序是稳定的（加入顺序）
        _myPlayerIndex = 0; // 未知时为 0
        // PlayerListUpdated 会在 UI 线程处理，这里只记录人数
    }

    public async Task WaitForAllPlayers(string syncId, CancellationToken ct)
    {
        if (!IsActive)
        {
            _logger.LogDebug("[联机] 已降级，跳过集合等待 syncId={SyncId}", syncId);
            return;
        }

        var effectiveMin = _minPlayersToSync;
        if (effectiveMin == 0)
            effectiveMin = _client.CurrentRoomPlayerCount;

        if (effectiveMin <= 1)
        {
            _logger.LogInformation("[联机] 有效最低人数={Min}，跳过集合等待 syncId={SyncId}", effectiveMin, syncId);
            return;
        }

        try
        {
            await _barrier.WaitAsync(syncId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] WaitForAllPlayers 异常，syncId={SyncId}，跳过同步继续执行", syncId);
        }
    }

    /// <summary>等待所有玩家完成路线验证。</summary>
    public async Task WaitForRouteVerificationAsync(CancellationToken ct)
    {
        if (!IsActive) return;

        var effectiveMin = _minPlayersToSync == 0 ? _client.CurrentRoomPlayerCount : _minPlayersToSync;
        if (effectiveMin <= 1) return;

        _logger.LogInformation("[联机] 等待所有玩家完成路线验证...");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_syncTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        Action? handler = null;
        handler = () => tcs.TrySetResult(true);

        _client.RouteVerificationAllDone += handler;
        try
        {
            // 先上报一次
            await _client.ReportRouteVerificationDoneAsync();

            // 设置重试机制，每10秒重试一次上报
            var retryTimer = new Timer(async _ =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    _logger.LogDebug("[联机] 重试上报路线验证完成状态");
                    await _client.ReportRouteVerificationDoneAsync();
                }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

            using var reg = linkedCts.Token.Register(() =>
            {
                retryTimer?.Dispose();
                if (ct.IsCancellationRequested)
                    tcs.TrySetCanceled(ct);
                else
                {
                    _logger.LogWarning("[联机] 路线验证同步等待超时({Timeout}s)，自动放行", _syncTimeoutSeconds);
                    tcs.TrySetResult(false);
                }
            });

            var result = await tcs.Task;
            retryTimer?.Dispose();
            _logger.LogInformation("[联机] 路线验证同步完成，结果: {Result}", result ? "全员完成" : "超时放行");
        }
        finally
        {
            _client.RouteVerificationAllDone -= handler;
        }
    }

    /// <summary>降级为单机模式。</summary>
    public void Degrade(string reason)
    {
        IsActive = false;
        _logger.LogWarning("MultiplayerCoordinator 已降级，原因：{Reason}", reason);
        OnDegraded?.Invoke(reason);
    }

    public async ValueTask DisposeAsync()
    {
        _client.PlayerListUpdated -= OnPlayerListUpdated;
        await _client.DisposeAsync();
    }
}
