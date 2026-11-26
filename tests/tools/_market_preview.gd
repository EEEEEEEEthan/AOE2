extends Node
## 抓市场交易界面截图：选中已完工市场，展示买卖价与进贡入口。

const WARMUP_FRAMES := 60
const SETTLE_FRAMES := 20
const KIND_BUILDING := 8
const MARKET := 6


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	var scene := get_tree().current_scene
	get_window().size = Vector2i(1440, 900)
	var bridge: Variant = scene.get("bridge")
	while bridge == null and waited < 240:
		await get_tree().process_frame
		bridge = scene.get("bridge")
		waited += 1
	var controller: Variant = scene.get_node("BattleController")
	var world: Variant = scene.get_node("WorldView")
	var camera: Variant = scene.get_node("Camera3D")
	bridge.call("StartBuildingShowcase")
	bridge.call("RevealAllVision")
	_reset_world(world, bridge)
	camera.set("grid_width", 28.0)
	camera.set("grid_height", 12.0)
	camera.set("focus", Vector3(17.0, 0.0, 3.5))
	camera.set("ortho_size", 12.0)
	camera.call("refresh_view")
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	var market := _find_market(bridge)
	if not market.is_empty():
		controller.get("selection").set_ids([int(market["id"])])
		world.call("set_selection", [int(market["id"])])
		controller.call("refresh")
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("market_panel.png")
	get_tree().quit(0)


func _find_market(bridge: Variant) -> Dictionary:
	for entity: Dictionary in bridge.Snapshot():
		if int(entity["team"]) != bridge.MyTeam:
			continue
		if int(entity["kind"]) != KIND_BUILDING:
			continue
		if int(entity["building_type"]) == MARKET and int(entity["building_state"]) == 1:
			return entity
	return {}


func _reset_world(world: Node3D, bridge: Variant) -> void:
	for child in world.get_children():
		child.free()
	world.set("_units", {})
	world.set("_unit_meshes", {})
	world.build_terrain(bridge)
	world.refresh(bridge)
	var fog: Variant = world.get("_fog")
	if fog != null:
		fog.visible = false


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
