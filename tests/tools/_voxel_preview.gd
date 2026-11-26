extends Node
## 抓取体素地形/单位与悬崖高度差的关键截图，并把演示地图导出为 maps/voxel_demo.json。

const WARMUP_FRAMES := 90
const SETTLE_FRAMES := 25


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
	bridge.call("StartLocalDemo")
	var world: Node3D = scene.get_node("WorldView")
	var fog: Variant = world.get("_fog")
	if fog != null:
		fog.visible = false
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("voxel_overview.png")

	var map_json: String = bridge.call("DemoMapJson")
	DirAccess.make_dir_recursive_absolute("res://maps")
	var file := FileAccess.open("res://maps/voxel_demo.json", FileAccess.WRITE)
	if file != null:
		file.store_string(map_json)
		file.close()
	print("MAP_WRITTEN res://maps/voxel_demo.json")

	var camera: Variant = scene.get_node("Camera3D")
	camera.set("focus", Vector3(16.0, 0.0, 9.0))
	camera.set("ortho_size", 9.0)
	camera.call("_apply_view")
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("voxel_cliff_closeup.png")

	camera.set("focus", Vector3(9.0, 0.0, 9.0))
	camera.set("ortho_size", 8.0)
	camera.call("_apply_view")
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("voxel_village.png")
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
