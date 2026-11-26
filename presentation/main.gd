extends Node3D

const GRID_WIDTH := 32
const GRID_HEIGHT := 32
const TICK_SECONDS := 1.0 / 30.0
const SEED := 20261126

var sim: GameSim
var _accumulator := 0.0

func _ready() -> void:
	sim = GameSim.new(_build_grid(), SEED, 2, 6)
	%WorldView.bind_sim(sim)
	%WorldView.build_terrain(sim.grid)
	_spawn_forces()
	_issue_orders()
	%WorldView.refresh()

func _process(delta: float) -> void:
	if sim == null:
		return
	_accumulator += delta
	var steps := 0
	while _accumulator >= TICK_SECONDS and steps < 6:
		sim.simulate_tick()
		_accumulator -= TICK_SECONDS
		steps += 1
	if steps > 0:
		%WorldView.refresh()

func _build_grid() -> NavGrid:
	var grid := NavGrid.new(GRID_WIDTH, GRID_HEIGHT)
	for y in GRID_HEIGHT:
		grid.set_blocked(0, y, true)
		grid.set_blocked(GRID_WIDTH - 1, y, true)
	for x in GRID_WIDTH:
		grid.set_blocked(x, 0, true)
		grid.set_blocked(x, GRID_HEIGHT - 1, true)
	for y in range(3, 13):
		grid.set_blocked(16, y, true)
	for y in range(19, 29):
		grid.set_blocked(16, y, true)
	grid.set_blocked_rect(22, 6, 3, 2)
	grid.set_blocked_rect(8, 24, 2, 4)
	return grid

func _spawn_forces() -> void:
	for row in 9:
		for column in 8:
			var cell_y := 7 + row * 2
			var team_blue_x := 3 + column
			var team_red_x := 28 - column
			sim.spawn_unit(team_blue_x, cell_y, 0, 60, 8, 1.2, 0.09)
			sim.spawn_unit(team_red_x, cell_y, 1, 60, 8, 1.2, 0.09)

func _issue_orders() -> void:
	for id in sim.world.entities():
		var team := sim.world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
		if team == 0:
			sim.issue_move(id, 27, 16)
		else:
			sim.issue_move(id, 4, 16)
