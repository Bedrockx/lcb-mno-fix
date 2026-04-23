#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

public class SyncBarrier
{
    private readonly ILogger<SyncBarrier> _logger = App.GetLogger<SyncBarrier>();
    private readonly CoordinatorClient _client;
    private readonly TimeSpan _timeout;

    public SyncBarrier(CoordinatorClient client, int timeoutSeconds = 60)
    {
        _client = client;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public async Task<bool> WaitAsync(string syncPointId, CancellationToken ct)
    {
        _logger.LogInformation("[SyncBarrier] 开始等待集合点: {SyncId}，超时={Timeout}s", syncPointId, _timeout.TotalSeconds);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        Action<string>? handler = null;
        handler = (arrivedSyncPointId) =>
        {
            _logger.LogInformation("[SyncBarrier] 收到 AllArrived 广播: {Arrived}，等待的: {SyncId}", arrivedSyncPointId, syncPointId);
            if (arrivedSyncPointId == syncPointId)
                tcs.TrySetResult(true);
        };

        _client.AllArrived += handler;
        try
        {
            _logger.LogInformation("[SyncBarrier] 上报到达集合点: {SyncId}", syncPointId);
            await _client.ReportArrivalAsync(syncPointId);
            _logger.LogInformation("[SyncBarrier] 上报完成，等待其他玩家...");

            using var reg = linkedCts.Token.Register(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("[SyncBarrier] 外部取消，集合点: {SyncId}", syncPointId);
                    tcs.TrySetCanceled(ct);
                }
                else
                {
                    _logger.LogWarning("[SyncBarrier] 等待超时({Timeout}s)，自动放行，集合点: {SyncId}", _timeout.TotalSeconds, syncPointId);
                    tcs.TrySetResult(false);
                }
            });

            var result = await tcs.Task;
            _logger.LogInformation("[SyncBarrier] 集合点 {SyncId} 完成，结果: {Result}（true=正常同步，false=超时放行）", syncPointId, result);
            return result;
        }
        finally
        {
            _client.AllArrived -= handler;
        }
    }
}
