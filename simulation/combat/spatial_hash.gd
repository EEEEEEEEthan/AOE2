class_name SpatialHash
extends RefCounted

var cell_size: int
var _buckets: Dictionary = {}

func _init(cell_tiles: int = 4) -> void:
	cell_size = cell_tiles * Fixed.SCALE

func clear() -> void:
	_buckets.clear()

func insert(id: int, x_fixed: int, y_fixed: int) -> void:
	var key := _key(_coordinate(x_fixed), _coordinate(y_fixed))
	if not _buckets.has(key):
		_buckets[key] = PackedInt64Array()
	var bucket: PackedInt64Array = _buckets[key]
	bucket.append(id)
	_buckets[key] = bucket

func rebuild(world: SimWorld) -> void:
	clear()
	for id in world.entities():
		if not world.has_component(id, SimComponents.POSITION):
			continue
		insert(id, world.get_value(id, SimComponents.POSITION, SimComponents.X), world.get_value(id, SimComponents.POSITION, SimComponents.Y))

func query_radius(x_fixed: int, y_fixed: int, radius_fixed: int) -> PackedInt64Array:
	var result := PackedInt64Array()
	if radius_fixed < 0:
		return result
	var min_x := _coordinate(x_fixed - radius_fixed)
	var max_x := _coordinate(x_fixed + radius_fixed)
	var min_y := _coordinate(y_fixed - radius_fixed)
	var max_y := _coordinate(y_fixed + radius_fixed)
	for cy in range(min_y, max_y + 1):
		for cx in range(min_x, max_x + 1):
			var key := _key(cx, cy)
			if not _buckets.has(key):
				continue
			var bucket: PackedInt64Array = _buckets[key]
			for id in bucket:
				result.append(id)
	return result

func _coordinate(value: int) -> int:
	var result := value / cell_size
	if value < 0 and value % cell_size != 0:
		result -= 1
	return result

func _key(cx: int, cy: int) -> int:
	return (cx << 32) ^ (cy & 0xFFFF_FFFF)
