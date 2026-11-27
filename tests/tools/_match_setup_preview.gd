extends Node
## 抓取对局设置界面截图：默认设置、选好文明、加载界面文明展示。

const WARMUP_FRAMES := 40
const LOADING_WARMUP_FRAMES := 30


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	get_window().size = Vector2i(1440, 900)
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	var menu := get_tree().current_scene
	var standard_game: Button = menu.get_node("%StandardGameButton")
	standard_game.pressed.emit()
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("match_setup.png")
	var dialog := menu.get_node_or_null("MatchSetupDialog")
	if dialog == null:
		get_tree().quit(1)
		return
	var civ_rows: VBoxContainer = dialog.get_node("Center/Panel/Margin/Content/CivRows")
	var player_rows := civ_rows.get_children()
	if player_rows.size() >= 2:
		(player_rows[0] as HBoxContainer).get_child(1).select(0)
		(player_rows[1] as HBoxContainer).get_child(1).select(1)
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("match_setup_civs.png")
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
