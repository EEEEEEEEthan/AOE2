class_name SelectionModel
extends RefCounted

var ids: Array[int] = []

var _groups: Dictionary = {}


func clear() -> void:
	ids.clear()


func set_ids(values: Array) -> void:
	ids.clear()
	for value in values:
		add_id(int(value))


func add_id(value: int) -> void:
	if not ids.has(value):
		ids.append(value)


func has_id(value: int) -> bool:
	return ids.has(value)


func prune(alive: Dictionary) -> void:
	var kept: Array[int] = []
	for id in ids:
		if alive.has(id):
			kept.append(id)
	ids = kept


func assign_group(number: int, group_ids: Array) -> void:
	_groups[number] = group_ids.duplicate()


func group_ids(number: int) -> Array[int]:
	var result: Array[int] = []
	if _groups.has(number):
		result.assign(_groups[number])
	return result


func group_assignments() -> Dictionary:
	var result: Dictionary = {}
	for number in _groups:
		for entity_id in _groups[number]:
			result[int(entity_id)] = int(number)
	return result
