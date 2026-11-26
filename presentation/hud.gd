class_name GameHud
extends CanvasLayer

signal build_requested(building_type: int)
signal train_requested(unit_type: int)
signal research_requested(tech_id: int)
signal age_up_requested(target_age: int)
signal stop_requested
signal placement_cancelled
signal idle_villager_requested
signal town_bell_toggled(enabled: bool)
signal formation_requested(formation: int)
signal market_buy_requested(resource: int, amount: int)
signal market_sell_requested(resource: int, amount: int)
signal tribute_requested(target: int, resource: int, amount: int)
signal transport_unload_requested(ship_id: int)

const AGE_NAMES := ["黑暗时代", "封建时代", "城堡时代", "帝王时代"]
const FORMATION_NAMES := ["线列", "楔形", "方阵", "散开"]
const GAME_SPEEDS := [0.5, 1.0, 1.5, 2.0]
const NOTICE_LIMIT := 5
const TOOLTIP_OFFSET := Vector2(16.0, 16.0)
const TOOLTIP_MARGIN := 8.0
const KIND_BUILDING := 8
const BUILDING_STATE_FOUNDATION := 0
const BUILDING_STATE_COMPLETE := 1
const VILLAGER_UNIT := -1
const TRANSPORT_SHIP_UNIT := -2
const SWORDSMAN_UNIT := 0
const SPEARMAN_UNIT := 1
const ARCHER_UNIT := 2
const KNIGHT_UNIT := 3
const TOWN_CENTER := 1
const BARRACKS := 3
const MARKET := 6
const DOCK := 8
const KIND_TRANSPORT_SHIP := 11
const ARCHERY_RANGE := 15
const STABLE := 16
const RESOURCE_WOOD := 0
const RESOURCE_FOOD := 1
const RESOURCE_GOLD := 2
const RESOURCE_STONE := 3
const RESOURCE_NAMES := {0: "木材", 1: "食物", 2: "金币", 3: "石料"}
const TRADABLE_RESOURCES := [RESOURCE_WOOD, RESOURCE_FOOD, RESOURCE_STONE]
const TRIBUTE_RESOURCES := [RESOURCE_WOOD, RESOURCE_FOOD, RESOURCE_GOLD, RESOURCE_STONE]
const MAX_TRADE_AMOUNT := 10000
const SHIFT_BATCH := 100

var _building_costs: Dictionary = {}
var _unit_costs: Dictionary = {}
var _notice_serial := 0
var _town_bell_enabled := false
var _bridge: GameBridge
var _market_targets_signature := ""
var _selected_transport_id := -1


func _ready() -> void:
	%SelectionBox.hide()
	%PlacementHint.hide()
	%NoticeLabel.text = ""
	%TooltipPanel.hide()
	%MarketPanel.hide()
	for resource: int in TRIBUTE_RESOURCES:
		%MarketResourceOption.add_item(RESOURCE_NAMES[resource], resource)
	for row in %TradeRows.get_children():
		var resource := int(row.get_meta("market_resource", -1))
		row.get_child(3).pressed.connect(_on_market_buy_pressed.bind(resource))
		row.get_child(5).pressed.connect(_on_market_sell_pressed.bind(resource))
	%MarketTributeButton.pressed.connect(_on_tribute_pressed)


func setup(bridge: GameBridge) -> void:
	_bridge = bridge
	_building_costs.clear()
	for option: Dictionary in bridge.BuildingOptions():
		_building_costs[int(option["id"])] = option
	_unit_costs.clear()
	for option: Dictionary in bridge.UnitOptions():
		_unit_costs[int(option["id"])] = option
	_apply_build_tooltips()
	_apply_unit_tooltips()


