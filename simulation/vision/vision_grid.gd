class_name VisionGrid
extends RefCounted

const STATE_UNEXPLORED := 0
const STATE_EXPLORED := 1
const STATE_VISIBLE := 2

const FNV_OFFSET := 0x811C_9DC5
const FNV_PRIME := 0x0100_0193
const MASK32 := 0xFFFF_FFFF

var width: int
var height: int
var _visible := PackedByteArray()
var _explored := PackedByteArray()

func _init(grid_width: int, grid_height: int) -> void:
	width = grid_width
	height = grid_height
	_visible.resize(width * height)
	_visible.fill(0)
	_explored.resize(width * height)
	_explored.fill(0)

func in_bounds(x: int, y: int) -> bool:
	return x >= 0 and y >= 0 and x < width and y < height

func index(x: int, y: int) -> int:
	return y * width + x

func clear_visible() -> void:
	_visible.fill(0)

func is_explored(x: int, y: int) -> bool:
	return in_bounds(x, y) and _explored[index(x, y)] == 1

func visible_at(x: int, y: int) -> bool:
	return in_bounds(x, y) and _visible[index(x, y)] == 1

func state_at(x: int, y: int) -> int:
	if not in_bounds(x, y):
		return STATE_UNEXPLORED
	var cell := index(x, y)
	if _visible[cell] == 1:
		return STATE_VISIBLE
	if _explored[cell] == 1:
		return STATE_EXPLORED
	return STATE_UNEXPLORED

func reveal_circle(center_x: int, center_y: int, radius: int) -> void:
	if radius <= 0:
		return
	var radius_sq := Fixed.mul(radius, radius)
	var min_x := clampi(Fixed.to_int(Fixed.sub(center_x, radius)), 0, width - 1)
	var max_x := clampi(Fixed.to_int(Fixed.add(center_x, radius)), 0, width - 1)
	var min_y := clampi(Fixed.to_int(Fixed.sub(center_y, radius)), 0, height - 1)
	var max_y := clampi(Fixed.to_int(Fixed.add(center_y, radius)), 0, height - 1)
	for tile_y in range(min_y, max_y + 1):
		for tile_x in range(min_x, max_x + 1):
			var dx := Fixed.sub(Fixed.add(Fixed.from_int(tile_x), Fixed.HALF), center_x)
			var dy := Fixed.sub(Fixed.add(Fixed.from_int(tile_y), Fixed.HALF), center_y)
			var distance := Fixed.add(Fixed.mul(dx, dx), Fixed.mul(dy, dy))
			if distance > radius_sq:
				continue
			var cell := index(tile_x, tile_y)
			_visible[cell] = 1
			_explored[cell] = 1

func checksum() -> int:
	var hash := FNV_OFFSET
	for cell in _visible.size():
		hash = ((hash ^ _visible[cell]) * FNV_PRIME) & MASK32
	for cell in _explored.size():
		hash = ((hash ^ _explored[cell]) * FNV_PRIME) & MASK32
	return hash
