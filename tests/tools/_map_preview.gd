extends Node
## 抓取阿拉伯地图生成器的关键截图：全景与起始基地近景。

const SEED := 20261127
const PLAYERS := 2
const WARMUP_FRAMES := 120
const SETTLE_FRAMES := 30


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	var scene := get_tree().current_scene
	var bridge: Variant = scene.get("bridge")
	while bridge == null and waited < 240:
		await get_tree().process_frame
		bridge = scene.get("bridge")
		waited += 1

	bridge.call("StartGeneratedMatch", SEED, PLAYERS)
	bridge.call("RevealAllVision")
	var world: Node3D = scene.find_child("WorldView", true, false)
	for child in world.get_children():
		child.free()
	world.set("_units", {})
	world.set("_unit_meshes", {})
	world.build_terrain(bridge)
	world.refresh(bridge)
	var fog: Variant = world.get("_fog")
	if fog != null:
		fog.visible = false

	var camera: Variant = scene.find_child("Camera3D", true, false)
	var width := float(bridge.GridWidth)
	var height := float(bridge.GridHeight)
	camera.set("grid_width", width)
	camera.set("grid_height", height)
	camera.set("focus", Vector3(width * 0.5, 0.0, height * 0.5))
	camera.set("ortho_size", 42.0)
	camera.call("refresh_view")
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("map_overview.png")

	camera.set("focus", Vector3(width * 0.25, 0.0, height * 0.5))
	camera.set("ortho_size", 16.0)
	camera.call("refresh_view")
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("map_start_base.png")
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
