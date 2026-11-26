class_name GatherSystem
extends RefCounted

const GATHER_STATE_IDLE := 0
const GATHER_STATE_GATHERING := 1
const GATHER_STATE_RETURNING := 2

const GATHER_RANGE_SQ := 147456

static func step(world: SimWorld, inventory_provider: Callable) -> void:
	var gather_store := world.store(SimComponents.GATHER)
	if gather_store.count == 0:
		return
	for id in world.entities():
		if not gather_store.has(id):
			continue
		var state := gather_store.get_value(id, SimComponents.GATHER_STATE)
		if state == GATHER_STATE_GATHERING:
			_step_gathering(world, id)
		elif state == GATHER_STATE_RETURNING:
			_step_returning(world, id, inventory_provider)

static func _step_gathering(world: SimWorld, id: int) -> void:
	var node := world.get_value(id, SimComponents.GATHER, SimComponents.NODE_ENTITY)
	var remaining := 0
	if _is_resource_node(world, node):
		remaining = world.get_value(node, SimComponents.RESOURCE_NODE, SimComponents.REMAINING)
	if remaining <= 0:
		_leave_gathering(world, id)
		return
	if not _in_range(world, id, node):
		_move_to_cell(world, id, node)
		return
	_stop(world, id)
	var carry_amount := world.get_value(id, SimComponents.GATHER, SimComponents.CARRY_AMOUNT)
	var carry_capacity := world.get_value(id, SimComponents.GATHER, SimComponents.CARRY_CAPACITY)
	if carry_amount == 0:
		world.set_value(id, SimComponents.GATHER, SimComponents.CARRY_TYPE, world.get_value(node, SimComponents.RESOURCE_NODE, SimComponents.NODE_TYPE))
	var progress := world.get_value(id, SimComponents.GATHER, SimComponents.PROGRESS) + world.get_value(id, SimComponents.GATHER, SimComponents.RATE)
	while progress >= Fixed.SCALE and carry_amount < carry_capacity and remaining > 0:
		progress -= Fixed.SCALE
		carry_amount += 1
		remaining -= 1
	world.set_value(id, SimComponents.GATHER, SimComponents.PROGRESS, progress)
	world.set_value(id, SimComponents.GATHER, SimComponents.CARRY_AMOUNT, carry_amount)
	world.set_value(node, SimComponents.RESOURCE_NODE, SimComponents.REMAINING, remaining)
	if carry_amount >= carry_capacity or remaining <= 0:
		world.set_value(id, SimComponents.GATHER, SimComponents.GATHER_STATE, GATHER_STATE_RETURNING)

static func _step_returning(world: SimWorld, id: int, inventory_provider: Callable) -> void:
	var drop := world.get_value(id, SimComponents.GATHER, SimComponents.DROP_ENTITY)
	if not _is_drop_site(world, drop):
		world.set_value(id, SimComponents.GATHER, SimComponents.GATHER_STATE, GATHER_STATE_IDLE)
		return
	if not _in_range(world, id, drop):
		_move_to_cell(world, id, drop)
		return
	_stop(world, id)
	var carry_type := world.get_value(id, SimComponents.GATHER, SimComponents.CARRY_TYPE)
	var accept_mask := world.get_value(drop, SimComponents.DROP_SITE, SimComponents.ACCEPT_MASK)
	if (accept_mask & (1 << carry_type)) == 0:
		world.set_value(id, SimComponents.GATHER, SimComponents.GATHER_STATE, GATHER_STATE_IDLE)
		return
	var carry_amount := world.get_value(id, SimComponents.GATHER, SimComponents.CARRY_AMOUNT)
	var inventory := int(inventory_provider.call(world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)))
	var column: StringName = SimComponents.RESOURCE_COLUMNS[carry_type]
	var stored := world.get_value(inventory, SimComponents.RESOURCE, column)
	world.set_value(inventory, SimComponents.RESOURCE, column, stored + carry_amount)
	world.set_value(id, SimComponents.GATHER, SimComponents.CARRY_AMOUNT, 0)
	world.set_value(id, SimComponents.GATHER, SimComponents.PROGRESS, 0)
	var node := world.get_value(id, SimComponents.GATHER, SimComponents.NODE_ENTITY)
	if _is_resource_node(world, node) and world.get_value(node, SimComponents.RESOURCE_NODE, SimComponents.REMAINING) > 0:
		world.set_value(id, SimComponents.GATHER, SimComponents.GATHER_STATE, GATHER_STATE_GATHERING)
	else:
		world.set_value(id, SimComponents.GATHER, SimComponents.GATHER_STATE, GATHER_STATE_IDLE)

static func _leave_gathering(world: SimWorld, id: int) -> void:
	var carry_amount := world.get_value(id, SimComponents.GATHER, SimComponents.CARRY_AMOUNT)
	var next_state := GATHER_STATE_RETURNING if carry_amount > 0 else GATHER_STATE_IDLE
	world.set_value(id, SimComponents.GATHER, SimComponents.GATHER_STATE, next_state)

static func _is_resource_node(world: SimWorld, id: int) -> bool:
	return world.is_alive(id) and world.has_component(id, SimComponents.RESOURCE_NODE)

static func _is_drop_site(world: SimWorld, id: int) -> bool:
	return world.is_alive(id) and world.has_component(id, SimComponents.DROP_SITE)

static func _in_range(world: SimWorld, first: int, second: int) -> bool:
	var first_x := world.get_value(first, SimComponents.POSITION, SimComponents.X)
	var first_y := world.get_value(first, SimComponents.POSITION, SimComponents.Y)
	var second_x := world.get_value(second, SimComponents.POSITION, SimComponents.X)
	var second_y := world.get_value(second, SimComponents.POSITION, SimComponents.Y)
	var dx := Fixed.sub(second_x, first_x)
	var dy := Fixed.sub(second_y, first_y)
	var distance_sq := Fixed.add(Fixed.mul(dx, dx), Fixed.mul(dy, dy))
	return distance_sq <= GATHER_RANGE_SQ

static func _move_to_cell(world: SimWorld, id: int, target: int) -> void:
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_X, Fixed.to_int(world.get_value(target, SimComponents.POSITION, SimComponents.X)))
	world.set_value(id, SimComponents.MOVE, SimComponents.TARGET_Y, Fixed.to_int(world.get_value(target, SimComponents.POSITION, SimComponents.Y)))
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 1)

static func _stop(world: SimWorld, id: int) -> void:
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.X, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, 0)
