using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 空闲动作执行器
/// 当任务结束后空闲超过设定的时间，按配置依次执行预设动作。
/// </summary>
public class IdleActionExecutor
{
    private static readonly ILogger<IdleActionExecutor> _logger = App.GetLogger<IdleActionExecutor>();
    private static volatile int _isRunning;

    /// <summary>
    /// 等待空闲时间后执行配置的动作
    /// </summary>
    public static async Task ExecuteIfIdleAsync(AllConfig config, CancellationToken ct)
    {
        var idleConfig = config.OtherConfig.IdleActionConfig;
        if (!idleConfig.Enabled)
        {
            return;
        }

        // 防重入：前一个空闲动作尚未结束时，不重复触发
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            try
            {
                await Task.Delay(idleConfig.TimeoutSeconds * 1000, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            // 计时期间有新的任务开始执行，跳过本次空闲动作
            // （有其他任务正在持有 TaskSemaphore，说明系统未真正空闲）
            if (Common.TaskControl.TaskSemaphore.CurrentCount == 0)
            {
                return;
            }

            _logger.LogInformation("空闲超时({Time}秒)，开始执行空闲动作", idleConfig.TimeoutSeconds);

            // 按序依次执行勾选的动作
            if (idleConfig.TeleportToStatue)
            {
                await TeleportToStatue(ct);
            }

            if (idleConfig.CloseGenshin)
            {
                CloseGenshinImpact();
            }

            if (idleConfig.CloseBgi)
            {
                CloseBgi();
            }

            if (idleConfig.Shutdown)
            {
                ShutdownComputer();
            }

            if (idleConfig.OneDragonEnabled && !string.IsNullOrEmpty(idleConfig.OneDragonConfigName))
            {
                try
                {
                    _logger.LogInformation("空闲动作：启动一条龙配置 {Name}", idleConfig.OneDragonConfigName);

                    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Application.Current.Dispatcher.Invoke(async () =>
                    {
                        try
                        {
                            var odVm = App.GetService<OneDragonFlowViewModel>();
                            if (odVm != null)
                            {
                                odVm.InitConfigList();
                                var odConfig = odVm.ConfigList.FirstOrDefault(c => c.Name == idleConfig.OneDragonConfigName);
                                if (odConfig != null)
                                {
                                    odVm.SelectedConfig = odConfig;
                                    TaskContext.Instance().Config.SelectedOneDragonFlowConfigName = odConfig.Name;
                                    await odVm.OnOneKeyExecute();
                                }
                                else
                                {
                                    _logger.LogWarning("空闲动作：未找到一条龙配置 {Name}", idleConfig.OneDragonConfigName);
                                }
                            }
                            tcs.SetResult();
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });
                    await tcs.Task;
                }
                catch (Exception e)
                {
                    _logger.LogWarning("空闲动作：启动一条龙失败：{Msg}", e.Message);
                }
            }

            if (idleConfig.ConfigGroupEnabled && !string.IsNullOrEmpty(idleConfig.ConfigGroupName))
            {
                try
                {
                    _logger.LogInformation("空闲动作：启动配置组 {Name}", idleConfig.ConfigGroupName);

                    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Application.Current.Dispatcher.Invoke(async () =>
                    {
                        try
                        {
                            var groupPath = Global.Absolute(@"User\ScriptGroup");
                            if (Directory.Exists(groupPath))
                            {
                                var group = Directory.GetFiles(groupPath, "*.json")
                                    .Select(f => ScriptGroup.FromJson(File.ReadAllText(f)))
                                    .FirstOrDefault(g => g.Name == idleConfig.ConfigGroupName);

                                if (group != null)
                                {
                                    var projects = ScriptControlViewModel.GetNextProjects(group);
                                    if (projects.Count > 0)
                                    {
                                        var scriptService = App.GetService<IScriptService>();
                                        if (scriptService != null)
                                        {
                                            await scriptService.RunMulti(projects, group.Name);
                                        }
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("空闲动作：未找到配置组 {Name}", idleConfig.ConfigGroupName);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("空闲动作：配置组目录不存在 {Path}", groupPath);
                            }
                            tcs.SetResult();
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });
                    await tcs.Task;
                }
                catch (Exception e)
                {
                    _logger.LogWarning("空闲动作：启动配置组失败：{Msg}", e.Message);
                }
            }
        }
        finally
        {
            _isRunning = 0;
        }
    }

    private static async Task TeleportToStatue(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("空闲动作：传送至七天神像");
            var tpTask = new TpTask(ct);
            await tpTask.TpToStatueOfTheSeven(requireLoadingScreen: false);
            _logger.LogInformation("空闲动作：传送至七天神像完成");
        }
        catch (Exception e)
        {
            _logger.LogWarning("空闲动作：传送至七天神像失败：{Msg}", e.Message);
        }
    }

    private static void CloseGenshinImpact()
    {
        _logger.LogInformation("空闲动作：关闭原神");
        SystemControl.CloseGame();
    }

    private static void CloseBgi()
    {
        try
        {
            _logger.LogInformation("空闲动作：关闭BGI");
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
        catch (Exception e)
        {
            _logger.LogWarning("空闲动作：关闭BGI失败：{Msg}", e.Message);
        }
    }

    private static void ShutdownComputer()
    {
        try
        {
            _logger.LogInformation("空闲动作：关机");
            Process.Start("shutdown", "/s /t 30");
        }
        catch (Exception e)
        {
            _logger.LogWarning("空闲动作：关机失败：{Msg}", e.Message);
        }
    }
}
