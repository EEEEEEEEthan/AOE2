class_name BattleController
extends Node

const KIND_UNIT := 0
const KIND_VILLAGER := 1
const KIND_RESOURCE_NODE := 2
const KIND_MONK := 5
const KIND_BUILDING := 8
const KIND_TRADE_CART := 9
const KIND_HUNTING_ANIMAL := 10
const BUILDING_STATE_COMPLETE := 1
const SELECTABLE_KINDS := [KIND_UNIT, KIND_VILLAGER, KIND_MONK, KIND_BUILDING, KIND_TRADE_CART]
const TARGETABLE_KINDS := [KIND_UNIT, KIND_VILLAGER, KIND_RESOURCE_NODE, KIND_MONK, KIND_BUILDING, KIND_TRADE_CART, KIND_HUNTING_ANIMAL]
const CLICK_RADIUS := 28.0
const DRAG_THRESHOLD := 6.0
const DOUBLE_CLICK_MS := 350
const MARKER_SECONDS := 0.45

var bridge: GameBridge
var world_view: WorldView
var camera: IsoCamera
var hud: GameHud
var minimap: Minimap
var selection := SelectionModel.new()

var _entities: Array[Dictionary] = []
var _entity_by_id: Dictionary = {}
var _player: Dictionary = {}
var _name_cache: Dictionary = {}
var _my_team := 0
var _dragging := false
var _drag_origin := Vector2.ZERO
var _last_click_id := -1
var _last_click_msec := 0
var _placing_type := -1
var _hover_cell := Vector2i(-1, -1)
var _group_last_press: Dictionary = {}


func setup(value: GameBridge) -> void:
	bridge = value
	world_view = %WorldView
	camera = %Camera3D
	hud = %Hud
	minimap = %Minimap
	_my_team = bridge.MyTeam
	hud.setup(bridge)
	hud.build_requested.connect(_start_placement)
	hud.train_requested.connect(_train_unit)
	hud.research_requested.connect(_research_tech)
	hud.age_up_requested.connect(_age_up)
	hud.stop_requested.connect(_stop_units)
	hud.placement_cancelled.connect(_cancel_placement)
	hud.market_buy_requested.connect(_market_buy)
	hud.market_sell_requested.connect(_market_sell)
	hud.tribute_requested.connect(_tribute)
	minimap.setup(camera)
	minimap.set_map_size(bridge.GridWidth, bridge.GridHeight)
	minimap.jump_requested.connect(_jump_to)
	refresh()
	_focus_on_start()


func refresh() -> void:
	var batch: Dictionary = bridge.SnapshotBatch()
	if batch.is_empty():
		return
	_entities.clear()
	_entity_by_id.clear()
	var ids: PackedInt64Array = batch["ids"]
	var positions_x: PackedFloat32Array = batch["x"]
	var positions_y: PackedFloat32Array = batch["y"]
	var teams: PackedInt32Array = batch["team"]
	var kinds: PackedInt32Array = batch["kind"]
	var hit_points: PackedInt32Array = batch["hp"]
	var max_hit_points: PackedInt32Array = batch["max_hp"]
	var unit_types: PackedInt32Array = batch["unit_type"]
	var building_types: PackedInt32Array = batch["building_type"]
	var widths: PackedInt32Array = batch["width"]
	var heights: PackedInt32Array = batch["height"]
	var building_states: PackedInt32Array = batch["building_state"]
	var building_progresses: PackedFloat32Array = batch["building_progress"]
	var building_max_progresses: PackedFloat32Array = batch["building_max_progress"]
	for index in int(batch["count"]):
		var entry := {
			"id": int(ids[index]),
			"x": positions_x[index],
			"y": positions_y[index],
			"team": teams[index],
			"kind": kinds[index],
			"hp": hit_points[index],
			"max_hp": max_hit_points[index],
			"unit_type": unit_types[index],
			"building_type": building_types[index],
			"width": widths[index],
			"height": heights[index],
			"building_state": building_states[index],
			"building_progress": building_progresses[index],
			"building_max_progress": building_max_progresses[index],
		}
		entry["name"] = _entity_name(entry)
		_entities.append(entry)
		_entity_by_id[int(ids[index])] = entry
	selection.prune(_entity_by_id)
	world_view.set_selection(selection.ids)
	_player = _find_player()
	hud.refresh(_player, _selected_entries(), _my_team)
	minimap.update_snapshot(batch, bridge.VisionStates(), _my_team)


func _focus_on_start() -> void:
	var total := Vector3.ZERO
	var count := 0
	for entry in _entities:
		if not _is_selectable(entry) or int(entry["team"]) != _my_team:
			continue
		total += Vector3(entry["x"], 0.0, entry["y"])
		count += 1
	if count == 0:
		return
	var center := total / float(count)
	camera.focus = Vector3(center.x, 0.0, center.z)
	camera.refresh_view()


