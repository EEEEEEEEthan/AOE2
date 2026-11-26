extends TestBase

const FNV_OFFSET := 0x811C_9DC5
const FNV_PRIME := 0x0100_0193
const MASK32 := 0xFFFF_FFFF


func run() -> void:
	var bridge := SimulationBridge.new()
	check_eq(_rng_digest(12345, 64), bridge.RngDigest(12345, 64), "rng digest matches c# for seed 12345")
	check_eq(_rng_digest(999, 200), bridge.RngDigest(999, 200), "rng digest matches c# for seed 999")
	check_eq(_path_march(77, false), bridge.ChecksumScenario(0), "path march checksum matches c#")
	check_eq(_path_march(77, true), bridge.ChecksumScenario(1), "altered path march checksum matches c#")
	check_eq(_combat_battle(), bridge.ChecksumScenario(2), "combat battle checksum matches c#")
	check_eq(_economy_run(), bridge.ChecksumScenario(3), "economy run checksum matches c#")
	check_eq(_lockstep_turn(), bridge.ChecksumScenario(4), "lockstep turn checksum matches c#")
	check_eq(_unit_reach(), bridge.ChecksumScenario(5), "unit reach checksum matches c#")
	check_eq(_diagonal_step(), bridge.ChecksumScenario(6), "diagonal step checksum matches c#")
	check_eq(_gather_deplete(), bridge.ChecksumScenario(7), "gather deplete checksum matches c#")
	bridge.free()


func _rng_digest(seed_value: int, count: int) -> int:
	var rng := SimRng.new(seed_value)
	var hash := FNV_OFFSET
	for draw in count:
		hash = _hash_int(hash, rng.next_u32())
	return hash


func _hash_int(hash: int, value: int) -> int:
	var bytes := value & MASK32
	for shift in range(0, 64, 8):
		hash = (hash ^ ((bytes >> shift) & 0xFF)) & MASK32
		hash = (hash * FNV_PRIME) & MASK32
	return hash


func _path_march(seed_value: int, with_extra_team: bool) -> int:
	var grid := NavGrid.new(20, 20)
	var sim := GameSim.new(grid, seed_value)
	for i in 20:
		var id := sim.spawn_unit(2 + (i % 4), 3 + (i % 5), 0, 40, 0, 0.0, 0.3)
		sim.issue_move(id, 16, 16)
	if with_extra_team:
		sim.spawn_unit(1, 1, 1, 40, 0, 0.0, 0.3)
	for i in 80:
		sim.simulate_tick()
	return sim.world.checksum()


func _combat_battle() -> int:
	var sim := GameSim.new(NavGrid.new(20, 20), 24)
	for i in 12:
		var attacker := sim.spawn_unit(4, 4 + i, 0, 50, 7, 1.5, 0.2)
		sim.issue_move(attacker, 9, 10)
	for i in 12:
		var defender := sim.spawn_unit(15, 4 + i, 1, 50, 7, 1.5, 0.2)
		sim.issue_move(defender, 10, 10)
	for i in 120:
		sim.simulate_tick()
	return sim.world.checksum()


func _economy_run() -> int:
	var sim := GameSim.new(NavGrid.new(24, 24), 44)
	var wood := sim.spawn_resource_node(5, 5, GameSim.ResourceKind.WOOD, 50)
	var gold := sim.spawn_resource_node(5, 8, GameSim.ResourceKind.GOLD, 30)
	var drop := sim.spawn_drop_site(10, 6, 0b1111)
	for index in 4:
		var villager := sim.spawn_villager(4 + index, 4, 0, 0.2, 5, 1.0)
		sim.issue_gather(villager, wood if index % 2 == 0 else gold, drop)
	for i in 120:
		sim.simulate_tick()
	return sim.world.checksum()


func _lockstep_turn() -> int:
	var sim := GameSim.new(NavGrid.new(24, 24), 31, 2, 6)
	for i in 6:
		sim.spawn_unit(3 + i, 4, 0, 50, 6, 1.5, 0.2)
		sim.spawn_unit(3 + i, 16, 1, 50, 6, 1.5, 0.2)
	var player_zero: Array = []
	var player_one: Array = []
	for id in sim.world.entities():
		var team := sim.world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
		if team == 0:
			player_zero.append(LockstepCommand.new(0, LockstepCommand.Kind.MOVE, id, 6, 10))
		else:
			player_one.append(LockstepCommand.new(1, LockstepCommand.Kind.MOVE, id, 6, 10))
	sim.submit_turn(0, player_zero)
	sim.submit_turn(1, player_one)
	return sim.simulate_turn()


func _unit_reach() -> int:
	var sim := GameSim.new(NavGrid.new(16, 16), 11)
	var id := sim.spawn_unit(2, 2, 0, 50, 0, 0.0, 0.5)
	sim.issue_move(id, 10, 10)
	for i in 120:
		sim.simulate_tick()
	return sim.world.checksum()


func _diagonal_step() -> int:
	var sim := GameSim.new(NavGrid.new(16, 16), 41)
	var id := sim.spawn_unit(2, 2, 0, 10, 0, 0.0, 0.4)
	sim.issue_move(id, 3, 3)
	sim.simulate_tick()
	return sim.world.checksum()


func _gather_deplete() -> int:
	var sim := GameSim.new(NavGrid.new(20, 20), 43)
	var node := sim.spawn_resource_node(4, 5, GameSim.ResourceKind.STONE, 3)
	var drop := sim.spawn_drop_site(9, 5, 0b1111)
	var villager := sim.spawn_villager(4, 5, 0, 0.2, 10, 1.0)
	sim.issue_gather(villager, node, drop)
	for i in 120:
		sim.simulate_tick()
	return sim.world.checksum()
