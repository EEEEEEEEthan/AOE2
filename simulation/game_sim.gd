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

func issue_place(type: int, cell_x: int, cell_y: int, player: int) -> int:
	var id := BuildingSystem.place(world, grid, type, cell_x, cell_y, player)
	if id >= 0:
		invalidate_fields()
	return id

func issue_construct(villager_id: int, building_id: int) -> void:
	if not world.is_alive(villager_id) or not world.has_component(villager_id, SimComponents.MOVE):
		return
	if not world.is_alive(building_id) or not world.has_component(building_id, SimComponents.BUILDING):
		return
	if world.get_value(building_id, SimComponents.BUILDING, SimComponents.STATE) != BuildingSystem.STATE_FOUNDATION:
		return
	world.attach(villager_id, SimComponents.CONSTRUCT)
	world.set_value(villager_id, SimComponents.CONSTRUCT, SimComponents.TARGET_BASE, building_id)
	world.set_value(villager_id, SimComponents.CONSTRUCT, SimComponents.STATE, BuildingSystem.CONSTRUCT_ACTIVE)

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
		LockstepCommand.Kind.PLACE_BUILDING:
			issue_place(command.build_type, command.cell_x, command.cell_y, command.player)
		LockstepCommand.Kind.CONSTRUCT:
			issue_construct(command.entity, command.target_entity)
		_:
			pass

func simulate_tick() -> void:
	BuildingSystem.step(world, grid)
	MovementSystem.step(world, grid, _field_provider)
	var dead := CombatSystem.step(world, spatial)
	_resolve_deaths(dead)
	world.tick += 1

func _resolve_deaths(dead: PackedInt64Array) -> void:
	var fields_changed := false
	for id in dead:
		if not world.is_alive(id):
			continue
		if world.has_component(id, SimComponents.BUILDING):
			BuildingSystem.ruin(world, grid, id)
			fields_changed = true
		else:
			world.despawn(id)
	if fields_changed:
		invalidate_fields()

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
