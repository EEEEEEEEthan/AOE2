extends TestBase

const KIND_VILLAGER := 1
const KIND_BUILDING := 8
const BUILDING_TYPE_TOWN_CENTER := 1


func run() -> void:
	_match_entry_starts_session()
	_demo_entry_spawns_units()
	_move_command_is_accepted()
	_train_command_spawns_unit()
	_market_endpoints_reach_lockstep()
	_tribute_endpoint_transfers_resource()


func _match_entry_starts_session() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalMatch()
	check_true(bridge.HasSession, "formal entry starts a match session")
	check_true(bridge.Snapshot().size() > 0, "formal entry spawns the starting base")
	check_true(bridge.GridWidth > 0 and bridge.GridHeight > 0, "formal entry keeps the map grid")
	bridge.queue_free()


func _demo_entry_spawns_units() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalDemo()
	check_true(bridge.Snapshot().size() > 0, "demo entry spawns units")
	bridge.queue_free()


func _move_command_is_accepted() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalMatch()
	var villager := _find_entity(bridge, KIND_VILLAGER)
	check_true(not villager.is_empty(), "starting villagers exist for command tests")
	if villager.is_empty():
		bridge.queue_free()
		return
	var before: int = bridge.AcceptedCommands
	bridge.MoveUnits(PackedInt64Array([int(villager["id"])]), int(villager["x"]) + 2, int(villager["y"]))
	bridge.FastForward(6)
	check_true(bridge.AcceptedCommands > before, "move command reaches the lockstep queue")
	bridge.queue_free()


func _train_command_spawns_unit() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalMatch()
	var town_center := _find_entity(bridge, KIND_BUILDING, BUILDING_TYPE_TOWN_CENTER)
	check_true(not town_center.is_empty(), "starting town center exists for training")
	if town_center.is_empty():
		bridge.queue_free()
		return
	var before: int = bridge.Snapshot().size()
	bridge.Train(int(town_center["id"]), -1)
	# 训练走生产队列：入队后需等计时结束才出生。
	bridge.FastForward(60)
	check_true(bridge.Snapshot().size() > before, "queued villager is produced after training time")
	bridge.queue_free()


func _market_endpoints_reach_lockstep() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartBuildingShowcase()
	check_true(bridge.HasCompletedMarket(), "showcase completes a market for the view team")
	var player := _my_player(bridge)
	check_true(player.has("market_wood"), "player state exposes market rates")
	check_true(not bridge.CanMarketBuy(0, 100), "buying without gold is blocked")
	var wood_before: int = player["wood"]
	var gold_before: int = player["gold"]
	var before: int = bridge.AcceptedCommands
	bridge.MarketSell(0, 100)
	bridge.FastForward(6)
	check_true(bridge.AcceptedCommands > before, "market sell reaches the lockstep queue")
	player = _my_player(bridge)
	check_true(int(player["wood"]) < wood_before, "market sell removes wood")
	check_true(int(player["gold"]) > gold_before, "market sell pays gold")
	bridge.queue_free()


func _tribute_endpoint_transfers_resource() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartBuildingShowcase()
	var mine := _my_player(bridge)
	var other := _other_player(bridge)
	var mine_wood: int = mine["wood"]
	var other_wood: int = other["wood"]
	var target := int(other["team"])
	check_true(bridge.CanTribute(target, 0, 100), "tribute is allowed to another player")
	bridge.Tribute(target, 0, 100)
	bridge.FastForward(6)
	mine = _my_player(bridge)
	other = _other_player(bridge)
	check_true(int(other["wood"]) == other_wood + 100, "tribute adds wood to target")
	check_true(int(mine["wood"]) == mine_wood - 100, "tribute removes wood from sender")
	bridge.queue_free()


func _my_player(bridge: GameBridge) -> Dictionary:
	for player: Dictionary in bridge.PlayerStates():
		if int(player["team"]) == bridge.MyTeam:
			return player
	return {}


func _other_player(bridge: GameBridge) -> Dictionary:
	for player: Dictionary in bridge.PlayerStates():
		if int(player["team"]) != bridge.MyTeam:
			return player
	return {}


func _find_entity(bridge: GameBridge, kind: int, building_type: int = -1) -> Dictionary:
	for entity: Dictionary in bridge.Snapshot():
		if int(entity["team"]) != bridge.MyTeam:
			continue
		if int(entity["kind"]) != kind:
			continue
		if building_type >= 0 and int(entity["building_type"]) != building_type:
			continue
		return entity
	return {}