func refresh(player: Dictionary, selection: Array, my_team: int) -> void:
	%ResourceLabel.text = "木 %d　食 %d　金 %d　石 %d" % [
		player["wood"], player["food"], player["gold"], player["stone"],
	]
	%PopulationLabel.text = "人口 %d / %d" % [player["population_used"], player["population_cap"]]
	var age: int = player["age"]
	%AgeLabel.text = AGE_NAMES[age] if age >= 0 and age < AGE_NAMES.size() else "未知时代"
	_update_build_buttons(player)
	_update_train_buttons(player, selection, my_team)
	_update_research_buttons(player)
	_update_market(player, selection, my_team)
	_update_transport_unload(selection, my_team)
	show_selection(selection)


func show_selection(selection: Array) -> void:
	if selection.is_empty():
		%SelectionTitle.text = "未选择单位"
		%SelectionDetail.text = "左键点选或拖拽框选己方单位\n右键下达移动、攻击或采集指令"
		return
	if selection.size() == 1:
		var entry: Dictionary = selection[0]
		%SelectionTitle.text = str(entry["name"])
		var lines: PackedStringArray = ["生命 %d / %d" % [entry["hp"], entry["max_hp"]]]
		if int(entry["kind"]) == KIND_BUILDING:
			lines.append(_building_state_text(entry))
		elif int(entry["kind"]) == KIND_TRANSPORT_SHIP:
			lines.append(_transport_crew_text(entry))
		%SelectionDetail.text = "\n".join(lines)
		return
	var counts: Dictionary = {}
	for entry: Dictionary in selection:
		var name: String = str(entry["name"])
		counts[name] = int(counts.get(name, 0)) + 1
	var parts: PackedStringArray = []
	for name: String in counts:
		parts.append("%s × %d" % [name, counts[name]])
	%SelectionTitle.text = "已选择 %d 个" % selection.size()
	%SelectionDetail.text = "　".join(parts)


func _building_state_text(entry: Dictionary) -> String:
	var state: int = entry["building_state"]
	if state == BUILDING_STATE_FOUNDATION:
		return "建造进度 %d / %d" % [entry["building_progress"], maxi(1, int(entry["building_max_progress"]))]
	return "已完工"


func _update_build_buttons(player: Dictionary) -> void:
	for child in %BuildGrid.get_children():
		var button := child as Button
		var building_type: int = button.get_meta("building_type", -1)
		button.disabled = not _can_afford(_building_costs.get(building_type, {}), player)


func _update_train_buttons(player: Dictionary, selection: Array, my_team: int) -> void:
	var building_type := -1
	var building_ready := false
	if selection.size() == 1:
		var entry: Dictionary = selection[0]
		if int(entry["kind"]) == KIND_BUILDING and int(entry["team"]) == my_team:
			building_type = int(entry["building_type"])
			building_ready = int(entry["building_state"]) != BUILDING_STATE_FOUNDATION
	for child in %TrainRow.get_children():
		var button := child as Button
		var unit_type: int = button.get_meta("unit_type", -999)
		var allowed := building_ready and _train_allowed(building_type, unit_type)
		button.disabled = not allowed or not _can_afford(_unit_costs.get(unit_type, {}), player)


func _train_allowed(building_type: int, unit_type: int) -> bool:
	if unit_type == VILLAGER_UNIT:
		return building_type == TOWN_CENTER
	if unit_type == TRANSPORT_SHIP_UNIT:
		return building_type == DOCK
	match building_type:
		BARRACKS:
			return unit_type == SWORDSMAN_UNIT or unit_type == SPEARMAN_UNIT
		ARCHERY_RANGE:
			return unit_type == ARCHER_UNIT
		STABLE:
			return unit_type == KNIGHT_UNIT
	return false


func _update_transport_unload(selection: Array, my_team: int) -> void:
	var button: Button = %UnloadButton
	_selected_transport_id = -1
	button.visible = false
	if selection.size() != 1:
		return
	var entry: Dictionary = selection[0]
	if int(entry["kind"]) != KIND_TRANSPORT_SHIP or int(entry["team"]) != my_team:
		return
	_selected_transport_id = int(entry["id"])
	button.visible = true
	button.text = "卸载 %d/%d" % [entry["transport_count"], entry["transport_capacity"]]
	button.disabled = not _bridge.CanTransportUnload(_selected_transport_id)


