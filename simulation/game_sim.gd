class_name GameSim
extends RefCounted

enum ResourceKind {
	WOOD = 0,
	FOOD = 1,
	GOLD = 2,
	STONE = 3,
}

var world: SimWorld
var grid: NavGrid
var spatial: SpatialHash
var lockstep: Lockstep
var _fields: Dictionary = {}
var _inventory_entities: Dictionary = {}

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

func spawn_resource_node(cell_x: int, cell_y: int, node_type: int, amount: int) -> int:
	_ensure_inventory_initialized()
	var id := world.spawn()
	world.attach(id, SimComponents.POSITION)
	world.attach(id, SimComponents.RESOURCE_NODE)
	world.set_value(id, SimComponents.POSITION, SimComponents.X, Fixed.add(Fixed.from_int(cell_x), Fixed.HALF))
	world.set_value(id, SimComponents.POSITION, SimComponents.Y, Fixed.add(Fixed.from_int(cell_y), Fixed.HALF))
	world.set_value(id, SimComponents.RESOURCE_NODE, SimComponents.NODE_TYPE, node_type)
	world.set_value(id, SimComponents.RESOURCE_NODE, SimComponents.REMAINING, amount)
	return id

func spawn_drop_site(cell_x: int, cell_y: int, accept_mask: int) -> int:
	_ensure_inventory_initialized()
	var id := world.spawn()
	world.attach(id, SimComponents.POSITION)
	world.attach(id, SimComponents.DROP_SITE)
	world.set_value(id, SimComponents.POSITION, SimComponents.X, Fixed.add(Fixed.from_int(cell_x), Fixed.HALF))
	world.set_value(id, SimComponents.POSITION, SimComponents.Y, Fixed.add(Fixed.from_int(cell_y), Fixed.HALF))
	world.set_value(id, SimComponents.DROP_SITE, SimComponents.ACCEPT_MASK, accept_mask)
	return id

func spawn_villager(cell_x: int, cell_y: int, team: int, speed_tiles_per_tick: float, carry_capacity: int, gather_rate_per_tick: float) -> int:
	_ensure_inventory_initialized()
	var id := world.spawn()
	world.attach(id, SimComponents.POSITION)
	world.attach(id, SimComponents.VELOCITY)
	world.attach(id, SimComponents.MOVE)
	world.attach(id, SimComponents.TEAM)
	world.attach(id, SimComponents.GATHER)
	world.set_value(id, SimComponents.POSITION, SimComponents.X, Fixed.add(Fixed.from_int(cell_x), Fixed.HALF))
	world.set_value(id, SimComponents.POSITION, SimComponents.Y, Fixed.add(Fixed.from_int(cell_y), Fixed.HALF))
	world.set_value(id, SimComponents.VELOCITY, SimComponents.X, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, 0)
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_X, cell_x)
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_Y, cell_y)
	world.set_value(id, SimComponents.MOVE, SimComponents.SPEED, Fixed.from_float(speed_tiles_per_tick))
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 0)
	world.set_value(id, SimComponents.TEAM, SimComponents.TEAM_ID, team)
	world.set_value(id, SimComponents.GATHER, SimComponents.GATHER_STATE, GatherSystem.GATHER_STATE_IDLE)
	world.set_value(id, SimComponents.GATHER, SimComponents.NODE_ENTITY, -1)
	world.set_value(id, SimComponents.GATHER, SimComponents.DROP_ENTITY, -1)
	world.set_value(id, SimComponents.GATHER, SimComponents.RATE, Fixed.from_float(gather_rate_per_tick))
	world.set_value(id, SimComponents.GATHER, SimComponents.CARRY_TYPE, 0)
	world.set_value(id, SimComponents.GATHER, SimComponents.CARRY_AMOUNT, 0)
	world.set_value(id, SimComponents.GATHER, SimComponents.CARRY_CAPACITY, carry_capacity)
	world.set_value(id, SimComponents.GATHER, SimComponents.PROGRESS, 0)
	return id

func resource(player: int, resource_kind: int) -> int:
	_ensure_inventory_initialized()
	return world.get_value(_inventory_entities[player], SimComponents.RESOURCE, SimComponents.RESOURCE_COLUMNS[resource_kind])

func add_resource(player: int, resource_kind: int, amount: int) -> void:
	_ensure_inventory_initialized()
	var inventory: int = _inventory_entities[player]
	var column: StringName = SimComponents.RESOURCE_COLUMNS[resource_kind]
	world.set_value(inventory, SimComponents.RESOURCE, column, world.get_value(inventory, SimComponents.RESOURCE, column) + amount)

func issue_move(id: int, cell_x: int, cell_y: int) -> void:
	if not world.is_alive(id) or not world.has_component(id, SimComponents.MOVE):
		return
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_X, cell_x)
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_Y, cell_y)
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 1)

func issue_gather(villager: int, node: int, drop: int) -> void:
	if not world.is_alive(villager) or not world.has_component(villager, SimComponents.GATHER):
		return
	world.set_value(villager, SimComponents.GATHER, SimComponents.GATHER_STATE, GatherSystem.GATHER_STATE_GATHERING)
	world.set_value(villager, SimComponents.GATHER, SimComponents.NODE_ENTITY, node)
	world.set_value(villager, SimComponents.GATHER, SimComponents.DROP_ENTITY, drop)
	world.set_value(villager, SimComponents.GATHER, SimComponents.CARRY_AMOUNT, 0)
	world.set_value(villager, SimComponents.GATHER, SimComponents.PROGRESS, 0)

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
		LockstepCommand.Kind.GATHER:
			issue_gather(command.entity, command.target_entity, command.drop_entity)
		_:
			pass

func simulate_tick() -> void:
	GatherSystem.step(world, _inventory_for_team)
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

func _ensure_inventory_initialized() -> void:
	if not _inventory_entities.is_empty():
		return
	for player in lockstep.player_count:
		var id := world.spawn()
		world.attach(id, SimComponents.RESOURCE)
		world.attach(id, SimComponents.TEAM)
		world.set_value(id, SimComponents.TEAM, SimComponents.TEAM_ID, player)
		for column: StringName in SimComponents.RESOURCE_COLUMNS:
			world.set_value(id, SimComponents.RESOURCE, column, 0)
		_inventory_entities[player] = id

func _inventory_for_team(team: int) -> int:
	return _inventory_entities[team]

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
