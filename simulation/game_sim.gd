class_name GameSim
extends RefCounted

var world: SimWorld
var grid: NavGrid
var spatial: SpatialHash
var lockstep: Lockstep
var _fields: Dictionary = {}

func _init(p_grid: NavGrid, seed_value: int = 1, player_count: int = 2, ticks_per_turn: int = 6) -> void:
	grid = p_grid
	world = SimWorld.new(seed_value)
	spatial = SpatialHash.new(4)
	lockstep = Lockstep.new(player_count, ticks_per_turn)

func spawn_unit(cell_x: int, cell_y: int, team: int, max_hp: int, damage: int, range_tiles: float, speed_tiles_per_tick: float) -> int:
	var id := world.spawn()
	world.attach(id, SimComponents.POSITION)
	world.attach(id, SimComponents.VELOCITY)
	world.attach(id, SimComponents.MOVE)
	world.attach(id, SimComponents.HEALTH)
	world.attach(id, SimComponents.COMBAT)
	world.attach(id, SimComponents.TEAM)
	world.set_value(id, SimComponents.POSITION, SimComponents.X, Fixed.add(Fixed.from_int(cell_x), Fixed.HALF))
	world.set_value(id, SimComponents.POSITION, SimComponents.Y, Fixed.add(Fixed.from_int(cell_y), Fixed.HALF))
	world.set_value(id, SimComponents.VELOCITY, SimComponents.X, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, 0)
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_X, cell_x)
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_Y, cell_y)
	world.set_value(id, SimComponents.MOVE, SimComponents.SPEED, Fixed.from_float(speed_tiles_per_tick))
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 0)
	world.set_value(id, SimComponents.HEALTH, SimComponents.HP, max_hp)
	world.set_value(id, SimComponents.HEALTH, SimComponents.MAX_HP, max_hp)
	var range_fixed := Fixed.from_float(range_tiles)
	world.set_value(id, SimComponents.COMBAT, SimComponents.DAMAGE, damage)
	world.set_value(id, SimComponents.COMBAT, SimComponents.RANGE_SQ, Fixed.mul(range_fixed, range_fixed))
	world.set_value(id, SimComponents.COMBAT, SimComponents.COOLDOWN, 6)
	world.set_value(id, SimComponents.COMBAT, SimComponents.COOLDOWN_LEFT, 0)
	world.set_value(id, SimComponents.TEAM, SimComponents.TEAM_ID, team)
	return id

func issue_move(id: int, cell_x: int, cell_y: int) -> void:
	if not world.is_alive(id) or not world.has_component(id, SimComponents.MOVE):
		return
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_X, cell_x)
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_Y, cell_y)
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 1)

func stop(id: int) -> void:
	if not world.is_alive(id):
		return
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.X, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, 0)

func apply_command(command: LockstepCommand) -> void:
	match command.kind:
		LockstepCommand.Kind.MOVE:
			issue_move(command.entity, command.cell_x, command.cell_y)
		LockstepCommand.Kind.STOP:
			stop(command.entity)
		_:
			pass

func simulate_tick() -> void:
	MovementSystem.step(world, grid, _field_provider)
	CombatSystem.step(world, spatial)
	world.tick += 1

func simulate_turn() -> int:
	if lockstep.can_advance():
		for command: LockstepCommand in lockstep.take_commands():
			apply_command(command)
	for i in lockstep.ticks_per_turn:
		simulate_tick()
	return world.checksum()

func submit_turn(player: int, commands: Array) -> bool:
	return lockstep.submit_turn(lockstep.turn, player, commands)

func _field_provider(target_x: int, target_y: int) -> FlowField:
	var key := Vector2i(target_x, target_y)
	if _fields.has(key):
		return _fields[key]
	var field := FlowField.new(grid)
	field.compute(grid, target_x, target_y)
	_fields[key] = field
	return field

func field_for(target_x: int, target_y: int) -> FlowField:
	return _field_provider(target_x, target_y)

func invalidate_fields() -> void:
	_fields.clear()
