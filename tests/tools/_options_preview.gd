extends Node
## 抓取选项界面截图：画面页与快捷键页。

const WARMUP_FRAMES := 40


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
	var options_button: Button = menu.get_node("%OptionsButton")
	options_button.pressed.emit()
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("options_display.png")
	var options := menu.get_node_or_null("Options")
	if options != null:
		var tabs: TabContainer = options.get_node("Center/Panel/Margin/Content/TabContainer")
		tabs.current_tab = 3
		for frame in WARMUP_FRAMES:
			await get_tree().process_frame
		await _save("options_keyboard.png")
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
