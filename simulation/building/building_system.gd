class_name BuildingSystem
extends RefCounted
const STATE_FOUNDATION := 0
const STATE_COMPLETE := 1
const STATE_RUINS := 2
const CONSTRUCT_ACTIVE := 0
const CONSTRUCT_DONE := 1
static func can_place(grid: NavGrid, type: int, origin_x: int, origin_y: int) -> bool:
	if not BuildingTypes.is_known(type):
		return false
	var size_w := BuildingTypes.size_width(type)
	var size_h := BuildingTypes.size_height(type)
	for cell_y in range(origin_y, origin_y + size_h):
		for cell_x in range(origin_x, origin_x + size_w):
			if not grid.in_bounds(cell_x, cell_y):
				return false
			if grid.is_terrain_blocked(cell_x, cell_y):
				return false
			if grid.is_building_blocked(cell_x, cell_y):
				return false
	return true
static func place(world: SimWorld, grid: NavGrid, type: int, origin_x: int, origin_y: int, team: int) -> int:
	if not can_place(grid, type, origin_x, origin_y):
		return -1
	var id := world.spawn()
	world.attach(id, SimComponents.POSITION)
	world.attach(id, SimComponents.HEALTH)
	world.attach(id, SimComponents.TEAM)
	world.attach(id, SimComponents.BUILDING)
	world.set_value(id, SimComponents.POSITION, SimComponents.X, Fixed.from_int(origin_x))
	world.set_value(id, SimComponents.POSITION, SimComponents.Y, Fixed.from_int(origin_y))
	world.set_value(id, SimComponents.HEALTH, SimComponents.HP, BuildingTypes.max_hp(type))
	world.set_value(id, SimComponents.HEALTH, SimComponents.MAX_HP, BuildingTypes.max_hp(type))
	world.set_value(id, SimComponents.TEAM, SimComponents.TEAM_ID, team)
	world.set_value(id, SimComponents.BUILDING, SimComponents.B_TYPE, type)
	world.set_value(id, SimComponents.BUILDING, SimComponents.SIZE_W, BuildingTypes.size_width(type))
	world.set_value(id, SimComponents.BUILDING, SimComponents.SIZE_H, BuildingTypes.size_height(type))
	world.set_value(id, SimComponents.BUILDING, SimComponents.PROGRESS, 0)
	world.set_value(id, SimComponents.BUILDING, SimComponents.MAX_PROGRESS, BuildingTypes.build_ticks(type))
	world.set_value(id, SimComponents.BUILDING, SimComponents.STATE, STATE_FOUNDATION)
	_set_block(world, grid, id, true)
	return id
static func step(world: SimWorld, grid: NavGrid) -> void:
	var construct_store := world.store(SimComponents.CONSTRUCT)
	if construct_store.count == 0:
		return
	for id in world.entities():
		if not construct_store.has(id):
			continue
		_step_worker(world, grid, id)
static func ruin(world: SimWorld, grid: NavGrid, id: int) -> void:
	if not world.has_component(id, SimComponents.BUILDING):
		return
	world.set_value(id, SimComponents.BUILDING, SimComponents.STATE, STATE_RUINS)
	world.set_value(id, SimComponents.HEALTH, SimComponents.HP, 0)
	world.detach(id, SimComponents.HEALTH)
	_set_block(world, grid, id, false)
static func _step_worker(world: SimWorld, grid: NavGrid, id: int) -> void:
	var building_id := world.get_value(id, SimComponents.CONSTRUCT, SimComponents.TARGET_BASE)
	if not _is_constructable(world, building_id):
		world.detach(id, SimComponents.CONSTRUCT)
		return
	if not world.has_component(id, SimComponents.POSITION):
		return
	var origin_x := Fixed.to_int(world.get_value(building_id, SimComponents.POSITION, SimComponents.X))
	var origin_y := Fixed.to_int(world.get_value(building_id, SimComponents.POSITION, SimComponents.Y))
	var size_w := world.get_value(building_id, SimComponents.BUILDING, SimComponents.SIZE_W)
	var size_h := world.get_value(building_id, SimComponents.BUILDING, SimComponents.SIZE_H)
	var worker_x := Fixed.to_int(world.get_value(id, SimComponents.POSITION, SimComponents.X))
	var worker_y := Fixed.to_int(world.get_value(id, SimComponents.POSITION, SimComponents.Y))
	if _is_adjacent(worker_x, worker_y, origin_x, origin_y, size_w, size_h):
		_stop_worker(world, id)
		_advance_progress(world, id, building_id)
	else:
		_approach(world, grid, id, worker_x, worker_y, origin_x, origin_y, size_w, size_h)
