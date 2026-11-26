# AOE2 确定性锁步 RTS 框架

面向「千数量级单位寻路与战斗、局域网同步、3D 等距视角」的框架骨架。核心思路是把**模拟**与**表现**彻底分离：模拟层只跑确定性逻辑，表现层只读模拟状态做渲染。

## 模块

| 模块 | 目录 | 职责 |
| --- | --- | --- |
| C# 模拟内核 | `simulation_cs/` | 定点、随机、ECS、寻路/流场/移动、战斗、采集、对局胜负、锁步、`GameSim`，引擎无关 |
| 战争迷雾 | `simulation_cs/VisionGrid.cs`、`VisionSystem.cs` | 按玩家的可视性网格（未探索/已探索/可见），定点圆揭示，随快照下发 |
| 协议与网络 | `simulation_cs/Protocol.cs`、`DedicatedServer.cs` | 快照/指令帧编解码、TCP dedicated server |
| 服务器权威 | `SimulationServer` / `LocalSession` / `SimulationClient` | 权威模拟出快照，瘦客户端只消费快照 |
| 表现 | `presentation/` | 3D 等距相机、单位与地形渲染、经 `GameBridge` 消费快照 |

## 确定性约定

- 所有模拟计算只用整数与定点，禁止在模拟层出现 `float`。
- 实体与系统按实体 id 升序迭代；指令按玩家 id 升序、批内顺序派发。
- 组件与列名统一转 `String` 按内容排序后再遍历、按内容取哈希，不依赖 `StringName` 内部索引。
- 校验和覆盖实体、组件列、`tick`、`next_entity` 与 RNG 状态。
- 对角移动按 1/√2 归一化，斜向合速度与正交一致。
- 移动结算走动态占位层：按实体 id 升序预约下一格，占用则排队并按流场次优方向确定性让位，单位不穿透不重叠；建筑用 footprint 多格占位。
- 战斗按格子切比雪夫距离判定 `RANGE_MIN`/`RANGE_MAX`，近战限相邻格；远程由 `ProjectileSystem` 按弹速换算命中 tick，到达时才结算伤害。
- 同一初始状态 + 同一指令序列 = 同一校验和，这是锁步与 desync 检测的基础。

## 服务器权威 + 瘦客户端

游戏引擎已切到 Godot 的 .NET 版（`project.godot` 的 `[engine_host] dotnet="true"`），模拟内核在 `simulation_cs/` 用 C# 且**不依赖 Godot**，前后端彻底拆分：

- **服务器**：`SimulationServer` 持有权威 `GameSim`，按 30Hz 推进并产出 `SnapshotFrame`；`DedicatedServer` 把同一内核放进独立进程，经 TCP（`Protocol.cs` 的长度前缀帧）广播快照、接收 `CommandFrame`。每个连接按接入顺序占用一个空闲玩家位，满员后拒绝新连接，避免复用 team 冒充已有玩家。
- **连接身份与权限**：连接层把「连接 → 玩家」绑定好，指令入队时以该玩家覆写 `CommandFrame.Player` 与指令自报的 `Player`；`GameSim.IsCommandAllowed` 再校验：作用于实体的指令要求该实体属于该玩家，作用于玩家的指令（投降/时代/科技/放置建筑）要求玩家合法。越权、过期、非法指令直接丢弃并计入 `RejectedCommands`，不进入模拟。
- **锁步回合**：服务器固定每 `Lockstep.TicksPerTurn` 个 tick 为一次回合边界，回合开始时按玩家升序统一应用该回合收到的输入，再递增回合号；缺输入的玩家按空输入推进，服务器不会为等待输入而停顿。本地局与联机对局共用这套「不等输入」语义，`Lockstep` 只是一枚回合时钟（`AdvanceTurn`），不再有 `SubmitTurn`/`CanAdvance` 等会让人误以为要等待全员的接口。早于当前回合的帧被拒绝，当前回合已开始后收到的帧顺延到下一回合，允许提前提交至多 8 个回合。
- **客户端提前提交**：`RemoteClient.SubmitCommands` 以最近快照回合为基准加提前量填 `CommandFrame.Turn`（默认 4 回合，覆盖约 300ms 单向延迟并在 +8 窗口内），`InputTiming` 集中负责「延迟 → 提前量」换算；否则高 RTT 下帧到达时回合已过会被整帧丢弃。`ServerSelfTest` 用 300ms 单向延迟回归守住「不丢指令 + 本地/联机校验和一致」。
- **瘦客户端**：`SimulationClient` 只保存解码后的快照用于渲染，从不直接读服务器模拟对象；本地局用 `LocalSession` 内嵌本机服务器，快照同样走编码/解码。
- **表现层**：`presentation/` 通过 `GameBridge` 读快照渲染，不再引用任何模拟类。
- **确定性**：整数/定点、`String` 内容排序、FNV 校验和、实体升序迭代全部保留；`tests/determinism.gd` 用固化黄金校验和守住回归，`tests/kernel.gd` 覆盖各模块语义。

