extends Node
## 抓取战争迷雾与合批单位的关键截图：迷雾保留可见，对比单位数量与合批桶数。

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
	var camera: Variant = scene.get_node("Camera3D")
	camera.set("focus", Vector3(16.0, 0.0, 16.0))
	camera.set("ortho_size", 20.0)
	camera.call("refresh_view")
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	var units := 0
	for bucket: Dictionary in world.get("_unit_meshes").values():
		units += (bucket["multimesh"] as MultiMesh).instance_count
	print("BATCH_UNITS %d BUCKETS %d" % [units, world.get("_unit_meshes").size()])
	await _save("fog_batched_units.png")

	camera.set("focus", Vector3(10.0, 0.0, 10.0))
	camera.set("ortho_size", 12.0)
	camera.call("refresh_view")
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("fog_base_closeup.png")
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