func _find_player() -> Dictionary:
	for player: Dictionary in bridge.PlayerStates():
		if int(player["team"]) == _my_team:
			return player
	return {}


func _entity_name(entry: Dictionary) -> String:
	var kind: int = entry["kind"]
	var key := ""
	if kind == KIND_BUILDING:
		key = "b%d" % int(entry["building_type"])
	elif kind == KIND_VILLAGER:
		key = "villager"
	elif kind == KIND_MONK:
		key = "monk"
	elif kind == KIND_TRADE_CART:
		key = "trade_cart"
	else:
		key = "u%d" % int(entry["unit_type"])
	if _name_cache.has(key):
		return _name_cache[key]
	var name := "单位"
	if kind == KIND_BUILDING:
		name = bridge.BuildingName(int(entry["building_type"]))
	elif kind == KIND_VILLAGER:
		name = "村民"
	elif kind == KIND_MONK:
		name = "僧侣"
	elif kind == KIND_TRADE_CART:
		name = "贸易车"
	elif int(entry["unit_type"]) >= 0:
		name = bridge.UnitName(int(entry["unit_type"]))
	elif kind == KIND_HUNTING_ANIMAL:
		name = "野生动物"
	_name_cache[key] = name
	return name


func _selected_entries() -> Array:
	var result: Array = []
	for id in selection.ids:
		if _entity_by_id.has(id):
			result.append(_entity_by_id[id])
	return result


func _process(_delta: float) -> void:
	if bridge == null:
		return
	if _placing_type >= 0:
		var mouse := get_viewport().get_mouse_position()
		_hover_cell = _cell_at(mouse)
		world_view.show_placement(
			_placing_type, _hover_cell.x, _hover_cell.y,
			bridge.CanPlaceBuilding(_placing_type, _hover_cell.x, _hover_cell.y))
		return
	if get_viewport().gui_get_hovered_control() != null:
		Input.set_default_cursor_shape(Input.CURSOR_ARROW)
		return
	var own := _pick(get_viewport().get_mouse_position(), true)
	Input.set_default_cursor_shape(Input.CURSOR_POINTING_HAND if not own.is_empty() else Input.CURSOR_ARROW)


func _unhandled_input(event: InputEvent) -> void:
	if bridge == null:
		return
	if event is InputEventMouseMotion:
		_on_mouse_motion(event as InputEventMouseMotion)
	elif event is InputEventMouseButton:
		_on_mouse_button(event as InputEventMouseButton)
	elif event is InputEventKey:
		_on_key(event as InputEventKey)


func _on_mouse_motion(event: InputEventMouseMotion) -> void:
	if not _dragging:
		return
	var rect := _drag_rect(event.position)
	if rect.size.length() >= DRAG_THRESHOLD:
		hud.set_selection_box(rect)


func _on_mouse_button(event: InputEventMouseButton) -> void:
	if not event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT and _dragging:
			_finish_drag(event.position)
		return
	if event.button_index == MOUSE_BUTTON_LEFT:
		if _placing_type >= 0:
			_confirm_placement()
			get_viewport().set_input_as_handled()
			return
		_dragging = true
		_drag_origin = event.position
	elif event.button_index == MOUSE_BUTTON_RIGHT:
		if _placing_type >= 0:
			_cancel_placement()
			get_viewport().set_input_as_handled()
			return
		_issue_order(event.position)


func _on_key(event: InputEventKey) -> void:
	if not event.pressed or event.echo:
		return
	var key := event.keycode
	if key == KEY_ESCAPE:
		if _placing_type >= 0:
			_cancel_placement()
		else:
			selection.clear()
			world_view.set_selection(selection.ids)
			_push_hud()
		get_viewport().set_input_as_handled()
		return
	if key < KEY_0 or key > KEY_9:
		return
	var number := key - KEY_0
	if event.ctrl_pressed:
		selection.assign_group(number, selection.ids)
		hud.set_notice("编队 %d 已设置（%d 个单位）" % [number, selection.ids.size()])
	else:
		_recall_group(number)
	get_viewport().set_input_as_handled()


func _recall_group(number: int) -> void:
	var group := selection.group_ids(number)
	if group.is_empty():
		return
	selection.set_ids(group)
	world_view.set_selection(selection.ids)
	_push_hud()
	var now := Time.get_ticks_msec()
	var center := _selection_center()
	if int(_group_last_press.get(number, 0)) + DOUBLE_CLICK_MS > now:
		_jump_to(Vector2(center.x, center.y))
	_group_last_press[number] = now


