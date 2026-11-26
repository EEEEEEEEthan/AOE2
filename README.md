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

- **服务器**：`SimulationServer` 持有权威 `GameSim`，按 30Hz 推进并产出 `SnapshotFrame`；`DedicatedServer` 把同一内核放进独立进程，经 TCP（`Protocol.cs` 的长度前缀帧）广播快照、接收 `CommandFrame`。
- **瘦客户端**：`SimulationClient` 只保存解码后的快照用于渲染，从不直接读服务器模拟对象；本地局用 `LocalSession` 内嵌本机服务器，快照同样走编码/解码。
- **表现层**：`presentation/` 通过 `GameBridge` 读快照渲染，不再引用任何模拟类。
- **确定性**：整数/定点、`String` 内容排序、FNV 校验和、实体升序迭代全部保留；`tests/determinism.gd` 用固化黄金校验和守住回归，`tests/kernel.gd` 覆盖各模块语义。

## 战争迷雾

- 每个玩家一份 `VisionGrid`（`_visible` 每 tick 重置、`_explored` 持久），`VisionSystem` 每 tick 以定点欧氏圆揭示单位/建筑视距内的格子并写回已探索。
- 视野随 `SnapshotFrame` 下发：快照按观察者玩家裁剪实体（己方恒见、中立与静态建筑按已探索、敌方单位仅当前可见），并附带整张可视性位图。
- 表现层 `WorldView` 依据位图渲染迷雾（未探索黑 / 已探索灰 / 可见透明）并隐藏未探索地形；`GameBridge` 只暴露解码后的客户端快照。`KernelSelfTest` 覆盖揭示、离开转已探索、双玩家独立、快照裁剪与确定性。

## 体素地形与单位

- 1 格 = 1 Godot 单位；取 4px 为 1 体素，即每格 8×8 体素，高度层 `1/8` 单位。普通地面可 0～2 层起伏，悬崖落差可达 8 层。
- `TerrainGrid`（`simulation_cs/TerrainGrid.cs`）逐格存类型（草地/泥土/水/崖）与 uint8 高度层，**与 `NavGrid` 完全独立**：NavGrid 继续只描述建筑 footprint 与障碍，地形高度不写进去，避免与 AOE-5 冲突。
- 寻路接高度差：`FlowField` 与 `MovementSystem` 通过 `TerrainGrid.CanTraverse` 判定，高度差超过 1 层、水或崖面不可通行，悬崖上下自然不通。
- 地图格式由 `MapData`（`simulation_cs/MapData.cs`）组合 TerrainGrid 与 NavGrid；`maps/voxel_demo.json` 是示例，行优先扁平数组 `types` / `heights` / `blocked`。
- 表现层（`presentation/world_view.gd`）按高度场生成体素地形，`presentation/voxel_mesher.gd` 用贪心合并（greedy meshing）并只用顶点色；地形剔除下方面，单位保留全部六面（倒地时可看到底面）。
- 自建体素模型在 `assets/vox/`（`.vox`），由 `tools/make_vox_models.py` 生成；`presentation/vox_model.gd` 解析 RIFF 的 `SIZE/XYZI/RGBA`，调色板索引 1 作阵营色。原作素材只放 `.reference/`（已 gitignore），不入库。

## 对局与胜负

`MatchState` / `PlayerState` 与其它状态一样存放在 ECS 世界里，随校验和与快照一起推进，表现层只经快照读取：

- 阶段：`Setup → Running → Ended`，结束时锁定 `winner`、触发条件与结束 tick。
- 可配置规则（`MatchConfig.Rules` 位掩码）：征服、弑君、奇观、圣物、分数，每 tick 末尾由 `MatchSystem` 确定性结算。
- 失败判定：玩家所有单位与建筑被摧毁、国王被杀、或主动投降；全部玩家出局判平局。
- 表现层在快照里拿到对局状态后显示结算面板（胜方、触发条件、结束 tick、双方分数与存活数），并提供投降/退出入口。

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
- [x] 体素地形与单位：TerrainGrid/MapData、高度差寻路、贪心合并体素渲染与自建 `.vox` 资产
