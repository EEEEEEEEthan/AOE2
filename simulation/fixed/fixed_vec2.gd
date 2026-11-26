class_name FixedVec2
extends RefCounted

var x: int
var y: int

func _init(px: int = 0, py: int = 0) -> void:
	x = px
	y = py

static func from_tiles(tx: float, ty: float) -> FixedVec2:
	return FixedVec2.new(Fixed.from_float(tx), Fixed.from_float(ty))

func add(other: FixedVec2) -> FixedVec2:
	return FixedVec2.new(Fixed.add(x, other.x), Fixed.add(y, other.y))

func sub(other: FixedVec2) -> FixedVec2:
	return FixedVec2.new(Fixed.sub(x, other.x), Fixed.sub(y, other.y))

func scaled(factor: int) -> FixedVec2:
	return FixedVec2.new(Fixed.mul(x, factor), Fixed.mul(y, factor))

func length_sq() -> int:
	return Fixed.add(Fixed.mul(x, x), Fixed.mul(y, y))

func distance_sq(other: FixedVec2) -> int:
	var dx: int = Fixed.sub(x, other.x)
	var dy: int = Fixed.sub(y, other.y)
	return Fixed.add(Fixed.mul(dx, dx), Fixed.mul(dy, dy))

func to_vector2() -> Vector2:
	return Vector2(Fixed.to_float(x), Fixed.to_float(y))

func cell_x() -> int:
	return Fixed.to_int(x)

func cell_y() -> int:
	return Fixed.to_int(y)
