using BetterGenshinImpact.Core.Config;
using System;
using System.Collections.Generic;
using System.Text;
using BetterGenshinImpact.Core.Simulator;
using System.Threading;
using Fischless.WindowsInput;

namespace BetterGenshinImpact.Core.Simulator.Extensions;

/// <summary>
/// 用于扩展<seealso cref="Fischless.WindowsInput.InputSimulator"/>的功能
/// </summary>
public static class InputSimulatorExtension
{

    /// <summary>
    /// 模拟玩家操作
    /// </summary>
    /// <param name="action">动作</param>
    /// <param name="type">按键类型</param>
    public static void SimulateAction(this InputSimulator self, GIActions action, KeyType type = KeyType.KeyPress)
    {
        // [TEST] NormalAttack 使用安全点击序列：Up → Down → Up，间隔可控
        // 用于排查 LeftButtonClick() 单次 Down+Up 可能被 ReleaseAllKey / 系统输入队列干扰的问题
        if (action == GIActions.NormalAttack && type == KeyType.KeyPress)
        {
            SafeMouseLeftClick(self);
            return;
        }

        var key = action.ToActionKey();
        switch (type)
        {
            case KeyType.KeyPress:
                KeyPress(self, key);
                break;
            case KeyType.KeyDown:
                KeyDown(self, key);
                break;
            case KeyType.KeyUp:
                KeyUp(self, key);
                break;
            case KeyType.Hold:
                HoldKeyPress(self, key);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// [TEST] 安全鼠标左键点击：Up → 间隔 → Down → 间隔 → Up
    /// 避免单次 LeftButtonClick() 的 Down+Up 过于紧凑的问题。
    /// 间隔值可根据测试结果调整。
    /// </summary>
    private static void SafeMouseLeftClick(InputSimulator self)
    {
        // ① 防御性释放：消除任何残留的按下状态
        self.Mouse.LeftButtonUp();
        Thread.Sleep(30);

        // ② 按下
        self.Mouse.LeftButtonDown();
        Thread.Sleep(50);

        // ③ 释放
        self.Mouse.LeftButtonUp();
    }

    private static void HoldKeyPress(InputSimulator self, KeyId key)
    {
        KeyDown(self, key);
        Thread.Sleep(1000);
        KeyUp(self, key);
    }

    private static void KeyPress(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonClick();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonClick();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonClick();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonClick(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonClick(0x0001);
                break;
            default:
                var k = key.ToVK();
                // 解决 shift 之类的键位没法正常使用的问题
                if (InputBuilder.IsExtendedKey(k))
                {
                    self.Keyboard.KeyPress(false, k);
                }
                else
                {
                    self.Keyboard.KeyPress(k);
                }
                break;
        }
    }

    private static void KeyDown(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonDown();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonDown();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonDown();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonDown(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonDown(0x0001);
                break;
            default:
                var k = key.ToVK();
                // 解决 shift 之类的键位没法正常使用的问题
                if (InputBuilder.IsExtendedKey(k))
                {
                    self.Keyboard.KeyDown(false, k);
                }
                else
                {
                    self.Keyboard.KeyDown(k);
                }
                break;
        }
    }

    private static void KeyUp(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonUp();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonUp();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonUp();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonUp(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonUp(0x0001);
                break;
            default:
                var k = key.ToVK();
                // 解决 shift 之类的键位没法正常使用的问题
                if (InputBuilder.IsExtendedKey(k))
                {
                    self.Keyboard.KeyUp(false, k);
                }
                else
                {
                    self.Keyboard.KeyUp(k);
                }
                break;
        }
    }

}
