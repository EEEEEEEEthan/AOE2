extends Node
## 抓科技树界面截图：通用文明全可用与不列颠禁用鳞甲两种状态。

const WARMUP_FRAMES := 40
const SETTLE_FRAMES := 10


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
	var tree: Variant = scene.get_node("TechTree")
	scene.call("_on_tech_tree_pressed")
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("tech_tree_generic.png")
	var select: OptionButton = tree.get_node("Frame/Margin/Content/TopBar/CivSelect")
	select.select(1)
	select.item_selected.emit(1)
	for frame in SETTLE_FRAMES:
		await get_tree().process_frame
	await _save("tech_tree_longbow.png")
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
