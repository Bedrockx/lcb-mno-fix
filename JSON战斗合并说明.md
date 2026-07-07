# B69→master 修改移植到 B74 合并说明

**目标**: 将 B69→master 的 JSON 战斗策略相关修改合并到 `upstream/main-OldTeaBag-B74` 分支

**原则**: 只处理 AutoFight 目录下的 JSON 战斗策略相关文件，其余一概不动

---

## 文件清单

### A. 纯新增文件（11 个，直接复制到 B74）

```
BetterGenshinImpact/GameTask/AutoFight/
├── AutoFightJsonTask.cs
├── AutoFightEndDetection.cs
├── TaskFightFinishDetectConfig.cs
├── Factory/
│   ├── ICombatTaskFactory.cs
│   ├── JsonCombatTaskFactory.cs
│   ├── TxtCombatTaskFactory.cs
│   └── CombatTaskFactoryProvider.cs
└── Script/
    ├── JsonCombatStrategy.cs
    ├── JsonCombatStrategyParser.cs
    ├── ConditionEvaluator.cs
    └── ESkillCdTracker.cs
```

### B. 需要合并的修改文件（8 个）

1. `Avatar.cs`
2. `AutoFightTask.cs`
3. `CombatCommand.cs`
4. `CombatScriptParser.cs`
5. `AutoFightParam.cs`
6. `CombatScenes.cs`
7. `AutoFightAssets.cs`
8. `combat_avatar.json`

---

## 处理方式说明

| 类型 | 处理方式 |
|:----|:--------|
| **A. 纯新增文件** | B69/B74 均无此文件，直接从 master 复制到 B74 即可，无冲突 |
| **B. 修改文件** | B69→master 和 B69→B74 都有改动，需要逐段手动合并 |

---

## A组：纯新增文件（直接复制）

以下 11 个文件在 B69 和 B74 中均不存在，直接从 master 复制到目标分支即可：

```
cp BetterGenshinImpact/GameTask/AutoFight/AutoFightJsonTask.cs          <target>/
cp BetterGenshinImpact/GameTask/AutoFight/AutoFightEndDetection.cs      <target>/
cp BetterGenshinImpact/GameTask/AutoFight/TaskFightFinishDetectConfig.cs <target>/
cp BetterGenshinImpact/GameTask/AutoFight/Factory/*.cs                  <target>/Factory/
cp BetterGenshinImpact/GameTask/AutoFight/Script/JsonCombatStrategy.cs  <target>/Script/
cp BetterGenshinImpact/GameTask/AutoFight/Script/JsonCombatStrategyParser.cs <target>/Script/
cp BetterGenshinImpact/GameTask/AutoFight/Script/ConditionEvaluator.cs  <target>/Script/
cp BetterGenshinImpact/GameTask/AutoFight/Script/ESkillCdTracker.cs     <target>/Script/
```

## B组：修改文件合并策略

### B1. Avatar.cs

#### ① 复活弹窗分支（L179-200）
**简单处理**: `!AutoEatConfig.Enabled` 作为总开关，不开自动吃药时不阻塞正常逻辑，其余照搬 B74（`AutoEatRecoveryDecisions` + `RecoverSuppressed`）

#### ② 阿蕾奇诺 Q 释放判断（L850-860）
**照搬 B74**: `ArlecchinoAutoEqDecisions.ShouldReleaseQByCd(cc, SkillCdForQ)` + `IsReady()` + `redTime`

#### ③ 契阔检测区域（L964-990）
**照搬 B74**: `QiX=840`, `QiW=220`, `IsQi()` 实例方法, `QiKong` 偏移, `IsReady()` 静态方法

#### ④ KeyUp/KeyPress E 技能检测（L1755-1880）
**照搬 master**: 删除内联 OCR 循环，替换为 `ESkillCdTracker.Record()` + `ApplyFallback()`

#### ⑤ UseSkill 中的阿蕾奇诺逻辑
**照搬 B74**: `_arlecchino` 字段 + 空E释放序列

---

### B2. AutoFightTask.cs

