extends TestBase

func run() -> void:
	_gather_grows_carry_and_depletes_node()
	_delivery_fills_inventory_and_conserves_resources()
	_depleted_node_ends_cycle()
	_gather_command_roundtrips()
	_economy_is_deterministic()

func _gather_grows_carry_and_depletes_node() -> void:
	var sim := GameSim.new(NavGrid.new(16, 16), 41)
	var initial_amount := 100
	var node := sim.spawn_resource_node(5, 5, GameSim.ResourceKind.WOOD, initial_amount)
	var drop := sim.spawn_drop_site(9, 5, 0b1111)
	var villager := sim.spawn_villager(5, 5, 0, 0.0, 8, 1.0)
	sim.issue_gather(villager, node, drop)
	for i in 3:
		sim.simulate_tick()
	var carry := _value(sim, villager, SimComponents.GATHER, SimComponents.CARRY_AMOUNT)
	var remaining := _value(sim, node, SimComponents.RESOURCE_NODE, SimComponents.REMAINING)
	check_true(carry > 0, "villager carries resources after gathering")
	check_true(remaining < initial_amount, "node loses resources while gathered")
	check_eq(carry + remaining, initial_amount, "carried and remaining equal the initial amount")

func _delivery_fills_inventory_and_conserves_resources() -> void:
	var sim := GameSim.new(NavGrid.new(20, 20), 42)
	var initial_amount := 100
	var capacity := 4
	var node := sim.spawn_resource_node(4, 5, GameSim.ResourceKind.WOOD, initial_amount)
	var drop := sim.spawn_drop_site(10, 5, 0b1111)
	var villager := sim.spawn_villager(4, 5, 0, 0.2, capacity, 1.0)
	sim.issue_gather(villager, node, drop)
	var capacity_respected := true
	for i in 80:
		sim.simulate_tick()
		if _value(sim, villager, SimComponents.GATHER, SimComponents.CARRY_AMOUNT) > capacity:
			capacity_respected = false
	var inventory := sim.resource(0, GameSim.ResourceKind.WOOD)
	var carry := _value(sim, villager, SimComponents.GATHER, SimComponents.CARRY_AMOUNT)
	var remaining := _value(sim, node, SimComponents.RESOURCE_NODE, SimComponents.REMAINING)
	check_true(capacity_respected, "carry never exceeds capacity")
	check_true(inventory > 0, "drop site delivers resources into the player inventory")
	check_eq(inventory + carry + remaining, initial_amount, "resources are conserved across the cycle")

func _depleted_node_ends_cycle() -> void:
	var sim := GameSim.new(NavGrid.new(20, 20), 43)
	var initial_amount := 3
	var node := sim.spawn_resource_node(4, 5, GameSim.ResourceKind.STONE, initial_amount)
	var drop := sim.spawn_drop_site(9, 5, 0b1111)
	var villager := sim.spawn_villager(4, 5, 0, 0.2, 10, 1.0)
	sim.issue_gather(villager, node, drop)
	for i in 120:
		sim.simulate_tick()
	check_eq(_value(sim, node, SimComponents.RESOURCE_NODE, SimComponents.REMAINING), 0, "node is fully depleted")
	check_eq(_value(sim, villager, SimComponents.GATHER, SimComponents.GATHER_STATE), GatherSystem.GATHER_STATE_IDLE, "villager idles after depletion")
	check_eq(_value(sim, villager, SimComponents.GATHER, SimComponents.CARRY_AMOUNT), 0, "villager unloads before idling")
	check_eq(sim.resource(0, GameSim.ResourceKind.STONE), initial_amount, "inventory equals the depleted node amount")

func _gather_command_roundtrips() -> void:
	var original := LockstepCommand.new(1, LockstepCommand.Kind.GATHER, 12, 0, 0, 30, 31)
	var restored := LockstepCommand.from_array(original.to_array())
	check_eq(restored.kind, LockstepCommand.Kind.GATHER, "gather kind survives serialization")
	check_eq(restored.entity, 12, "villager id survives serialization")
	check_eq(restored.target_entity, 30, "resource node id survives serialization")
	check_eq(restored.drop_entity, 31, "drop site id survives serialization")
	var legacy := LockstepCommand.from_array(PackedInt32Array([0, 0, 5, 1, 2, 9]))
	check_eq(legacy.drop_entity, -1, "legacy command defaults the missing drop site")

func _economy_is_deterministic() -> void:
	check_eq(_run_economy(), _run_economy(), "identical economy runs share a checksum")

func _run_economy() -> int:
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

func _value(sim: GameSim, entity: int, component: StringName, column: StringName) -> int:
	return sim.world.get_value(entity, component, column)