func _update_research_buttons(player: Dictionary) -> void:
	var age: int = player["age"]
	var research: int = player["research"]
	for child in %AgeRow.get_children():
		var button := child as Button
		button.disabled = int(button.get_meta("target_age", -1)) != age + 1
	for child in %TechRow.get_children():
		var button := child as Button
		var tech_id: int = button.get_meta("tech_id", -1)
		button.disabled = ((research >> tech_id) & 1) == 1


func _update_market(player: Dictionary, selection: Array, my_team: int) -> void:
	var market := _selected_market(selection, my_team)
	%MarketPanel.visible = not market.is_empty()
	if market.is_empty() or _bridge == null:
		return
	_ensure_market_targets(my_team)
	for row in %TradeRows.get_children():
		var resource := int(row.get_meta("market_resource", -1))
		var amount := int(row.get_child(2).value)
		row.get_child(1).text = "买价 %d 金/100" % _bridge.MarketBuyRate(resource)
		row.get_child(4).text = "卖价 %d 金/100" % _bridge.MarketSellRate(resource)
		var buy_button: Button = row.get_child(3)
		var sell_button: Button = row.get_child(5)
		buy_button.disabled = not _bridge.CanMarketBuy(resource, amount)
		sell_button.disabled = not _bridge.CanMarketSell(resource, amount)
		buy_button.tooltip_text = _market_trade_hint(resource, amount, true)
		sell_button.tooltip_text = _market_trade_hint(resource, amount, false)
	_update_tribute_button()


func _selected_market(selection: Array, my_team: int) -> Dictionary:
	if selection.size() != 1:
		return {}
	var entry: Dictionary = selection[0]
	if int(entry["kind"]) != KIND_BUILDING or int(entry["team"]) != my_team:
		return {}
	if int(entry.get("building_type", -1)) != MARKET:
		return {}
	if int(entry.get("building_state", -1)) != BUILDING_STATE_COMPLETE:
		return {}
	return entry


func _ensure_market_targets(my_team: int) -> void:
	var teams: Array = []
	for player: Dictionary in _bridge.PlayerStates():
		var team := int(player["team"])
		if team != my_team:
			teams.append(team)
	var signature := str(teams)
	if signature == _market_targets_signature:
		return
	_market_targets_signature = signature
	%MarketTargetOption.clear()
	for team: int in teams:
		%MarketTargetOption.add_item("玩家 %d" % (team + 1), team)
	if %MarketTargetOption.item_count > 0:
		%MarketTargetOption.select(0)


func _update_tribute_button() -> void:
	var button: Button = %MarketTributeButton
	if %MarketTargetOption.item_count == 0:
		button.disabled = true
		button.tooltip_text = "没有可进贡的玩家"
		return
	var target_index: int = %MarketTargetOption.selected
	if target_index < 0:
		button.disabled = true
		button.tooltip_text = "请选择进贡目标"
		return
	var target := int(%MarketTargetOption.get_item_id(target_index))
	var resource_index: int = %MarketResourceOption.selected
	var resource := int(%MarketResourceOption.get_item_id(resource_index))
	var amount := _trade_amount(%MarketTributeAmount)
	button.disabled = not _bridge.CanTribute(target, resource, amount)
	button.tooltip_text = "资源不足或目标无效" if button.disabled \
		else "向玩家 %d 进贡 %d %s" % [target + 1, amount, RESOURCE_NAMES[resource]]


func _market_trade_hint(resource: int, amount: int, buying: bool) -> String:
	var name: String = RESOURCE_NAMES[resource]
	if not _bridge.HasCompletedMarket():
		return "需要已完工市场"
	if buying:
		if not _bridge.CanMarketBuy(resource, amount):
			return "金币不足"
		return "花费 %d 金买入 %d %s" % [
			_bridge.MarketBuyRate(resource) * amount / 100, amount, name,
		]
	if not _bridge.CanMarketSell(resource, amount):
		return "%s不足" % name
	return "卖出 %d %s 获得 %d 金" % [amount, name, _bridge.MarketSellRate(resource) * amount / 100]


