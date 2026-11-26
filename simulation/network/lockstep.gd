class_name Lockstep
extends RefCounted

var player_count: int
var ticks_per_turn: int
var turn := 0
var _turns: Dictionary = {}

func _init(p_player_count: int = 2, p_ticks_per_turn: int = 6) -> void:
	player_count = max(1, p_player_count)
	ticks_per_turn = max(1, p_ticks_per_turn)

func submit_turn(turn_index: int, player: int, commands: Array) -> bool:
	if player < 0 or player >= player_count or turn_index < turn:
		return false
	if not _turns.has(turn_index):
		_turns[turn_index] = {}
	var slots: Dictionary = _turns[turn_index]
	if slots.has(player):
		return false
	var copied := commands.duplicate()
	slots[player] = copied
	return true

func ready_players(turn_index: int) -> int:
	if not _turns.has(turn_index):
		return 0
	return _turns[turn_index].size()

func can_advance() -> bool:
	return ready_players(turn) == player_count

func take_commands() -> Array:
	var flattened: Array = []
	if not _turns.has(turn):
		return flattened
	var slots: Dictionary = _turns[turn]
	var players := slots.keys()
	players.sort()
	for player in players:
		var commands: Array = slots[player]
		flattened.append_array(commands)
	_turns.erase(turn)
	turn += 1
	return flattened

func pending_turns() -> int:
	return _turns.size()
