extends TestBase

const SELECTION_MODEL := preload("res://presentation/selection_model.gd")
const WORLD_VIEW := preload("res://presentation/world_view.gd")
const MAIN_SCENE := preload("res://presentation/main.tscn")
const KIND_BUILDING := 8


func run() -> void:
	_selection_deduplicates_and_prunes()
	_groups_keep_their_own_copy()
	_world_view_draws_selection_rings()
	_hud_periphery_frameworks()
	_hud_band_keeps_panels_inside()


func _selection_deduplicates_and_prunes() -> void:
	var model = SELECTION_MODEL.new()
	model.set_ids([3, 5, 3])
	check_eq(model.ids.size(), 2, "selection keeps unique ids")
	model.prune({3: true})
	check_true(model.has_id(3) and not model.has_id(5), "selection drops dead entities")
	model.clear()
	check_eq(model.ids.size(), 0, "selection clears")


func _groups_keep_their_own_copy() -> void:
	var model = SELECTION_MODEL.new()
	var source := [7, 9]
	model.assign_group(1, source)
	source.append(11)
	check_eq(model.group_ids(1).size(), 2, "control group snapshots its ids")
	check_true(model.group_ids(2).is_empty(), "unassigned control group is empty")


func _world_view_draws_selection_rings() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalDemo()
	var world := WORLD_VIEW.new()
	add_child(world)
	world.build_terrain(bridge)
	world.refresh(bridge)
	var selected_ids: Array = []
	for index in int(bridge.SnapshotBatch()["count"]):
		var batch: Dictionary = bridge.SnapshotBatch()
		var kinds: PackedInt32Array = batch["kind"]
		if kinds[index] != KIND_BUILDING:
			var ids: PackedInt64Array = batch["ids"]
			selected_ids.append(int(ids[index]))
			break
	world.set_selection(selected_ids)
	var rings: MultiMeshInstance3D = world.get("_selection_mesh")
	check_eq(rings.multimesh.instance_count, selected_ids.size(), "selection rings follow the selection")
	world.set_selection([])
	check_eq(rings.multimesh.instance_count, 0, "selection rings clear with the selection")
	bridge.queue_free()
	world.queue_free()


func _hud_periphery_frameworks() -> void:
	var scene := MAIN_SCENE.instantiate()
	add_child(scene)
	var hud: GameHud = scene.get_node("Hud")
	var feed: VBoxContainer = scene.get_node("%NoticeFeed")
	var notifications_before := feed.get_child_count()
	hud.push_notification("测试通知")
	check_eq(feed.get_child_count(), notifications_before + 1, "notification feed appends events")
	var chat_log: RichTextLabel = scene.get_node("%ChatLog")
	var chat_before := chat_log.get_parsed_text().length()
	hud.push_chat_line("测试聊天")
	check_true(chat_log.get_parsed_text().length() > chat_before, "chat log accepts messages")
	hud.show_unit_tooltip({"name": "村民", "hp": 10, "max_hp": 20, "kind": 0}, Vector2(400, 300))
	var tooltip: Control = scene.get_node("%TooltipPanel")
	check_true(tooltip.visible, "tooltip shows for a hovered entity")
	hud.hide_unit_tooltip()
	check_true(not tooltip.visible, "tooltip hides when hover ends")
	hud.open_pause_menu()
	check_true(get_tree().paused, "pause menu pauses the tree")
	check_true((scene.get_node("%PauseMenu") as Control).visible, "pause menu becomes visible")
	hud.close_pause_menu()
	check_true(not get_tree().paused, "pause menu resumes the tree")
	var pause_menu: PauseMenu = scene.get_node("%PauseMenu")
	var surrender_confirm: ConfirmationDialog = scene.get_node("%PauseSurrenderConfirm")
	var surrender_events: Array = []
	pause_menu.surrender_requested.connect(func() -> void: surrender_events.append(true))
	hud.open_pause_menu()
	var surrender_button: Button = scene.get_node("%SurrenderButton")
	surrender_button.pressed.emit()
	check_true(surrender_confirm.visible, "surrender asks for confirmation")
	check_true(get_tree().paused, "confirmation keeps the game paused")
	check_eq(surrender_events.size(), 0, "surrender is not emitted before confirmation")
	surrender_confirm.canceled.emit()
	surrender_confirm.hide()
	check_true(pause_menu.visible, "canceling surrender returns to the pause menu")
	check_true(get_tree().paused, "canceling surrender keeps the game paused")
	check_eq(surrender_events.size(), 0, "canceling surrender does not end the match")
	surrender_button.pressed.emit()
	surrender_confirm.confirmed.emit()
	check_eq(surrender_events.size(), 1, "confirming surrender ends the match")
	check_true(not get_tree().paused, "confirming surrender resumes the tree")
	check_true(not pause_menu.visible, "confirming surrender closes the pause menu")
	var controller: BattleController = scene.get_node("BattleController")
	controller.selection.assign_group(2, [11, 12])
	var assignments: Dictionary = controller.selection.group_assignments()
	check_eq(assignments.size(), 2, "group assignments map every member")
	check_eq(int(assignments[11]), 2, "group assignments keep the control group number")
	scene.queue_free()


## 底部 25% 是唯一的常驻 HUD 带：主面板默认/展开态都必须落在带内。
func _hud_band_keeps_panels_inside() -> void:
	var scene := MAIN_SCENE.instantiate()
	add_child(scene)
	var chat: Control = scene.get_node("Hud/ChatPanel")
	var market: Control = scene.get_node("Hud/MarketPanel")
	check_true(not chat.visible, "chat panel starts collapsed")
	check_true(not market.visible, "market panel starts collapsed")
	chat.show()
	market.show()
	var panel_names := ["MinimapPanel", "SelectionPanel", "CommandPanel", "ChatPanel", "MarketPanel"]
	for resolution: Vector2 in [Vector2(1920, 1080), Vector2(1600, 900), Vector2(1280, 720)]:
		var band_top := resolution.y * 0.75
		for panel_name: String in panel_names:
			var panel: Control = scene.get_node("Hud/%s" % panel_name)
			var top := panel.anchor_top * resolution.y + panel.offset_top
			var bottom := panel.anchor_bottom * resolution.y + panel.offset_bottom
			var left := panel.anchor_left * resolution.x + panel.offset_left
			var right := panel.anchor_right * resolution.x + panel.offset_right
			check_true(top >= band_top - 0.5,
				"%s stays below the band top at %dx%d" % [panel_name, int(resolution.x), int(resolution.y)])
			check_true(bottom <= resolution.y + 0.5,
				"%s stays inside the viewport at %dx%d" % [panel_name, int(resolution.x), int(resolution.y)])
			check_true(bottom > top and right > left,
				"%s keeps a positive rect at %dx%d" % [panel_name, int(resolution.x), int(resolution.y)])
			check_true(left >= 0.0 and right <= resolution.x + 0.5,
				"%s stays inside the viewport width at %dx%d" % [panel_name, int(resolution.x), int(resolution.y)])
	scene.queue_free()
