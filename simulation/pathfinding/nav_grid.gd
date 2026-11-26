class_name NavGrid
extends RefCounted

const COST_BLOCKED := 0xFF

var width: int
var height: int
var blocked := PackedByteArray()

func _init(grid_width: int, grid_height: int) -> void:
	width = grid_width
	height = grid_height
	blocked.resize(width * height)
	blocked.fill(0)

func in_bounds(x: int, y: int) -> bool:
	return x >= 0 and y >= 0 and x < width and y < height

func index(x: int, y: int) -> int:
	return y * width + x

func is_blocked(x: int, y: int) -> bool:
	if not in_bounds(x, y):
		return true
	return blocked[index(x, y)] == COST_BLOCKED

func set_blocked(x: int, y: int, value: bool) -> void:
	if in_bounds(x, y):
		blocked[index(x, y)] = COST_BLOCKED if value else 0

func set_blocked_rect(x: int, y: int, rect_width: int, rect_height: int) -> void:
	for cell_y in range(y, y + rect_height):
		for cell_x in range(x, x + rect_width):
			set_blocked(cell_x, cell_y, true)

func clear_blocked() -> void:
	blocked.fill(0)

func clone() -> NavGrid:
	var other := NavGrid.new(width, height)
	other.blocked = blocked.duplicate()
	return other
