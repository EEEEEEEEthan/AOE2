class_name SimWorld
extends RefCounted

const FNV_OFFSET := 0x811C_9DC5
const FNV_PRIME := 0x0100_0193
const MASK32 := 0xFFFF_FFFF

var tick := 0
var next_entity := 0
var rng: SimRng
var _alive := PackedByteArray()
var _stores: Dictionary = {}
var _alive_cache := PackedInt64Array()
var _cache_dirty := true

func _init(seed_value: int = 1) -> void:
	rng = SimRng.new(seed_value)

func spawn() -> int:
	var id := next_entity
	next_entity += 1
	if _alive.size() <= id:
		_alive.resize(id + 1)
	_alive[id] = 1
	_cache_dirty = true
	return id

func despawn(id: int) -> void:
	if not is_alive(id):
		return
	_alive[id] = 0
	_cache_dirty = true
	for name: StringName in _stores.keys():
		var store: ComponentStore = _stores[name]
		store.remove(id)

func is_alive(id: int) -> bool:
	return id >= 0 and id < _alive.size() and _alive[id] == 1

func entities() -> PackedInt64Array:
	if _cache_dirty:
		var result := PackedInt64Array()
		for id in _alive.size():
			if _alive[id] == 1:
				result.append(id)
		_alive_cache = result
		_cache_dirty = false
	return _alive_cache

func store(name: StringName) -> ComponentStore:
	if not _stores.has(name):
		_stores[name] = ComponentStore.new()
	return _stores[name]

func attach(id: int, component: StringName) -> void:
	store(component).add(id)

func detach(id: int, component: StringName) -> void:
	store(component).remove(id)

func set_value(id: int, component: StringName, column: StringName, value: int) -> void:
	store(component).set_value(id, column, value)

func get_value(id: int, component: StringName, column: StringName) -> int:
	return store(component).get_value(id, column)

func has_component(id: int, component: StringName) -> bool:
	return _stores.has(component) and _stores[component].has(id)

func component_names_sorted() -> Array:
	var names := _stores.keys()
	names.sort()
	return names

func checksum() -> int:
	var hash := FNV_OFFSET
	hash = _hash_int(hash, tick)
	for id in entities():
		hash = _hash_int(hash, id)
		for component_name: StringName in component_names_sorted():
			var store: ComponentStore = _stores[component_name]
			if not store.has(id):
				continue
			hash = _hash_int(hash, String(component_name).hash())
			for column: StringName in store.columns_sorted():
				hash = _hash_int(hash, String(column).hash())
				hash = _hash_int(hash, store.get_value(id, column))
	return hash

func _hash_int(hash: int, value: int) -> int:
	var bytes := value & MASK32
	for shift in range(0, 64, 8):
		hash = (hash ^ ((bytes >> shift) & 0xFF)) & MASK32
		hash = (hash * FNV_PRIME) & MASK32
	return hash