## 战争迷雾

- 每个玩家一份 `VisionGrid`（`_visible` 每 tick 重置、`_explored` 持久），`VisionSystem` 每 tick 以定点欧氏圆揭示单位/建筑视距内的格子并写回已探索。
- 视野随 `SnapshotFrame` 下发：快照按观察者玩家裁剪实体（己方恒见、中立与静态建筑按已探索、敌方单位仅当前可见），并附带整张可视性位图。
- 表现层 `WorldView` 依据位图渲染迷雾（未探索黑 / 已探索灰 / 可见透明）并隐藏未探索地形；`GameBridge` 只暴露解码后的客户端快照。`KernelSelfTest` 覆盖揭示、离开转已探索、双玩家独立、快照裁剪与确定性。

## 体素地形与单位

- 1 格 = 1 Godot 单位 = 8×8 体素（1 体素 = 1/8 单位），高度层 `1/8` 单位。地图编辑器笔刷、网格拾取、坐标换算一律以「格」为单位。
- `TerrainGrid`（`simulation_cs/TerrainGrid.cs`）**每格存 4 个顶点高度，相邻格共享边顶点**，整体是 `(宽 + 1) × (高 + 1)` 的顶点高度场；格内高度由四角派生。**悬崖由顶点高差形成**，这是刻意的玩法差异——原作是 2D 共面地形，本作靠高差制造悬崖。
- `TerrainGrid` 逐格存类型（草地/泥土/水/崖）与顶点高度层，**与 `NavGrid` 完全独立**：NavGrid 只描述建筑 footprint 与障碍。
- 寻路与建造按坡度判定：`FlowField` 与 `MovementSystem` 通过 `TerrainGrid.CanTraverse` 判定，水/崖类型、任一端陡坡（单格四角高差超阈值）或相邻格高差超阈值都不可通行；`BuildingSystem.CanPlace` 要求占地各格非陡坡且整体高差在阈值内，可在缓坡上建造。
- 地图格式由 `MapData`（`simulation_cs/MapData.cs`）组合 TerrainGrid 与 NavGrid；行优先扁平数组 `types` / `blocked`，`vertexHeights` 为顶点高度场，`heights` 为逐格最高角（兼容旧地图，缺 `vertexHeights` 时按逐格高度展开四角）。
- 表现层（`presentation/world_view.gd`）按共享顶点高度场生成连续地形顶面，`presentation/voxel_mesher.gd` 提供体素贪心合并与高度场网格两种生成；只用顶点色，单位保留全部六面（倒地时可看到底面）。
- 自建体素模型在 `assets/vox/`（`.vox`），由 `tools/make_vox_models.py` 生成；`presentation/vox_model.gd` 解析 RIFF 的 `SIZE/XYZI/RGBA`，调色板索引 1 作阵营色。
- 原作素材放在项目点开头的 `.reference/` 目录，**正常提交入库**（不加入 `.gitignore`），用于对照制作体素模型。

## 地图编辑器

独立场景 `tools/map_editor.tscn`（F6 直接运行），复用 `presentation/iso_camera.gd` 正交相机与地面平面拾格。

- 工具：抬升/降低/平滑顶点高度（按格同步四角、半径可调）、涂抹地形类型、标记悬崖、放置出生点/资源/建筑、标记/清除障碍。
- 编辑结果实时生成高度场地形预览与放置标记；差分层面的撤销/重做（含地形格、顶点高度与放置项），支持 `Ctrl+Z` / `Ctrl+Y` / `Ctrl+S`。
- 存读走 `simulation_cs/MapEditorBridge.cs`，与运行期共用同一 `MapData`：默认 `res://maps/editor_map.json`，格式含 `types` / `vertexHeights` / `blocked` / `placements`。
- 校验用 `TerrainGrid` + `NavGrid` + `FlowField`：出生点须彼此连通，资源点须能从出生点到达。

## 地图生成与起始资源

- `MapGenerator`（`simulation_cs/MapGenerator.cs`）提供 `IMapGenerator.Generate(seed, playerCount) -> MapData`，地图类型做脚本化枚举，先落地阿拉伯（Arabia），后续按同一接口扩展黑森林/岛屿。
- 生成器只用整数与 `SimRng`：同种子逐格一致；阿拉伯地图草地打底、四周悬崖封边，双方起始点沿地图中轴镜像，起始资源（木材、浆果/羊、金、石）同样镜像成簇。
- `MapData` 在原有的地形与阻塞之外携带资源点列表与玩家起始位置，JSON 存读与 `Checksum()` 一并覆盖这些布局数据。
- `MapSetup`（`simulation_cs/MapSetup.cs`）把地图落进模拟层：静态阻塞随 `NavGrid` 进入寻路，资源点生成资源实体，起始位置由建筑/经济系统摆放已完工城镇中心、投放点与初始村民。
- `MapSelfTest` 覆盖同种子一致、双方资源镜像、地形与阻塞一致、起点可建、资源点合法、JSON 往返与生成对局落地；`tests/tools/_map_preview.gd` 抓取全景与起始基地截图。

