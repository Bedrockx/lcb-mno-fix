using BgiCoordinatorServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BgiCoordinatorServer.Services;

public class HeartbeatMonitor : IHostedService, IDisposable
{
    private readonly RoomManager _roomManager;
    private readonly IHubContext<CoordinatorHub> _hubContext;
    private readonly ILogger<HeartbeatMonitor> _logger;
    private Timer? _timer;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PlayerTimeout = TimeSpan.FromSeconds(90);

    public HeartbeatMonitor(
        RoomManager roomManager,
        IHubContext<CoordinatorHub> hubContext,
        ILogger<HeartbeatMonitor> logger)
    {
        _roomManager = roomManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatMonitor 启动，扫描间隔 {Interval}s，超时阈值 {Timeout}s",
            ScanInterval.TotalSeconds, PlayerTimeout.TotalSeconds);
        _timer = new Timer(Scan, null, ScanInterval, ScanInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatMonitor 停止");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void Scan(object? state)
    {
        try
        {
            var affectedRooms = _roomManager.RemoveDeadPlayers(PlayerTimeout);
            foreach (var roomCode in affectedRooms)
            {
                var room = _roomManager.GetRoom(roomCode);
                var players = room?.Players ?? [];

                _logger.LogInformation("房间 {RoomCode} 有玩家超时断线，当前剩余 {Count} 人", roomCode, players.Count);

                _ = _hubContext.Clients.Group(roomCode)
                    .SendAsync("PlayerListUpdated", players);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HeartbeatMonitor 扫描时发生异常");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
