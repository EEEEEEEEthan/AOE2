extends Node
## 生产队列截图：自建最小场景，起一局完工兵营 + 排队单位，等生产推进后截图。

const CAPTURE_FRAMES := 90
const TICK_SECONDS := 1.0 / 30.0


func _ready() -> void:
	_capture()


func _capture() -> void:
	var previous := get_tree().current_scene
	if previous != null:
		previous.queue_free()
		await get_tree().process_frame
	var root := Node3D.new()
	root.name = "ProductionPreview"
	get_tree().root.add_child(root)
	get_tree().current_scene = root

	var world_environment := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color(0.09, 0.11, 0.15, 1.0)
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.62, 0.66, 0.74, 1.0)
	environment.ambient_light_energy = 0.7
	world_environment.environment = environment
	root.add_child(world_environment)

	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-60.0, 0.0, 0.0)
	light.light_energy = 1.1
	root.add_child(light)

	var camera_script := load("res://presentation/iso_camera.gd") as GDScript
	var camera := camera_script.new() as Camera3D
	camera.set("focus", Vector3(14.0, 0.0, 14.0))
	camera.set("ortho_size", 10.0)
	root.add_child(camera)

	var bridge := GameBridge.new()
	root.add_child(bridge)
	bridge.StartProductionDemo()

	var view_script := load("res://presentation/world_view.gd") as GDScript
	var view := view_script.new() as Node3D
	root.add_child(view)
	view.call("build_terrain", bridge)
	view.call("refresh", bridge)

	for frame in CAPTURE_FRAMES:
		bridge.Step(TICK_SECONDS)
		view.call("refresh", bridge)
		await get_tree().process_frame
	await _save("production_queue.png")

	for frame in 160:
		bridge.Step(TICK_SECONDS)
		view.call("refresh", bridge)
		await get_tree().process_frame
	await _save("production_units.png")
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
