class_name ComponentStore
extends RefCounted

var _columns: Dictionary = {}
var _active := PackedByteArray()
var count := 0

func has(id: int) -> bool:
	return id >= 0 and id < _active.size() and _active[id] == 1

func add(id: int) -> void:
	_ensure_capacity(id)
	if _active[id] == 0:
		_active[id] = 1
		count += 1

func remove(id: int) -> void:
	if has(id):
		_active[id] = 0
		count -= 1

func set_value(id: int, column: StringName, value: int) -> void:
	_ensure_capacity(id)
	if not _columns.has(column):
		var values := PackedInt64Array()
		values.resize(_active.size())
		_columns[column] = values
	var values: PackedInt64Array = _columns[column]
	if values.size() <= id:
		values.resize(id + 1)
	values[id] = value
	_columns[column] = values

func get_value(id: int, column: StringName) -> int:
	if not _columns.has(column):
		return 0
	var values: PackedInt64Array = _columns[column]
	if id < 0 or id >= values.size():
		return 0
	return values[id]

func columns_sorted() -> Array:
	var texts: Array[String] = []
	for column: StringName in _columns.keys():
		texts.append(String(column))
	texts.sort()
	return texts

func column_values(column: StringName) -> PackedInt64Array:
	return _columns.get(column, PackedInt64Array())

func _ensure_capacity(id: int) -> void:
	if id < 0:
		return
	if _active.size() <= id:
		_active.resize(id + 1)
		for column: StringName in _columns.keys():
			var values: PackedInt64Array = _columns[column]
			values.resize(id + 1)
			_columns[column] = values
