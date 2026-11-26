class_name LockstepCommand
extends RefCounted

enum Kind {
	MOVE = 0,
	ATTACK = 1,
	STOP = 2,
	GATHER = 3,
}

var player: int
var kind: int
var entity: int
var cell_x: int
var cell_y: int
var target_entity: int
var drop_entity: int

func _init(p_player: int = 0, p_kind: int = Kind.MOVE, p_entity: int = -1, p_cell_x: int = 0, p_cell_y: int = 0, p_target_entity: int = -1, p_drop_entity: int = -1) -> void:
	player = p_player
	kind = p_kind
	entity = p_entity
	cell_x = p_cell_x
	cell_y = p_cell_y
	target_entity = p_target_entity
	drop_entity = p_drop_entity

func to_array() -> PackedInt32Array:
	return PackedInt32Array([player, kind, entity, cell_x, cell_y, target_entity, drop_entity])

static func from_array(values: PackedInt32Array) -> LockstepCommand:
	var command := LockstepCommand.new()
	if values.size() >= 1:
		command.player = values[0]
	if values.size() >= 2:
		command.kind = values[1]
	if values.size() >= 3:
		command.entity = values[2]
	if values.size() >= 4:
		command.cell_x = values[3]
	if values.size() >= 5:
		command.cell_y = values[4]
	if values.size() >= 6:
		command.target_entity = values[5]
	if values.size() >= 7:
		command.drop_entity = values[6]
	return command
