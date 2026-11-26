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
	var controller: BattleController = scene.get_node("BattleController")
	controller.selection.assign_group(2, [11, 12])
	var assignments: Dictionary = controller.selection.group_assignments()
	check_eq(assignments.size(), 2, "group assignments map every member")
	check_eq(int(assignments[11]), 2, "group assignments keep the control group number")
	scene.queue_free()