func _selection_center() -> Vector3:
	var total := Vector3.ZERO
	var count := 0
	for id in selection.ids:
		if not _entity_by_id.has(id):
			continue
		var entry: Dictionary = _entity_by_id[id]
		total += Vector3(entry["x"], 0.0, entry["y"])
		count += 1
	if count == 0:
		return Vector3(camera.focus.x, 0.0, camera.focus.z)
	return total / float(count)


func _finish_drag(end_position: Vector2) -> void:
	_dragging = false
	hud.hide_selection_box()
	var rect := _drag_rect(end_position)
	if rect.size.length() < DRAG_THRESHOLD:
		_click_select(end_position)
	else:
		_box_select(rect)


func _drag_rect(end_position: Vector2) -> Rect2:
	return Rect2(_drag_origin, end_position - _drag_origin).abs()


func _click_select(position: Vector2) -> void:
	var hit := _pick(position, true)
	if hit.is_empty():
		selection.clear()
		world_view.set_selection(selection.ids)
		_push_hud()
		return
	var id: int = hit["id"]
	var now := Time.get_ticks_msec()
	if id == _last_click_id and now - _last_click_msec <= DOUBLE_CLICK_MS:
		_select_same_type(hit)
	else:
		selection.set_ids([id])
	_last_click_id = id
	_last_click_msec = now
	world_view.set_selection(selection.ids)
	_push_hud()


func _box_select(rect: Rect2) -> void:
	var ids: Array = []
	for entry in _entities:
		if not _is_selectable(entry) or int(entry["team"]) != _my_team:
			continue
		if rect.has_point(_entity_screen_position(entry)):
			ids.append(int(entry["id"]))
	selection.set_ids(ids)
	world_view.set_selection(selection.ids)
	_push_hud()


func _select_same_type(entry: Dictionary) -> void:
	var ids: Array = []
	var viewport_rect := get_viewport().get_visible_rect()
	for candidate in _entities:
		if not _is_selectable(candidate) or int(candidate["team"]) != _my_team:
			continue
		if int(candidate["kind"]) != int(entry["kind"]):
			continue
		if int(entry["kind"]) != KIND_BUILDING and int(candidate["unit_type"]) != int(entry["unit_type"]):
			continue
		if int(entry["kind"]) == KIND_BUILDING and int(candidate["building_type"]) != int(entry["building_type"]):
			continue
		if viewport_rect.has_point(_entity_screen_position(candidate)):
			ids.append(int(candidate["id"]))
	selection.set_ids(ids)
	world_view.set_selection(selection.ids)
	_push_hud()


func _issue_order(position: Vector2) -> void:
	if selection.ids.is_empty():
		return
	var cell := _cell_at(position)
	var target := _pick(position, false)
	var selected_building := _single_selected_building()
	if not selected_building.is_empty():
		_set_rally(selected_building, cell, target)
		return
	var unit_ids := _selected_unit_ids()
	if unit_ids.is_empty():
		return
	if not target.is_empty():
		var kind: int = target["kind"]
		if kind == KIND_RESOURCE_NODE:
			var villagers: Array = []
			for id in unit_ids:
				if int(_entity_by_id[id]["kind"]) == KIND_VILLAGER:
					villagers.append(id)
			if not villagers.is_empty():
				bridge.GatherUnits(villagers, int(target["id"]))
				_flash(cell, Color(1.0, 0.85, 0.3))
				hud.set_notice("村民前往采集")
			_move_ids(_without_ids(unit_ids, villagers), cell)
			return
		if int(target["team"]) != _my_team:
			bridge.AttackUnits(unit_ids, int(target["id"]))
			_flash(cell, Color(1.0, 0.35, 0.3))
			hud.set_notice("已下达攻击指令")
			return
	_move_ids(unit_ids, cell)


func _set_rally(building: Dictionary, cell: Vector2i, target: Dictionary) -> void:
	var target_id := int(target["id"]) if not target.is_empty() else -1
	bridge.SetRally(int(building["id"]), cell.x, cell.y, target_id)
	_flash(cell, Color(0.4, 1.0, 0.5))
	hud.set_notice("已设置集结点")


func _move_ids(ids: Array, cell: Vector2i) -> void:
	if ids.is_empty():
		return
	var packed := PackedInt64Array(ids)
	bridge.MoveUnits(packed, cell.x, cell.y)
	_flash(cell, Color(0.4, 1.0, 0.5))
	hud.set_notice("已下达移动指令")


func _without_ids(ids: Array, removed: Array) -> Array:
	var result: Array = []
	for id in ids:
		if not removed.has(id):
			result.append(id)
	return result


func _single_selected_building() -> Dictionary:
	if selection.ids.size() != 1:
		return {}
	var entry: Dictionary = _entity_by_id.get(selection.ids[0], {})
	if entry.is_empty() or int(entry["kind"]) != KIND_BUILDING:
		return {}
	if int(entry["team"]) != _my_team:
		return {}
	return entry