static func _is_constructable(world: SimWorld, building_id: int) -> bool:
	if not world.is_alive(building_id) or not world.has_component(building_id, SimComponents.BUILDING):
		return false
	return world.get_value(building_id, SimComponents.BUILDING, SimComponents.STATE) == STATE_FOUNDATION
static func _is_adjacent(worker_x: int, worker_y: int, origin_x: int, origin_y: int, size_w: int, size_h: int) -> bool:
	var near_x := clampi(worker_x, origin_x, origin_x + size_w - 1)
	var near_y := clampi(worker_y, origin_y, origin_y + size_h - 1)
	var distance_x := absi(worker_x - near_x)
	var distance_y := absi(worker_y - near_y)
	if distance_x == 0 and distance_y == 0:
		return false
	return distance_x <= 1 and distance_y <= 1
static func _advance_progress(world: SimWorld, worker_id: int, building_id: int) -> void:
	var progress := world.get_value(building_id, SimComponents.BUILDING, SimComponents.PROGRESS) + 1
	var max_progress := world.get_value(building_id, SimComponents.BUILDING, SimComponents.MAX_PROGRESS)
	if progress >= max_progress:
		world.set_value(building_id, SimComponents.BUILDING, SimComponents.PROGRESS, max_progress)
		world.set_value(building_id, SimComponents.BUILDING, SimComponents.STATE, STATE_COMPLETE)
		world.detach(worker_id, SimComponents.CONSTRUCT)
		return
	world.set_value(building_id, SimComponents.BUILDING, SimComponents.PROGRESS, progress)
static func _approach(world: SimWorld, grid: NavGrid, worker_id: int, worker_x: int, worker_y: int, origin_x: int, origin_y: int, size_w: int, size_h: int) -> void:
	if not world.has_component(worker_id, SimComponents.MOVE):
		return
	var approach_cell := _approach_cell(grid, worker_x, worker_y, origin_x, origin_y, size_w, size_h)
	if approach_cell.x < 0:
		return
	world.set_value(worker_id, SimComponents.MOVE, SimComponents.TARGET_X, approach_cell.x)
	world.set_value(worker_id, SimComponents.MOVE, SimComponents.TARGET_Y, approach_cell.y)
	world.set_value(worker_id, SimComponents.MOVE, SimComponents.ACTIVE, 1)
static func _approach_cell(grid: NavGrid, worker_x: int, worker_y: int, origin_x: int, origin_y: int, size_w: int, size_h: int) -> Vector2i:
	var best := Vector2i(-1, -1)
	var best_distance := 0
	for cell_y in range(origin_y - 1, origin_y + size_h + 1):
		for cell_x in range(origin_x - 1, origin_x + size_w + 1):
			if _inside(cell_x, cell_y, origin_x, origin_y, size_w, size_h):
				continue
			if not grid.in_bounds(cell_x, cell_y) or grid.is_blocked(cell_x, cell_y):
				continue
			var distance := absi(cell_x - worker_x) + absi(cell_y - worker_y)
			if best.x < 0 or distance < best_distance or (distance == best_distance and (cell_x < best.x or (cell_x == best.x and cell_y < best.y))):
				best = Vector2i(cell_x, cell_y)
				best_distance = distance
	return best
static func _inside(cell_x: int, cell_y: int, origin_x: int, origin_y: int, size_w: int, size_h: int) -> bool:
	return cell_x >= origin_x and cell_x < origin_x + size_w and cell_y >= origin_y and cell_y < origin_y + size_h
static func _stop_worker(world: SimWorld, id: int) -> void:
	world.set_value(id, SimComponents.MOVE, SimComponents.ACTIVE, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.X, 0)
	world.set_value(id, SimComponents.VELOCITY, SimComponents.Y, 0)
static func _set_block(world: SimWorld, grid: NavGrid, id: int, value: bool) -> void:
	var origin_x := Fixed.to_int(world.get_value(id, SimComponents.POSITION, SimComponents.X))
	var origin_y := Fixed.to_int(world.get_value(id, SimComponents.POSITION, SimComponents.Y))
	var size_w := world.get_value(id, SimComponents.BUILDING, SimComponents.SIZE_W)
	var size_h := world.get_value(id, SimComponents.BUILDING, SimComponents.SIZE_H)
	grid.set_building_blocked_rect(origin_x, origin_y, size_w, size_h, value)
