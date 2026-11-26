class_name MovementSystem
extends RefCounted

const DIAGONAL_SCALE := 46341

static func step(world: SimWorld, grid: NavGrid, field_provider: Callable) -> void:
	var move_store := world.store(SimComponents.MOVE)
	if move_store.count == 0:
		return
	for id in world.entities():
		if not move_store.has(id):
			continue
		if move_store.get_value(id, SimComponents.ACTIVE) == 0:
			continue
		_step_entity(world, id, grid, field_provider)

static func _step_entity(world: SimWorld, id: int, grid: NavGrid, field_provider: Callable) -> void:
	var x := world.get_value(id, SimComponents.POSITION, SimComponents.X)
	var y := world.get_value(id, SimComponents.POSITION, SimComponents.Y)
	var target_x := int(world.get_value(id, SimComponents.MOVE, SimComponents.TARGET_X))
	var target_y := int(world.get_value(id, SimComponents.MOVE, SimComponents.TARGET_Y))
	var speed := world.get_value(id, SimComponents.MOVE, SimComponents.SPEED)
	var cell_x := Fixed.to_int(x)
	var cell_y := Fixed.to_int(y)
	if cell_x == target_x and cell_y == target_y:
		_stop(world, id)
		return
	var field: FlowField = field_provider.call(target_x, target_y)
	if field == null or not field.reachable(cell_x, cell_y):
		_stop(world, id)
		return
	var direction := field.direction_at(cell_x, cell_y)
	if direction == Vector2i.ZERO:
		_stop(world, id)
		return
	var target_center_x := Fixed.add(Fixed.from_int(target_x), Fixed.HALF)
	var target_center_y := Fixed.add(Fixed.from_int(target_y), Fixed.HALF)
	var dx := Fixed.sub(target_center_x, x)
	var dy := Fixed.sub(target_center_y, y)
	if Fixed.abs(dx) <= speed and Fixed.abs(dy) <= speed:
		world.set_value(id, SimComponents.POSITION, SimComponents.X, target_center_x)
		world.set_value(id, SimComponents.POSITION, SimComponents.Y, target_center_y)
		world.set_value(id, SimComponents.VELOCITY, SimComponents.X, 0)
		world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, 0)
		_stop(world, id)
		return
	var velocity_x := 0
	var velocity_y := 0
	if direction.x != 0 and direction.y != 0:
		var diagonal_speed := Fixed.mul(speed, DIAGONAL_SCALE)
		velocity_x = direction.x * diagonal_speed
		velocity_y = direction.y * diagonal_speed
	else:
		velocity_x = direction.x * speed
		velocity_y = direction.y * speed
	world.set_value(id, SimComponents.VELOCITY, SimComponents.X, velocity_x)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, velocity_y)
	world.set_value(id, SimComponents.POSITION, SimComponents.X, Fixed.add(x, velocity_x))
	world.set_value(id, SimComponents.POSITION, SimComponents.Y, Fixed.add(y, velocity_y))

static func _stop(world: SimWorld, id: int) -> void:
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.X, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, 0)
