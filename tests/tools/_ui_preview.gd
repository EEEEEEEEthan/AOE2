extends Node
## 抓 UI/交互层截图：选中单位、建造放置预览与 HUD/小地图。

const WARMUP_FRAMES := 60
const SETTLE_FRAMES := 12


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	if get_tree().current_scene.get("bridge") == null:
		await get_tree().process_frame
		get_tree().change_scene_to_file("res://presentation/main.tscn")
		await get_tree().process_frame
		await get_tree().process_frame
	var scene := get_tree().current_scene
	get_window().size = Vector2i(1440, 900)
	var bridge: Variant = scene.get("bridge")
	while bridge == null and waited < 240:
		await get_tree().process_frame
		bridge = scene.get("bridge")
		waited += 1
	var controller: Variant = scene.get_node("BattleController")
	var hud: Variant = scene.get_node("Hud")
	var world: Variant = scene.get_node("WorldView")
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	var selected: Array = []
	for entity: Dictionary in bridge.Snapshot():
		if int(entity["team"]) != bridge.MyTeam:
			continue
		if int(entity["kind"]) in [0, 1, 5, 9]:
			selected.append(int(entity["id"]))
	controller.get("selection").set_ids(selected)
	world.call("set_selection", selected)
	controller.call("refresh")
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("ui_selection.png")
	hud.emit_signal("build_requested", 2)
	Input.warp_mouse(Vector2(380, 640))
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("ui_placement.png")
	hud.emit_signal("placement_cancelled")
	hud.call("open_pause_menu")
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("ui_pause.png")
	hud.call("close_pause_menu")
	get_tree().quit(0)


func _save(file_name: String) -> void:
	await RenderingServer.frame_post_draw
	var image := get_viewport().get_texture().get_image()
	var path := _run_directory().path_join(file_name)
	image.save_png(path)
	print("SCREENSHOT %s %dx%d" % [path, image.get_width(), image.get_height()])


func _run_directory() -> String:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--run-directory="):
			return argument.trim_prefix("--run-directory=")
	return OS.get_user_data_dir()
