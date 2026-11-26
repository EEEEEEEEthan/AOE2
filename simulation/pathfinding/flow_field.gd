class_name FlowField
extends RefCounted

const NEIGHBORS := [
	Vector2i(1, 0),
	Vector2i(-1, 0),
	Vector2i(0, 1),
	Vector2i(0, -1),
	Vector2i(1, 1),
	Vector2i(1, -1),
	Vector2i(-1, 1),
	Vector2i(-1, -1),
]

var width: int
var height: int
var goal_x: int
var goal_y: int
var cost := PackedInt32Array()
var dir_x := PackedInt32Array()
var dir_y := PackedInt32Array()

func _init(grid: NavGrid) -> void:
	width = grid.width
	height = grid.height
	cost.resize(width * height)
	cost.fill(-1)
	dir_x.resize(width * height)
	dir_x.fill(0)
	dir_y.resize(width * height)
	dir_y.fill(0)

func compute(grid: NavGrid, target_x: int, target_y: int) -> bool:
	goal_x = target_x
	goal_y = target_y
	cost.fill(-1)
	dir_x.fill(0)
	dir_y.fill(0)
	if not grid.in_bounds(target_x, target_y) or grid.is_blocked(target_x, target_y):
		return false
	var start := grid.index(target_x, target_y)
	cost[start] = 0
	var queue := PackedInt32Array()
	queue.append(start)
	var head := 0
	while head < queue.size():
		var current := queue[head]
		head += 1
		var cx := current % width
		var cy := current / width
		var next_cost := cost[current] + 1
		for offset: Vector2i in NEIGHBORS:
			var nx: int = cx + offset.x
			var ny: int = cy + offset.y
			if not grid.in_bounds(nx, ny) or grid.is_blocked(nx, ny):
				continue
			if offset.x != 0 and offset.y != 0:
				if grid.is_blocked(cx + offset.x, cy) or grid.is_blocked(cx, cy + offset.y):
					continue
			var neighbor := grid.index(nx, ny)
			if cost[neighbor] != -1:
				continue
			cost[neighbor] = next_cost
			queue.append(neighbor)
	_build_directions()
	return true

func direction_at(x: int, y: int) -> Vector2i:
	if x < 0 or y < 0 or x >= width or y >= height:
		return Vector2i.ZERO
	var index := y * width + x
	return Vector2i(dir_x[index], dir_y[index])

func cost_at(x: int, y: int) -> int:
	if x < 0 or y < 0 or x >= width or y >= height:
		return -1
	return cost[y * width + x]

func reachable(x: int, y: int) -> bool:
	return cost_at(x, y) >= 0

func _build_directions() -> void:
	for y in height:
		for x in width:
			var index := y * width + x
			var current_cost := cost[index]
			if current_cost <= 0:
				continue
			var best_offset := Vector2i.ZERO
			var best_cost := current_cost
			for offset: Vector2i in NEIGHBORS:
				var nx: int = x + offset.x
				var ny: int = y + offset.y
				if nx < 0 or ny < 0 or nx >= width or ny >= height:
					continue
				var neighbor_cost := cost[ny * width + nx]
				if neighbor_cost < 0 or neighbor_cost >= best_cost:
					continue
				best_cost = neighbor_cost
				best_offset = offset
			dir_x[index] = best_offset.x
			dir_y[index] = best_offset.y
