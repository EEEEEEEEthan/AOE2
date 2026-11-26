extends TestBase

func run() -> void:
	_turn_waits_for_all_players()
	_commands_apply_in_player_order()
	_command_roundtrips()
	_lockstep_simulations_stay_identical()

func _turn_waits_for_all_players() -> void:
	var lock := Lockstep.new(2, 6)
	var first := LockstepCommand.new(0, LockstepCommand.Kind.MOVE, 1, 4, 4)
	check_true(lock.submit_turn(0, 0, [first]), "first player accepted")
	check_true(not lock.can_advance(), "cannot advance with one player")
	check_true(not lock.submit_turn(0, 0, [first]), "duplicate submission rejected")
	check_true(lock.submit_turn(0, 1, []), "second player accepted")
	check_true(lock.can_advance(), "turn advances once all players ready")
	check_eq(lock.ready_players(0), 2, "two players ready for turn zero")

func _commands_apply_in_player_order() -> void:
	var lock := Lockstep.new(2, 6)
	var late := LockstepCommand.new(1, LockstepCommand.Kind.MOVE, 9, 1, 1)
	var early := LockstepCommand.new(0, LockstepCommand.Kind.STOP, 9, 0, 0)
	lock.submit_turn(0, 1, [late])
	lock.submit_turn(0, 0, [early])
	var ordered := lock.take_commands()
	check_eq(ordered.size(), 2, "both commands collected")
	check_eq(ordered[0].player, 0, "player zero command comes first")
	check_eq(ordered[1].player, 1, "player one command comes second")
	check_eq(lock.turn, 1, "turn index advanced")

func _command_roundtrips() -> void:
	var original := LockstepCommand.new(1, LockstepCommand.Kind.MOVE, 42, 7, 9)
	var restored := LockstepCommand.from_array(original.to_array())
	check_eq(restored.player, 1, "player survives serialization")
	check_eq(restored.kind, LockstepCommand.Kind.MOVE, "kind survives serialization")
	check_eq(restored.entity, 42, "entity survives serialization")
	check_eq(restored.cell_x, 7, "cell x survives serialization")
	check_eq(restored.cell_y, 9, "cell y survives serialization")

func _lockstep_simulations_stay_identical() -> void:
	var first := GameSim.new(NavGrid.new(24, 24), 31, 2, 6)
	var second := GameSim.new(NavGrid.new(24, 24), 31, 2, 6)
	_populate(first)
	_populate(second)
	var commands_a := _commands_for(first)
	var commands_b := _commands_for(second)
	first.submit_turn(0, commands_a[0])
	second.submit_turn(1, commands_b[1])
	check_eq(first.world.checksum(), second.world.checksum(), "no simulation happens before all players ready")
	first.submit_turn(1, commands_a[1])
	second.submit_turn(0, commands_b[0])
	check_eq(first.simulate_turn(), second.simulate_turn(), "turn checksum identical across peers")

func _populate(sim: GameSim) -> void:
	for i in 6:
		sim.spawn_unit(3 + i, 4, 0, 50, 6, 1.5, 0.2)
		sim.spawn_unit(3 + i, 16, 1, 50, 6, 1.5, 0.2)

func _commands_for(sim: GameSim) -> Array:
	var player_zero: Array = []
	var player_one: Array = []
	for id in sim.world.entities():
		var team := sim.world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
		if team == 0:
			player_zero.append(LockstepCommand.new(0, LockstepCommand.Kind.MOVE, id, 6, 10))
		else:
			player_one.append(LockstepCommand.new(1, LockstepCommand.Kind.MOVE, id, 6, 10))
	return [player_zero, player_one]