#### master 的改动
| 位置 | 改动 |
|:----|------|
| L467 | `ApplyStabilityBuffer` → `internal static`（供 `AutoFightJsonTask` 调用） |
| L2234-2275 | 复活弹窗按 `!AutoEatConfig.Enabled` 分叉：关闭吃药时直接取消弹窗→`return true` |

#### B74 的改动
| 位置 | 改动 |
|:----|------|
| L707 | 新增 `avatarToInit.QiKong = _taskParam.QiKong` |
| L2034 | 新增 `PathingConditionConfig.CombatScenesGoBackUp = combatScenes` |
| L3153 | 万叶回点新增 `isScreenStable` 画面稳定门控 |

#### 冲突判断：✅ 无冲突
两个分支改动区域完全独立，**全部照搬，同时采用**。

---

### B3. CombatCommand.cs

#### master 的改动
| 位置 | 改动 |
|:----|------|
| L1-11 | 新增 3 个 `using`：`AutoFight.Script`、`System.Threading`、`Vanara.PInvoke` |
| L296-300 | `KeyUp`/`KeyPress` 末尾各新增 `TryTriggerESkillCdCheck(avatar, Args![0])` |
| L316-358 | 新增 `TryTriggerESkillCdCheck` 方法：E 键时触发 OCR 检测 E 技能 CD |

#### B74 的改动：**无**

#### 冲突判断：✅ 无冲突
**直接照搬 master**。

---

### B4. CombatScriptParser.cs

#### master 的改动
| 位置 | 改动 |
|:----|------|
| L19-24 | 新增 `.json` 路径检测→报错（防止旧解析器误解析 JSON 文件） |

#### B74 的改动：**无**

#### 冲突判断：✅ 无冲突
**直接照搬 master**。

---

### B5. AutoFightParam.cs

#### master 的改动
| 位置 | 改动 |
|:----|------|
| L106-125 | 新增 `ResolveStrategyPath()` 静态方法（`.txt`→`.json` 双格式支持） |
| L195-218 | `SetCombatStrategyPath()` 改用 `ResolveStrategyPath()` |

#### B74 的改动
| 位置 | 改动 |
|:----|------|
| L50 | 新增 `QiKong = autoFightConfig.QiKong` |
| L129 | 新增 `public int QiKong` 属性 |
| L219 | 新增 `QiKong = autoFightConfig.QiKong`（`SetDefault` 中）|

#### 冲突判断：✅ 无冲突
两个分支改的是不同区域，**合并采用**。

---

### B6. CombatScenes.cs

#### master 的改动：**无**

#### B74 的改动：**无**

#### 冲突判断：✅ 无冲突
**保持不变**。

---

### B7. AutoFightAssets.cs

#### master 的改动：**无**

#### B74 的改动：**无**

#### 冲突判断：✅ 无冲突
**保持不变**。

---

### B8. combat_avatar.json

#### master 的改动
末尾新增 3 个角色：桑多涅、阿罗夏、奥黛特

#### B74 的改动
末尾新增同 3 个角色（内容一致）+ 奇偶男/女别名调整 + 茜特拉莉删除"老伴"别名

#### 冲突判断：✅ 无冲突
新增内容相同，别名改动独立。

---

## ⚠️ 已发现的 Bug 修复

### AutoFightViewModel.LoadCustomScript 子文件夹路径丢失

**文件**: `BetterGenshinImpact/ViewModel/Pages/View/AutoFightViewModel.cs`

**原因**: `Path.GetFileNameWithoutExtension` 只取文件名，丢弃了子文件夹路径。例如 `SubFolder\strategy.txt` 只得到 `strategy`（下拉列表显示和策略名都丢失了路径信息，导致 `ResolveStrategyPath` 找不到文件）。

**修复**: 改为 `Path.ChangeExtension(relativePath, null)`，保留子路径。

```diff
- var strategyName = Path.GetFileNameWithoutExtension(relativePath);
+ var strategyName = Path.ChangeExtension(relativePath, null);
```

**影响**: 修复后下拉列表显示 `SubFolder\strategy`，`ResolveStrategyPath("SubFolder\strategy")` 能正确拼出完整路径。

**状态**: ✅ 已在两个本地仓库修复