func _selected_unit_ids() -> Array:
	var ids: Array = []
	for id in selection.ids:
		if not _entity_by_id.has(id):
			continue
		if int(_entity_by_id[id]["kind"]) != KIND_BUILDING:
			ids.append(id)
	return ids


func _start_placement(building_type: int) -> void:
	_placing_type = building_type
	hud.set_placement_hint(bridge.BuildingName(building_type))
	Input.set_default_cursor_shape(Input.CURSOR_CROSS)
	var mouse := get_viewport().get_mouse_position()
	_hover_cell = _cell_at(mouse)
	world_view.show_placement(building_type, _hover_cell.x, _hover_cell.y, false)


func _confirm_placement() -> void:
	var cell := _cell_at(get_viewport().get_mouse_position())
	if bridge.CanPlaceBuilding(_placing_type, cell.x, cell.y):
		bridge.PlaceBuilding(_placing_type, cell.x, cell.y)
		hud.set_notice("已下达建造指令")
		_flash(cell, Color(0.5, 0.8, 1.0))
	else:
		hud.set_notice("该位置无法建造")
	_cancel_placement()


func _cancel_placement() -> void:
	_placing_type = -1
	world_view.hide_placement()
	hud.clear_placement_hint()
	Input.set_default_cursor_shape(Input.CURSOR_ARROW)


func _train_unit(unit_type: int) -> void:
	var building := _single_selected_building()
	if building.is_empty():
		return
	bridge.Train(int(building["id"]), unit_type)
	hud.set_notice("已下达训练指令")


func _research_tech(tech_id: int) -> void:
	bridge.ResearchTech(tech_id)
	hud.set_notice("已下达研究指令")


func _age_up(target_age: int) -> void:
	bridge.AgeUp(target_age)
	hud.set_notice("已下达升级时代指令")


func _stop_units() -> void:
	var ids := _selected_unit_ids()
	if ids.is_empty():
		return
	bridge.StopUnits(PackedInt64Array(ids))
	hud.set_notice("已下达停止指令")


func _market_buy(resource: int, amount: int) -> void:
	bridge.MarketBuy(resource, amount)
	hud.set_notice("已下达买入指令")


func _market_sell(resource: int, amount: int) -> void:
	bridge.MarketSell(resource, amount)
	hud.set_notice("已下达卖出指令")


func _tribute(target: int, resource: int, amount: int) -> void:
	bridge.Tribute(target, resource, amount)
	hud.set_notice("已下达进贡指令")


func _jump_to(world_position: Vector2) -> void:
	camera.focus = Vector3(world_position.x, 0.0, world_position.y)
	camera.refresh_view()


func _flash(cell: Vector2i, color: Color) -> void:
	var marker := world_view.show_order_marker(cell.x, cell.y, color)
	await get_tree().create_timer(MARKER_SECONDS).timeout
	if is_instance_valid(marker):
		marker.queue_free()


func _push_hud() -> void:
	_player = _find_player()
	hud.refresh(_player, _selected_entries(), _my_team)


func _is_selectable(entry: Dictionary) -> bool:
	return int(entry["kind"]) in SELECTABLE_KINDS


func _is_targetable(entry: Dictionary) -> bool:
	return int(entry["kind"]) in TARGETABLE_KINDS


func _pick(screen_position: Vector2, own_only: bool) -> Dictionary:
	var best: Dictionary = {}
	var best_distance := CLICK_RADIUS
	for entry in _entities:
		if not _is_targetable(entry):
			continue
		if own_only and int(entry["team"]) != _my_team:
			continue
		var distance := screen_position.distance_to(_entity_screen_position(entry))
		if distance <= best_distance:
			best_distance = distance
			best = entry
	return best


func _entity_screen_position(entry: Dictionary) -> Vector2:
	var center_x: float = entry["x"]
	var center_y: float = entry["y"]
	if int(entry["kind"]) == KIND_BUILDING:
		center_x = center_x - 0.5 + float(entry["width"]) * 0.5
		center_y = center_y - 0.5 + float(entry["height"]) * 0.5
	var ground := world_view.ground_level_at(center_x, center_y)
	return camera.unproject_position(Vector3(center_x, ground + 0.4, center_y))


func _cell_at(screen_position: Vector2) -> Vector2i:
	var origin := camera.project_ray_origin(screen_position)
	var normal := camera.project_ray_normal(screen_position)
	var hit = Plane(Vector3.UP, 0.0).intersects_ray(origin, normal)
	if hit == null:
		return Vector2i(-1, -1)
	return Vector2i(floori(hit.x), floori(hit.z))