## 市场交易、商队与进贡

- 市场不再是空建筑：`MarketSystem`（`simulation_cs/MarketSystem.cs`）为每个玩家维护木/食/金/石的整数基准价，买入价与卖出价按固定价差展开；每次成交按数量推动基准价，并夹在上下限内，全部整数运算。
- 指令：`KindMarketBuy` / `KindMarketSell`（资源类型放 `Aux`、数量放 `CellX`）、`KindTribute`（目标玩家 `Aux`、资源 `CellX`、数量 `CellY`）。买卖要求该玩家至少有一座完工市场；进贡校验目标与库存后原子转移，可向队友或对手输送。
- 陆路商队：`SpawnTradeCart` 直接生成贸易车，`KindTradeRoute` 指定本玩家完工市场为起点、任意完工市场为终点；`TradeSystem` 复用邻接接近逻辑让车往返，抵达时按两市场曼哈顿距离与价差结算黄金。海上商队/贸易船随 AOE-60。
- 失败指令（无市场、越权、库存不足、非法目标）直接丢弃且不改变状态；市场基准价随快照下发，并全部纳入校验和。`tests/market.gd` 覆盖买卖、进贡、路线产金、异常拒绝、快照与确定性。

## 对局与胜负

`MatchState` / `PlayerState` 与其它状态一样存放在 ECS 世界里，随校验和与快照一起推进，表现层只经快照读取：

- 阶段：`Setup → Running → Ended`，结束时锁定 `winner`、触发条件与结束 tick。
- 可配置规则（`MatchConfig.Rules` 位掩码）：征服、弑君、奇观、圣物、分数，每 tick 末尾由 `MatchSystem` 确定性结算。
- 失败判定：玩家所有单位与建筑被摧毁、国王被杀、或主动投降；全部玩家出局判平局。
- 表现层在快照里拿到对局状态后显示结算面板（胜方、触发条件、结束 tick、双方分数与存活数），并提供投降/退出入口。

## 文明

- `CivilizationCatalog` 定义每个文明：名称、独特单位、全局属性加成与被禁科技位图。一期落两个文明——通用 baseline 与不列颠（长弓兵、全局攻击 +1、禁用鳞甲）。
- `GameSim` 构造时按玩家传入 `civIds` 选择文明（缺省通用）；文明 id 写入 `PlayerProgress.civ` 列，随校验和同步。
- 兵种生成前校验 `UnitDefs.IsUnitAvailable(civ, unitType)`，独特单位非所属文明不可造；属性走 `TechCatalog.EffectiveStat(civ, ...)`，在时代/科技加成之上叠加文明加成。
- 研究前校验 `TechCatalog.IsAvailable(civ, techId)`，被本文明禁用的科技不可研究。

## 运行与测试

```text
python .engine_test.py --ci              # CI 测试档（内核语义 + 协议 + 服务器/客户端 + 黄金校验和）
python .engine_test.py --id determinism  # 只跑黄金校验和回归
python .engine.py --headless --path . -- --server --port=27015   # 本机 headless dedicated server
dotnet build AOE2Framework.sln           # 单独编译 C# 内核
```

引擎版本固定在 `project.godot` 的 `[engine_host]`：`4.8` / `beta5` / `dotnet="true"`（自动取 mono 构建）。

## 里程碑

- [x] 阶段一：模拟内核（ECS + Q16.16 + 确定性随机 + 校验和）
- [x] 阶段二：流场寻路与移动
- [x] 阶段三：空间哈希战斗结算
- [x] 阶段四：锁步指令回合与跨端校验和一致
- [x] 阶段五：3D 等距视角演示
- [x] 迁移：切 Godot .NET 版、模拟内核移植 C#、服务器权威拆分（dedicated server + 协议 + 快照渲染 + 本地局本机服务器）
- [x] 对局流程：`MatchState`/`PlayerState`、可配置胜负条件、每 tick 确定性结算与结算界面
- [x] 战争迷雾与视野：C# 视距网格、快照按玩家裁剪、瘦客户端迷雾渲染
- [x] 体素地形与单位：TerrainGrid/MapData、每格四顶点高度与悬崖坡度寻路、贪心合并体素渲染与自建 `.vox` 资产
- [x] 地图编辑器：高度/地形/悬崖笔刷、出生点与资源放置、撤销重做、MapData 存读与连通校验
- [x] 地图生成与起始资源：确定性阿拉伯生成器、镜像起始资源、TC/村民/资源点落地与自测
