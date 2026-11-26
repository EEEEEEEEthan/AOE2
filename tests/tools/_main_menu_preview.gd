extends Node

const WARMUP_FRAMES := 40
const MATCH_WARMUP_FRAMES := 90


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("main_menu.png")
	var menu := get_tree().current_scene
	var standard_game: Button = menu.get_node("%StandardGameButton")
	standard_game.pressed.emit()
	for frame in MATCH_WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("standard_match.png")
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
