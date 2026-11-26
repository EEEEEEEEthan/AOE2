extends TestBase

func run() -> void:
	_flow_field_moves_toward_goal()
	_unit_reaches_target()
	_unit_routes_around_wall()
	_movement_is_deterministic()

func _flow_field_moves_toward_goal() -> void:
	var grid := NavGrid.new(16, 16)
	var field := FlowField.new(grid)
	check_true(field.compute(grid, 10, 10), "flow field computes for open goal")
	check_true(field.reachable(2, 2), "far cell reachable")
	var x := 2
	var y := 2
	var steps := 0
	while field.cost_at(x, y) > 0 and steps < 200:
		var direction := field.direction_at(x, y)
		check_true(direction != Vector2i.ZERO, "direction present at %d,%d" % [x, y])
		x += direction.x
		y += direction.y
		steps += 1
	check_eq(field.cost_at(x, y), 0, "greedy descent reaches goal cell")
	check_eq(x, 10, "goal cell x reached")
	check_eq(y, 10, "goal cell y reached")

func _unit_reaches_target() -> void:
	var grid := NavGrid.new(16, 16)
	var sim := GameSim.new(grid, 11)
	var id := sim.spawn_unit(2, 2, 0, 50, 0, 0.0, 0.5)
	sim.issue_move(id, 10, 10)
	for i in 120:
		sim.simulate_tick()
	var x := sim.world.get_value(id, SimComponents.POSITION, SimComponents.X)
	var y := sim.world.get_value(id, SimComponents.POSITION, SimComponents.Y)
	check_eq(Fixed.to_int(x), 10, "unit moved to target x")
	check_eq(Fixed.to_int(y), 10, "unit moved to target y")
	check_eq(sim.world.get_value(id, SimComponents.MOVE, SimComponents.ACTIVE), 0, "unit cleared move on arrival")

func _unit_routes_around_wall() -> void:
	var grid := NavGrid.new(16, 16)
	for wall_y in 16:
		if wall_y != 3:
			grid.set_blocked(8, wall_y, true)
	var field := FlowField.new(grid)
	check_true(field.compute(grid, 13, 8), "flow field avoids blocked goal wall")
	check_true(field.reachable(2, 8), "cell across wall reachable through gap")
	var sim := GameSim.new(grid, 12)
	var id := sim.spawn_unit(2, 8, 0, 50, 0, 0.0, 0.6)
	sim.issue_move(id, 13, 8)
	for i in 300:
		sim.simulate_tick()
	var x := sim.world.get_value(id, SimComponents.POSITION, SimComponents.X)
	var y := sim.world.get_value(id, SimComponents.POSITION, SimComponents.Y)
	check_eq(Fixed.to_int(x), 13, "unit crosses wall gap to target x")
	check_eq(Fixed.to_int(y), 8, "unit crosses wall gap to target y")

func _movement_is_deterministic() -> void:
	var first := _run_march(77)
	var second := _run_march(77)
	check_eq(first, second, "identical setup produces identical checksum")
	check_ne(first, _run_march_altered(), "different setup changes outcome")

func _run_march_altered() -> int:
	var grid := NavGrid.new(20, 20)
	var sim := GameSim.new(grid, 77)
	for i in 20:
		var id := sim.spawn_unit(2 + (i % 4), 3 + (i % 5), 0, 40, 0, 0.0, 0.3)
		sim.issue_move(id, 16, 16)
	sim.spawn_unit(1, 1, 1, 40, 0, 0.0, 0.3)
	for i in 80:
		sim.simulate_tick()
	return sim.world.checksum()

func _run_march(seed_value: int) -> int:
	var grid := NavGrid.new(20, 20)
	var sim := GameSim.new(grid, seed_value)
	for i in 20:
		var id := sim.spawn_unit(2 + (i % 4), 3 + (i % 5), 0, 40, 0, 0.0, 0.3)
		sim.issue_move(id, 16, 16)
	for i in 80:
		sim.simulate_tick()
	return sim.world.checksum()