func _trade_amount(spin: SpinBox) -> int:
	var amount := int(spin.value)
	if Input.is_key_pressed(KEY_SHIFT):
		amount = mini(amount * SHIFT_BATCH, MAX_TRADE_AMOUNT)
	return maxi(amount, 1)


func _market_row(resource: int) -> Control:
	for row in %TradeRows.get_children():
		if int(row.get_meta("market_resource", -1)) == resource:
			return row
	return null


func _can_afford(cost: Dictionary, player: Dictionary) -> bool:
	if cost.is_empty():
		return false
	return player["wood"] >= int(cost["wood"]) \
		and player["food"] >= int(cost["food"]) \
		and player["gold"] >= int(cost["gold"]) \
		and player["stone"] >= int(cost["stone"])


func _apply_build_tooltips() -> void:
	for child in %BuildGrid.get_children():
		var button := child as Button
		var building_type: int = button.get_meta("building_type", -1)
		button.tooltip_text = _cost_text(_building_costs.get(building_type, {}))


func _apply_unit_tooltips() -> void:
	for child in %TrainRow.get_children():
		var button := child as Button
		var unit_type: int = button.get_meta("unit_type", -999)
		button.tooltip_text = _cost_text(_unit_costs.get(unit_type, {}))


func _cost_text(cost: Dictionary) -> String:
	if cost.is_empty():
		return ""
	var parts: PackedStringArray = []
	for key: String in ["wood", "food", "gold", "stone"]:
		if int(cost[key]) > 0:
			parts.append("%s %d" % [{"wood": "木", "food": "食", "gold": "金", "stone": "石"}[key], cost[key]])
	return "造价：%s" % "　".join(parts)


func set_placement_hint(building_name: String) -> void:
	%PlacementHint.text = "放置 %s：左键确认，右键或 Esc 取消" % building_name
	%PlacementHint.show()


func clear_placement_hint() -> void:
	%PlacementHint.hide()


func set_notice(text: String) -> void:
	%NoticeLabel.text = text
	_notice_serial += 1
	var serial := _notice_serial
	await get_tree().create_timer(2.5).timeout
	if serial == _notice_serial:
		%NoticeLabel.text = ""


func push_notification(text: String) -> void:
	var label := Label.new()
	label.text = text
	%NoticeFeed.add_child(label)
	while %NoticeFeed.get_child_count() > NOTICE_LIMIT:
		var oldest := %NoticeFeed.get_child(0)
		%NoticeFeed.remove_child(oldest)
		oldest.queue_free()


func push_chat_line(text: String) -> void:
	%ChatLog.add_text("%s\n" % text)
	%ChatLog.scroll_to_line(maxi(0, %ChatLog.get_line_count() - 1))


func show_unit_tooltip(entry: Dictionary, screen_position: Vector2) -> void:
	%TooltipTitle.text = str(entry["name"])
	%TooltipBody.text = _tooltip_body(entry)
	%TooltipPanel.show()
	var panel_size: Vector2 = %TooltipPanel.get_combined_minimum_size()
	var viewport_size := get_viewport().get_visible_rect().size
	var target := screen_position + TOOLTIP_OFFSET
	target.x = clampf(target.x, 0.0, viewport_size.x - panel_size.x - TOOLTIP_MARGIN)
	target.y = clampf(target.y, 0.0, viewport_size.y - panel_size.y - TOOLTIP_MARGIN)
	%TooltipPanel.position = target


func hide_unit_tooltip() -> void:
	%TooltipPanel.hide()


func open_pause_menu() -> void:
	%PauseMenu.open()


func close_pause_menu() -> void:
	%PauseMenu.close()


