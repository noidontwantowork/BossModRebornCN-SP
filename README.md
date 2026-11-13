# BossModReborn 合并版（国服本地化 + 斗兽奇弈）

上游项目：[FFXIV-CombatReborn/BossmodReborn](https://github.com/FFXIV-CombatReborn/BossmodReborn) —— 本版本在其 7.5.8.1 源码基线之上构建（并已合入上游 7.5.8.2 的 `HookAddress` 空地址保护修复），再合并国服本地化与斗兽奇弈支持。上游的一切功劳归 The Combat Reborn Team 及其贡献者。

基于 **7.5.8.1 国服本地化版**的功能基线，合入 **7.5.6.17 斗兽版**独有的「斗兽奇弈（Crucible of the Unbroken / Beastmaster）」副本支持，产出一个可直接被卫月（Dalamud）加载的插件。

## 产物

| 文件 | 说明 |
| --- | --- |
| `BossModReborn.dll` | 主插件（约 105 MB，内嵌 456 个资源：字体、着色器、寻路障碍图） |
| `BossModReborn.json` | 插件清单（`InternalName` = `BossModReborn`，`AssemblyVersion` = `7.5.8.2`） |
| `BossModReborn.pdb` | 调试符号（可选，便于异常栈定位） |
| `RebornMaterial.dll` | UI 材质依赖（RebornMaterial 7.5.8.1） |
| `DefaultRotationPresets.json` | 自动循环默认预设 |
| `RebornPresets.json` | 附加预设 |

安装：卫月 → 设置 → 插件 → 第三方插件，填入仓库链接 `https://raw.githubusercontent.com/noidontwantowork/BossModRebornCN-SP/main/pluginmaster.json` ，即可在插件列表看到「BossMod Reborn」。

## 合并内容

### 1. 斗兽奇弈模块（来自 7.5.6.17 斗兽版）

- `BossMod\Modules\Dawntrail\Foray\Crucible\`：38 个文件，XBMB01–XBMB45 共 45 个副本模块及全部机制组件（CrucibleArena / CrucibleSpells / CrucibleOIDResolver / CrucibleEnums / High・Low 分层机制 / Lauda・Sphinx・Eye・Dragon・Gargoyle・Golem・Morbol・Owl・Taurus・Medusa・Manticore・Minotaur・Gatler・Siren・Durga・Atomos・Mindflayer・Bomb・Zu・Ymir・Catoblepas・Lahamu・Chimera・Jelly 等）。
- `BossMod\Modules\Dawntrail\Foray\CriticalEngagement\ReplayValidatedCastAOEs.cs`：斗兽战斗专用的加速/回放去重 AOE 基类（`ReplayValidatedCastAOEs` / `ReplayValidatedOppositeAOEs`），Crucible 组件全部继承自它。

### 2. 为支撑上述模块补齐的运行时 API

7.5.6.17 斗兽版在 `AIHints` 与 `ArenaBounds` 上做了扩展，7.5.8.1 主线没有。已按最小改动移植：

- `BossMod\BossModule\AIHints.cs`：`PreserveTarget`、`ClearTargetID`、`DesiredFacing` / `DesiredFacingExpire`、`ForcedMarchImminent`、`LockedFacing(DateTime)`，并把 `ForcedTarget` 改为受 `PreserveTarget` 守卫的属性。
- `BossMod\BossModule\ArenaBounds.cs`：`ArenaBoundsCustom(..., WPos? CenterOverride = null)`，用于把自定义多边形竞技场的中心钉在指定坐标（斗兽棋盘类场地）。
- 对应消费点：`Framework\ActionManagerEx.cs`（锁定朝向/期望朝向/动作临近判断）、`AI\AIBehaviour.cs`（强制行进时停止移动、保留目标）、`Autorotation\MiscAI\AutoTarget.cs`（保留目标）、`Framework\Plugin.cs`（按 `ClearTargetID` 清除目标）。

未移植 7.5.6.17 的 `MechanicTargetSelection` / `TargetSelectionOnce`：它们只被斗兽版改写过的 `Plugin.cs` 使用，Crucible 模块本身不引用，属死代码。

### 3. 国服中文化

以 7.5.8.1 国服二进制为基准，把 1 262 条中文界面/配置/策略字面量回灌进源码（`LocalizeTool` 基于 Roslyn 语法树替换，只动字符串字面量，不碰标识符）。共 158 个文件、约 1 450 处。

刻意**不翻译/已回滚**的部分：

- `BossMod.SourceGen\`（编译期生成器，其字符串参与模块注册键匹配）。
- `Config\ConfigConverter.cs`、`Autorotation\PlanPresetConverter.cs`（历史配置迁移用 JSON 键名，必须保持英文）。
- `Config\ConfigUI.cs` / `RebornUI\Settings\SettingsWindow.Commands.cs` 的帮助文本里的命令名 `on` / `off`（解析器只认 `ON` / `OFF`）。
- `GenerateNodeName` 里的 `"Config"` 后缀（用于剥掉类型名后缀，必须匹配英文类型名）。
- 斗兽模块自带的 17 条英文 CastHints（与国服版对同类提示的处理一致）。

#### 3.1 与国服二进制逐条对齐（副本名 / Boss 名 / 术语）

首轮本地化是「英 → 中」批量映射，会把国服**仍保留英文**的专有名词、枚举描述、策略选项名翻成自造中文，同一英文在不同位置的译法也会不一致。已按 `Type::Method` 级的 LDSTR 对齐（`_work\compare-strings*.py` + CN 二进制字符串表 `_work\str2-cn.txt`）逐条回滚或改写：

- 副本名 / Boss 名：全部来自 Lumina Excel（`ModuleViewer.BuildModuleInfo` → `ContentFinderCondition` / `BNpcName` / `Fate` / `DynamicEvent`），源码层不再有任何硬编码名称表；`LuminaSheet<T>()` 改为 `Language.ChineseSimplified` 后直接读国服译名。
- 策略与术语：`Targeting` 系列 `[Option]` 描述、`TargetingRenderer` 四个单选项、`OffensiveStrategyRenderer` 的「自动 / 禁用 / 强制」、各职业 `Define(SharedTrack.Targeting).As<T>("Targeting", ...)` 的 track 名（曾被误译成「目标选择」）、`[Option("Disabled")]`。
- 命令帮助表（`ConfigUI.cs` / `SettingsWindow.Commands.cs`）：与国服逐条同文。
- 模块界面（`ModuleViewer` / `SupportedFights`）：模块开关提示、筛选提示、成熟度标签、空状态文案。
- 其它：回放 `All` / `Current plan was not modified`、深宫 `Obstacle map missing` / `Wrong resolution`、`MaterialUI` 状态、目录错误提示。

对齐结果（`_work\str-diff*.tsv`，按 `Type::Method` 全量比对 6 913 个共有方法的 LDSTR）：

| 类别 | 首轮 | 最终 |
| --- | --- | --- |
| `ZHoverEN`（国服是英文、我们翻了） | 142 | **0** |
| `ZHdiffZH`（两边都中文但译法不同） | 55 | **6** |
| `ENleftZH`（国服是中文、我们漏翻） | 10 | **0** |

剩余 6 条全部是有意保留或 difflib 错配：`ConfigUI::.cctor` 的 3 条是合并版新增的「近战 / 远程最大距离」帮助文本（国服对应位置是别的条目，属错配）；`UIPresetDatabaseEditor` 的预设说明里国服二进制带字面反斜杠转义（实际 UI 会多显示 `\`），保留裸引号；`AboutTab::Draw` 一条为错配。

另修一处国服版自身遗漏的分组名：`ModuleViewer.ClassifyGroup` 的 `GroupType.CastrumLacusLitore` 分支在国服二进制里仍是英文 `"Castrum Lacus Litore"`，本版改为游戏内官方译名「帝国湖岸堡攻城战」。

### 4. 国服版相对上游 7.5.8.1 的独有功能（按国服二进制逐项复刻）

国服版与上游同源但并非同一快照，以下功能只存在于国服版（及 7.5.6.17 斗兽版），上游 7.5.8.1 缺失，已全部补齐：

| 功能 | 位置 | 说明 |
| --- | --- | --- |
| 近战/远程分开的最大距离 | `AI\AIConfig.cs`、`AI\AIBehaviour.cs`、`AI\AIManagementWindow.cs`、`AI\AIManager.cs` | 上游只有单个 `MaxDistanceToTarget = 2.6`；国服拆成 `MaxDistanceToTargetMelee = 1`（坦克/近战）与 `MaxDistanceToTargetRanged = 15`（远程/治疗），并新增 `GetMaxDistanceToTarget(Role)`；命令 `/bmr ai maxdistancetargetmelee X`、`maxdistancetargetranged X` |
| AEAssist 联动 | `Framework\AEAssist.cs`、`BossModule\AIHintsBuilder.cs`、`Framework\Plugin.cs` | 读取 AEAssist 的期望身位（`AEAssist.GetDesiredPositional`）写入 `RSRDesiredPositional`（RSR 优先），并在危险 AoE/Pyretic 前调用 `AEAssist.TriggerSpecialStateWithDuration(NoCasting, ...)` 停止读条 |
| AoE 对外广播 IPC | `Framework\AOEIPC.cs`、`BossModule\MiniArena.cs`、`BossModule\BossModule.cs`、`BossComponent.cs`、`Components\GenericAOEs.cs`、`Components\StackSpread.cs`、`Framework\IPCProvider.cs` | `MiniArena` 绘制时记录所有图形（`DrawnZones`，按组件 `SetBatch` 分批），`AOE.Active` IPC 汇总场地图形 + 分摊/分散 + 狙杀 + 共享坦克buster + 击退，输出 JSON（`AOEIPCDto`/`AOEIPCShapeType`）给外部插件 |
| 深宫：队伍中自动使用万化药 | `Modules\Global\DeepDungeon\Config.cs`、`AutoClear.cs` | 新增 `AutomaticPomanderInParty` 选项；同时通过 `PalacePal.Ipc` 拉取本层陷阱坐标刷新提示 |
| 异闻零式拉拉的平面炸弹反向解法 | `Modules\Endwalker\Criterion\C03AAI\C032Lala\C032LalaConfig.cs`、`PlanarTactics.cs` | 新增 `PlanarTacticsReverse` 配置项，切换安全区取角的旋转方向 |
| 回放记忆 | `Replay\ReplayMemory.cs`、`Replay\ReplayManagementConfig.cs` | 新增 `RememberReplays` / `RememberReplayTimes` / `ReplayHistory` 配置项（国服版中为半成品：仅存在配置节点，无运行时消费点，此处如实保留） |
| Lumina 中文语言层 | `Framework\Service.cs` | `LuminaSheet<T>()` 由 `Language.English` 改为 `Language.ChineseSimplified`，否则国服客户端取不到副本名/技能名 |
| 最低成熟度默认值 | `BossModule\BossModuleConfig.cs` | `MinMaturity` 默认值由 `Contributed` 改为 `WIP`，与国服版一致（否则大量开发中模块不会自动加载） |

### 5. 斗兽版里「斗兽之外」的内容（博兹雅等区域的增强）

斗兽版自身 `manifest.json` 的 `Changelog` 写明：`完善博兹雅南方战线 CE 与遭遇战的机制绘制和 AI 避让，新增 2 个 CE 与 2 个遭遇战模块（感谢 duyaxxx）`。这部分与斗兽无关，但确实只存在于斗兽版，已逐项移植：

**新增 4 个模块**（`Modules\Shadowbringers\Foray\`）

| 文件 | 类型 | GroupType / GroupID / NameID | 内容 |
| --- | --- | --- | --- |
| `CriticalEngagement\CE43OnSerpentsWings.cs` | CE | `CriticalEngagement` / 778 / 17 | 以 `OID.ElectricFence` 为主目标的机制绘制与 AI 避让 |
| `CriticalEngagement\CE61LeanMeanMagitekMachines.cs` | CE | `CriticalEngagement` / 778 / 27 | 以 `OID.MagitekInfantry` 为主目标 |
| `Skirmish\ImAMechanicalMan.cs` | 遭遇战 | `ForayFATE` / 735 / 1620 | 南方战线遭遇战 |
| `Skirmish\OfSteelAndFlame.cs` | 遭遇战 | `ForayFATE` / 735 / 1626 | 含子模块 `OfSteelAndFlameSartauvoir`（`OID.Sartauvoir`） |

移植前已核验这 4 个 `(GroupType, GroupID, NameID)` 在 7.5.8.1 中均未被占用，且模块只依赖主线 API（`Components.BaitAwayCast` / `GenericAOEs` / `SimpleAOEs` / `SimpleKnockbacks` / `GenericRotatingAOE` / `StackWithCastTargets` / `StatusDrivenForcedMarch` / `ProximityAOEs`）。

**10 个 CE 的机制与 AI 增强**（用斗兽版实现覆盖主线的简化版）

`CE11ShadowOfDeathHand` / `CE13KillItWithFire` / `CE21FinalFurlong` / `CE22TheHuntForRedChoctober` / `CE23FiresOfWar` / `CE32RiseOfTheRobots` / `CE42FromBeyondTheGrave` / `CE53HereComesTheCavalry` / `CE54NeverCryWolf` / `CE64FeelingTheBurn`。

以 `CE42FromBeyondTheGrave` 为例，斗兽版比主线多出：`ElectricFence` 的保留外边界甜甜圈与致命地板绘制、完整的 `GallowsMarch` 行进 AI（6 m/s、5° 步进搜索 72 次、`SDInvertedCircle`/`SDCapsule` 禁区、`DesiredFacing`/`ForcedMarchImminent` 驱动）、带分散评分的 `Aethertide`、`PurifyingLight` 的事件清理，以及冲刺技能的临时屏蔽与恢复。

**4 个黎明之途 FATE 增强**

`Modules\Dawntrail\Foray\FATE\` 下的 `InconstantGardener`、`Thunderregnum`、`GaleForceEncounter`、`AllureOfTheOccult`。

**开放世界 FATE 动态场地**（`BossModule\SimpleBossModule.cs`）

`OpenWorldFate` 改为按 `ClientState.ActiveFate` 实时追踪 FATE 中心与半径，`Arena` 跟随 FATE 移动；并根据障碍图 `PixelSize` 自动选择 `ArenaBoundsCircle` 分辨率（>60 m 取 2、>30 m 取 1、否则 0.5），避免大地图 FATE 的场地判定失真。

**明确不移植的斗兽版差异**

| 差异 | 理由 |
| --- | --- |
| 15 个黎明之途 CE 的斗兽版实现（CE201FamiliarTactics 等新命名） | 与主线是同一批 CE 的两种实现，`NameID` 49–63 与 OID 一一对应；主线为 `Verified`/上游作者版本，斗兽版为 `Contributed` 自测版，保留主线 |
| 4 个黎明之途 FATE（CrescentFan / CrescentGremlin / NH110 / NH111） | 其 `NameID` 是复制模板未改（与主线已有 FATE 冲突，NH111 与主线 `ShorelineShowdown` 同 OID 同 NameID） |
| `ForkedTowerMagic` 的 FTMN4Index / FTMN2SwordDancer | 斗兽版是带「继续测试」注释的自测版，主线为 `Verified` 正式版且体量更大 |
| `Pathfinding\NavigationDecision.cs` 的 `IncrementalBuilder` + `MinSafeGoalFraction` | 属寻路引擎改动，需同步修改 `AIBehaviour` / `NormalMovement` 的调用方式，风险高于收益，暂不并入 |
| `Util\ShapeDistance\KnockbackInAABBSquare.cs` 新增类、`ManualActionQueueTweak` 的 `Clear()`/`IsRSREnabled()` | 全库无调用点，纯死代码 |
| `Debug\DebugCollision.cs` 的按楼层导出碰撞 | 调试工具，不影响游戏内行为 |

### 6. 与上游 7.5.8.2 的同步

上游 7.5.8.2 只包含一个修复（[PR #1395](https://github.com/FFXIV-CombatReborn/BossmodReborn/pull/1395)「Fix for null hooking」）：`BossMod\Util\Hook.cs` 的 `HookAddress(nint address, T detour, bool autoEnable)` 构造函数在解析出的地址为 `0` 时直接抛 `InvalidOperationException`，而不是继续 `HookFromAddress(0, ...)` 造成难以定位的静默失效。本版已同步（异常文本 `Cannot hook {typeof(T).Name}: resolved address is null. CS may need updating.`），签名扫描失败时会以明确异常暴露。

## 版本号说明

插件版本为 **7.5.8.2**（在国服原版 7.5.8.1 基础上递增修订号，用于与本合并构建区分），`BossModReborn.json` 的 `AssemblyVersion` 与 DLL 的 `AssemblyVersion` / `FileVersion` / `ProductVersion` 全部为 `7.5.8.2`。

卫月的 `PluginManifest.AssemblyVersion` 是 `System.Version` 类型，最多 4 段，因此不能使用 `7.5.8.1.1` 这类五段版本号（清单反序列化阶段会抛异常导致插件无法加载；MSBuild 的 `<Version>` 同样只接受 4 段，写成 5 段会让 NuGet 还原静默失败）。

## 斗兽模块在「支持的战斗」里的归类

初版合并时 XBMB01–45 的 `ModuleInfo` 写的是 `Category = Foray`、`GroupType = None`，因此全部落进「未分组」。现已改为与棋盘子模块一致的归类：

- `Category = BossModuleInfo.Category.CrucibleOfTheUnbroken` → 栏目名「驯兽师」（`ModuleViewer.EnsureInitialized` 中 `Customize(Category.CrucibleOfTheUnbroken, contentType.GetRow(40u), "驯兽师")`）。
- `GroupType = BossModuleInfo.GroupType.CrucibleOfTheUnbroken` → `GroupKey`/`ClassifyGroup` 走 `ContentFinderCondition` 分支，组名取 CFC 行名并去掉 ` Of the Unbroken` 后缀。
- `GroupID` 按 `XBMBattles.cs` 中每组上方的注释（`// 第 N 组（斗兽奇弈 [高段] 第X盘）`）分配：第一盘 1088、第二盘 1089、第三盘 1090、斗兽奇弈高段第一盘 1091、高段第二盘 1092 —— 与 `Modules\Global\CrucibleOfTheUnbroken\01FirstBoard`…`05SecondMasterBoard` 使用的 CFC 行完全一致。

结果：45 个斗兽模块按盘分成 5 组，全部显示在「驯兽师」栏目下。

### 与棋盘子模块合并成同一批分组

棋盘子模块（`Modules\Global\CrucibleOfTheUnbroken\` 下 41 个模块）原本不写 `Expansion`，由 SourceGen 按命名空间第二层推断成 `Expansion.Global`（值 6）。而 `ModuleViewer` 的分组键是 `(expansion, category, groupId)`，于是同样 5 个 CFC 行被拆成两批共 10 个分组；又因为 `EnsureInitialized` 只对 `RealmReborn…Dawntrail` 调用过 `Customize(Expansion…)`，`Expansions[6].icon == 0`，这批分组的行首扩展图标显示为「？」。

修复：给全部 41 个棋盘子模块显式补 `Expansion = BossModuleInfo.Expansion.Dawntrail`（与 XBMB 一致），分组键合流为 `(Dawntrail, CrucibleOfTheUnbroken, 1088…1092)`。

验证（读 SourceGen 生成的 `GeneratedRegistries.BossModules.g.cs`，统计 `GroupType == 18` 的 Info 构造）：

| Expansion | Category | GroupID | 模块数 |
| --- | --- | --- | --- |
| 5 (Dawntrail) | 22 (CrucibleOfTheUnbroken) | 1088 | 12 |
| 5 | 22 | 1089 | 13 |
| 5 | 22 | 1090 | 16 |
| 5 | 22 | 1091 | 20 |
| 5 | 22 | 1092 | 25 |

即 **5 个分组键**（此前为 10 个），合计 86 个模块（45 个 XBMB + 41 个棋子）。运行时按 `PrimaryActorOID` 先注册者胜，被 XBMB 遮蔽的棋子不会出现在列表里，每组显示 XBMB 模块 + 该盘未被覆盖的棋子模块。分组图标取 `Expansions[Dawntrail].icon`（61880）与 `Categories[CrucibleOfTheUnbroken].icon`（`ContentType` 行 40），不再出现「？」。

## 已知行为

- XBMB01–45 与 `Global\CrucibleOfTheUnbroken`（棋子棋盘）共用 `PrimaryActorOID`。`BossModuleRegistry` 采用先注册者胜，生成注册表的顺序为 `Dawntrail` 先于 `Global`，**运行时由 XBMB 模块接管**，与 7.5.6.17 斗兽版行为一致。编译期会因此产生 32 对 `BMSG005` 警告，属预期。
- `RebornMaterial.dll` 与上游 7.5.8.1 NuGet 包一致（363 520 B），与国服版随附文件同源。

## 复现构建

```powershell
$env:HTTP_PROXY='http://127.0.0.1:10853'; $env:HTTPS_PROXY='http://127.0.0.1:10853'
dotnet build "BossModReborn\BossMod\BossModReborn.csproj" -c Release -p:Platform=x64 `
  -p:DalamudLibPath="$env:APPDATA\XIVLauncherCN\addon\Hooks\dev\"
```

- SDK：.NET 10（`net10.0-windows10.0.26100.0`），Dalamud API Level 15。
- `DalamudLibPath` 必须指向国服运行时目录 `%APPDATA%\XIVLauncherCN\addon\Hooks\dev\`。
- 输出目录 `BossModReborn\BossMod\bin\x64\Release\`，即上表 6 个文件。
