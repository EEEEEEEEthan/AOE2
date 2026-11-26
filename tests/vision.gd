extends TestBase

func run() -> void:
	_initial_state_is_unexplored()
	_unit_reveals_visible_circle()
	_leaving_becomes_explored()
	_explored_persists_across_ticks()
	_players_keep_independent_vision()
	_vision_is_deterministic()

func _initial_state_is_unexplored() -> void:
	var sim := GameSim.new(NavGrid.new(16, 16), 51)
	sim.spawn_unit(5, 5, 0, 40, 5, 1.0, 0.0)
	var vision := sim.vision_for(0)
	check_eq(vision.state_at(5, 5), VisionGrid.STATE_UNEXPLORED, "vision starts unexplored")
	check_true(not vision.visible_at(5, 5), "nothing is visible before a tick")

func _unit_reveals_visible_circle() -> void:
	var sim := GameSim.new(NavGrid.new(16, 16), 52)
	sim.spawn_unit(5, 5, 0, 40, 5, 1.0, 0.0, 3.0)
	sim.simulate_tick()
	var vision := sim.vision_for(0)
	check_eq(vision.state_at(5, 5), VisionGrid.STATE_VISIBLE, "unit tile becomes visible")
	check_true(vision.visible_at(6, 6), "tile inside sight radius is visible")
	check_eq(vision.state_at(15, 15), VisionGrid.STATE_UNEXPLORED, "tile outside sight stays unexplored")

func _leaving_becomes_explored() -> void:
	var sim := GameSim.new(NavGrid.new(16, 16), 53)
	var unit := sim.spawn_unit(5, 5, 0, 40, 5, 1.0, 0.0, 3.0)
	sim.simulate_tick()
	var vision := sim.vision_for(0)
	check_eq(vision.state_at(5, 5), VisionGrid.STATE_VISIBLE, "start tile visible after tick")
	_move_unit(sim, unit, 12, 12)
	sim.simulate_tick()
	check_eq(vision.state_at(5, 5), VisionGrid.STATE_EXPLORED, "left tile becomes explored")
	check_true(not vision.visible_at(5, 5), "left tile is no longer visible")
	check_eq(vision.state_at(12, 12), VisionGrid.STATE_VISIBLE, "new tile is visible")

func _explored_persists_across_ticks() -> void:
	var sim := GameSim.new(NavGrid.new(16, 16), 54)
	var unit := sim.spawn_unit(4, 4, 0, 40, 5, 1.0, 0.0, 2.0)
	sim.simulate_tick()
	_move_unit(sim, unit, 10, 10)
	for i in 5:
		sim.simulate_tick()
	var vision := sim.vision_for(0)
	check_eq(vision.state_at(4, 4), VisionGrid.STATE_EXPLORED, "explored tile stays explored")
	check_true(vision.is_explored(4, 4), "explored flag stays set")

func _players_keep_independent_vision() -> void:
	var sim := GameSim.new(NavGrid.new(24, 24), 55)
	sim.spawn_unit(5, 5, 0, 40, 5, 1.0, 0.0, 3.0)
	sim.spawn_unit(18, 18, 1, 40, 5, 1.0, 0.0, 3.0)
	sim.simulate_tick()
	var blue := sim.vision_for(0)
	var red := sim.vision_for(1)
	check_true(blue.visible_at(5, 5), "blue sees its own unit")
	check_true(not blue.visible_at(18, 18), "blue cannot see the red unit")
	check_true(red.visible_at(18, 18), "red sees its own unit")
	check_true(not red.visible_at(5, 5), "red cannot see the blue unit")

func _vision_is_deterministic() -> void:
	check_eq(_run_scenario(), _run_scenario(), "identical scenarios share a checksum")

func _run_scenario() -> int:
	var sim := GameSim.new(NavGrid.new(24, 24), 56)
	for index in 4:
		sim.spawn_unit(4, 4 + index, 0, 50, 7, 1.5, 0.2, 4.0)
		sim.spawn_unit(18, 4 + index, 1, 50, 7, 1.5, 0.2, 4.0)
	for i in 20:
		sim.simulate_tick()
	return sim.checksum()

func _move_unit(sim: GameSim, id: int, cell_x: int, cell_y: int) -> void:
	sim.world.set_value(id, SimComponents.POSITION, SimComponents.X, Fixed.add(Fixed.from_int(cell_x), Fixed.HALF))
	sim.world.set_value(id, SimComponents.POSITION, SimComponents.Y, Fixed.add(Fixed.from_int(cell_y), Fixed.HALF))