func _tooltip_body(entry: Dictionary) -> String:
	var lines: PackedStringArray = ["生命 %d / %d" % [entry["hp"], entry["max_hp"]]]
	if int(entry["kind"]) == KIND_BUILDING:
		lines.append(_building_state_text(entry))
	elif int(entry["kind"]) == KIND_TRANSPORT_SHIP:
		lines.append(_transport_crew_text(entry))
	return "\n".join(lines)


func _transport_crew_text(entry: Dictionary) -> String:
	return "乘员 %d / %d" % [entry["transport_count"], entry["transport_capacity"]]


func _on_chat_submitted(text: String) -> void:
	var trimmed := text.strip_edges()
	if trimmed.is_empty():
		return
	push_chat_line("[你] %s" % trimmed)
	%ChatInput.text = ""


func _on_chat_send_pressed() -> void:
	_on_chat_submitted(%ChatInput.text)
	%ChatInput.grab_focus()


func _on_chat_button_pressed() -> void:
	%ChatPanel.visible = not %ChatPanel.visible
	if %ChatPanel.visible:
		%ChatInput.grab_focus()


func _on_speed_selected(index: int) -> void:
	Engine.time_scale = GAME_SPEEDS[index]
	push_notification("游戏速度：%s" % %SpeedOption.get_item_text(index))


func _on_menu_pressed() -> void:
	open_pause_menu()


func _on_diplomacy_pressed() -> void:
	_show_unavailable("外交功能开发中，敬请期待。")


func _on_objectives_pressed() -> void:
	_show_unavailable("目标与剧情功能开发中，敬请期待。")


func _on_idle_villager_pressed() -> void:
	idle_villager_requested.emit()
	push_notification("暂无闲置村民")


func _on_town_bell_pressed() -> void:
	_town_bell_enabled = not _town_bell_enabled
	%TownBellButton.text = "解除警钟" if _town_bell_enabled else "城镇警钟"
	town_bell_toggled.emit(_town_bell_enabled)
	push_notification("城镇警钟：%s" % ("召集" if _town_bell_enabled else "解除"))


func _on_formation_pressed(formation: int) -> void:
	formation_requested.emit(formation)
	push_notification("阵型：%s" % FORMATION_NAMES[formation])


func _show_unavailable(message: String) -> void:
	%UnavailableDialog.dialog_text = message
	%UnavailableDialog.popup_centered()


func set_selection_box(rect: Rect2) -> void:
	%SelectionBox.position = rect.position
	%SelectionBox.size = rect.size
	%SelectionBox.show()


func hide_selection_box() -> void:
	%SelectionBox.hide()


func _on_build_button_pressed(building_type: int) -> void:
	build_requested.emit(building_type)


func _on_train_button_pressed(unit_type: int) -> void:
	train_requested.emit(unit_type)


func _on_tech_button_pressed(tech_id: int) -> void:
	research_requested.emit(tech_id)


func _on_age_button_pressed(target_age: int) -> void:
	age_up_requested.emit(target_age)


func _on_stop_pressed() -> void:
	stop_requested.emit()


func _on_unload_pressed() -> void:
	if _selected_transport_id >= 0:
		transport_unload_requested.emit(_selected_transport_id)


func _on_cancel_placement_pressed() -> void:
	placement_cancelled.emit()


func _on_market_buy_pressed(resource: int) -> void:
	var row := _market_row(resource)
	if row != null:
		market_buy_requested.emit(resource, _trade_amount(row.get_child(2)))


func _on_market_sell_pressed(resource: int) -> void:
	var row := _market_row(resource)
	if row != null:
		market_sell_requested.emit(resource, _trade_amount(row.get_child(2)))


func _on_tribute_pressed() -> void:
	var target_index: int = %MarketTargetOption.selected
	var resource_index: int = %MarketResourceOption.selected
	if target_index < 0 or resource_index < 0:
		return
	tribute_requested.emit(
		int(%MarketTargetOption.get_item_id(target_index)),
		int(%MarketResourceOption.get_item_id(resource_index)),
		_trade_amount(%MarketTributeAmount))
