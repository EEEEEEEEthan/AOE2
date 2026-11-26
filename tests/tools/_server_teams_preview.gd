extends Node
## 抓取按连接分配观察方视野的关键截图：运行 DedicatedServer 多连接自测并叠加结果。

const WARMUP_FRAMES := 90


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
	print("PREVIEW before self test")
	var self_test := SimulationBridge.new()
	scene.add_child(self_test)
	var failures: Array = self_test.call("ServerTeamsSelfTest")
	self_test.queue_free()
	print("PREVIEW after self test failures=%d" % failures.size())
	var passed := failures.is_empty()

	var layer := CanvasLayer.new()
	scene.add_child(layer)
	var backdrop := ColorRect.new()
	backdrop.color = Color(0.04, 0.05, 0.07, 0.82)
	backdrop.position = Vector2(32.0, 32.0)
	backdrop.size = Vector2(920.0, 230.0)
	layer.add_child(backdrop)
	var label := Label.new()
	label.position = Vector2(56.0, 52.0)
	label.size = Vector2(880.0, 190.0)
	label.add_theme_font_size_override("font_size", 26)
	var report := "DedicatedServer multi-connection test: %s\n" % ("PASS" if passed else "FAIL")
	report += "Connection A -> viewer team 0\n"
	report += "Connection B -> viewer team 1\n"
	report += "Each snapshot keeps only its own team's visible units\n"
	report += "Per-connection vision bitmaps differ\n"
	if not passed:
		report += "\n".join(failures)
	label.text = report
	layer.add_child(label)

	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("server_teams_vision.png")
	get_tree().quit(0 if passed else 1)


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
