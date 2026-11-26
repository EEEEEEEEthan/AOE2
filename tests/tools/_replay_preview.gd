extends Node


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	var scene := get_tree().current_scene
	var bridge: Variant = scene.get("bridge")
	while bridge == null and waited < 480:
		await get_tree().process_frame
		bridge = scene.get("bridge")
		waited += 1
	if bridge == null:
		get_tree().quit(1)
		return
	bridge.FastForward(240)
	for frame in 20:
		await get_tree().process_frame
	bridge.SaveReplay("预览录像")
	get_tree().change_scene_to_file("res://presentation/replay.tscn")
	for frame in 8:
		await get_tree().process_frame
	var replay_scene := get_tree().current_scene
	var replay_bridge: Variant = replay_scene.get("bridge")
	waited = 0
	while replay_bridge == null and waited < 240:
		await get_tree().process_frame
		replay_bridge = replay_scene.get("bridge")
		waited += 1
	if replay_bridge != null:
		replay_bridge.SetReplaySpeed(4.0)
		replay_scene.call("_on_play_pressed")
	for frame in 90:
		await get_tree().process_frame
	await _save("replay_scene.png")
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
