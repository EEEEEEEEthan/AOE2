extends TestBase

func run() -> void:
	_enemy_takes_damage_and_dies()
	_friendly_fire_disabled()
	_out_of_range_no_damage()
	_combat_is_deterministic()

func _enemy_takes_damage_and_dies() -> void:
	var sim := GameSim.new(NavGrid.new(12, 12), 21)
	var attacker := sim.spawn_unit(5, 5, 0, 100, 10, 1.2, 0.0)
	var victim := sim.spawn_unit(6, 5, 1, 30, 10, 1.2, 0.0)
	for i in 40:
		sim.simulate_tick()
	check_true(sim.world.is_alive(attacker), "stronger attacker survives")
	check_true(not sim.world.is_alive(victim), "weaker defender dies")
	check_eq(sim.world.entities().size(), 1, "only attacker remains")
	check_eq(sim.world.get_value(attacker, SimComponents.HEALTH, SimComponents.HP), 70, "attacker lost three exchanges")

func _friendly_fire_disabled() -> void:
	var sim := GameSim.new(NavGrid.new(12, 12), 22)
	sim.spawn_unit(5, 5, 0, 40, 10, 1.2, 0.0)
	sim.spawn_unit(6, 5, 0, 40, 10, 1.2, 0.0)
	for i in 40:
		sim.simulate_tick()
	check_eq(sim.world.entities().size(), 2, "same team does not fight")

func _out_of_range_no_damage() -> void:
	var sim := GameSim.new(NavGrid.new(16, 16), 23)
	var a := sim.spawn_unit(2, 2, 0, 40, 10, 1.0, 0.0)
	var b := sim.spawn_unit(10, 10, 1, 40, 10, 1.0, 0.0)
	for i in 40:
		sim.simulate_tick()
	check_eq(sim.world.get_value(a, SimComponents.HEALTH, SimComponents.HP), 40, "far attacker keeps health")
	check_eq(sim.world.get_value(b, SimComponents.HEALTH, SimComponents.HP), 40, "far defender keeps health")

func _combat_is_deterministic() -> void:
	check_eq(_run_battle(), _run_battle(), "identical battles share checksum")

func _run_battle() -> int:
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
