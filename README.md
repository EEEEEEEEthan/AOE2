# AOE2 确定性锁步 RTS 框架

面向「千数量级单位寻路与战斗、局域网同步、3D 等距视角」的框架骨架。核心思路是把**模拟**与**表现**彻底分离：模拟层只跑确定性逻辑，表现层只读模拟状态做渲染。

## 模块

| 模块 | 目录 | 职责 |
| --- | --- | --- |
| 定点数学 | `simulation/fixed/` | Q16.16 定点数与二维向量，跨机器逐位一致 |
| 确定性随机 | `simulation/rng/` | xorshift32，同种子产生同序列 |
| ECS | `simulation/ecs/` | SoA 列存储、实体生命周期、状态校验和 |
| 寻路与移动 | `simulation/pathfinding/` | 导航网格、流场、移动系统 |
| 战斗 | `simulation/combat/` | 空间哈希、最近目标检索、伤害结算 |
| 锁步网络 | `simulation/network/` | 回合指令缓冲、按玩家固定顺序派发 |
| 编排 | `simulation/game_sim.gd` | 组合世界、系统、流场缓存、锁步回合 |
| C# 模拟内核 | `simulation_cs/` | 与 GDScript 内核逐位一致的 .NET 实现，供服务器权威架构使用 |
| 表现 | `presentation/` | 3D 等距相机、单位与地形渲染、固定步长驱动 |

## 确定性约定

- 所有模拟计算只用整数与定点，禁止在模拟层出现 `float`。
- 实体与系统按实体 id 升序迭代；指令按玩家 id 升序、批内顺序派发。
- 组件与列名统一转 `String` 按内容排序后再遍历、按内容取哈希，不依赖 `StringName` 内部索引。
- 校验和覆盖实体、组件列、`tick`、`next_entity` 与 RNG 状态。
- 对角移动按 1/√2 归一化，斜向合速度与正交一致。
- 同一初始状态 + 同一指令序列 = 同一校验和，这是锁步与 desync 检测的基础。

## C# 模拟内核

游戏引擎已切到 Godot 的 .NET 版（`project.godot` 的 `[engine_host] dotnet="true"`），模拟内核在 `simulation_cs/` 用 C# 重写，目标是**服务器权威 + 瘦客户端**：服务器跑模拟、出状态快照，客户端只渲染并上发指令。

- 与 GDScript 内核**逐位一致**：整数/定点、`String` 内容排序、FNV 校验和、实体升序迭代全部复刻；禁止 `float`。
- `SimulationBridge` 是暴露给 Godot / GDScript 的 `[GlobalClass]` 入口；`tests/parity.gd` 对同一场景分别用 GDScript 与 C# 各跑一遍，断言校验和相等，作为跨语言确定性对拍。
- 迁移分阶段：阶段 0 已完成（切 .NET、`.engine_test.py --ci` 跑通），阶段 1 内核移植与对拍已完成；阶段 2 前后端拆分、表现层改为消费 C# 服务器快照后，再删除 `simulation/` 的旧 GDScript 实现。

## 运行与测试

```text
python .engine_test.py --ci       # CI 测试档（含跨语言对拍 tests/parity.gd）
python .engine_test.py --id parity   # 只跑 GDScript vs C# 确定性对拍
python .engine_test.py --id pathfinding combat   # 点名自测
dotnet build AOE2Framework.sln    # 单独编译 C# 内核
```

引擎版本固定在 `project.godot` 的 `[engine_host]`：`4.8` / `beta5` / `dotnet="true"`（自动取 mono 构建）。

## 里程碑

- [x] 阶段一：模拟内核（ECS + Q16.16 + 确定性随机 + 校验和）
- [x] 阶段二：流场寻路与移动
- [x] 阶段三：空间哈希战斗结算
- [x] 阶段四：锁步指令回合与跨端校验和一致
- [x] 阶段五：3D 等距视角演示
