extends Node

const WARMUP_FRAMES := 40


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	var editor := get_tree().current_scene
	var model: MapEditorModel = editor.get("_model")
	while model == null and waited < 240:
		await get_tree().process_frame
		model = editor.get("_model")
		waited += 1
	model.new_map(32, 32)
	model.begin_stroke()
	_fill_height(model, 4, 4, 9, 9, 3)
	_fill_type(model, 4, 4, 9, 9, MapEditorModel.TYPE_CLIFF)
	_fill_height(model, 5, 5, 7, 7, 3)
	_fill_type(model, 5, 5, 7, 7, MapEditorModel.TYPE_GRASS)
	for cell_y in range(3, 29):
		model.raise_height(16, cell_y, 0, 3)
		model.paint_type(16, cell_y, 0, MapEditorModel.TYPE_CLIFF)
	_fill_height(model, 21, 2, 7, 6, 4)
	_fill_type(model, 21, 2, 7, 6, MapEditorModel.TYPE_GRASS)
	_fill_height(model, 24, 23, 4, 4, 0)
	_fill_type(model, 24, 23, 4, 4, MapEditorModel.TYPE_WATER)
	_fill_type(model, 6, 22, 5, 4, MapEditorModel.TYPE_DIRT)
	model.add_placement(MapEditorModel.KIND_SPAWN, 4, 16, 0, 0, 0)
	model.add_placement(MapEditorModel.KIND_SPAWN, 27, 16, 1, 0, 0)
	model.add_placement(MapEditorModel.KIND_BUILDING, 8, 14, 0, 0, 0)
	model.add_placement(MapEditorModel.KIND_BUILDING, 24, 14, 1, 0, 0)
	model.add_placement(MapEditorModel.KIND_RESOURCE, 6, 7, 0, 0, 300)
	model.add_placement(MapEditorModel.KIND_RESOURCE, 10, 6, 0, 1, 300)
	model.add_placement(MapEditorModel.KIND_RESOURCE, 25, 4, 0, 2, 300)
	model.add_placement(MapEditorModel.KIND_RESOURCE, 10, 25, 0, 3, 300)
	model.end_stroke()
	editor.call("_mark_dirty")
	editor.call("_fit_camera")
	var camera := editor.get_node("Camera3D") as Camera3D
	camera.ortho_size = 20.0
	camera.call("refresh_view")
	for frame in WARMUP_FRAMES:
		await get_tree().process_frame
	await _save("map_editor_overview.png")
	get_tree().quit(0)


func _fill_height(model: MapEditorModel, start_x: int, start_y: int, size_x: int, size_y: int,
		level: int) -> void:
	for cell_y in range(start_y, start_y + size_y):
		for cell_x in range(start_x, start_x + size_x):
			var delta := level - model.height_at(cell_x, cell_y)
			if delta != 0:
				model.raise_height(cell_x, cell_y, 0, delta)


func _fill_type(model: MapEditorModel, start_x: int, start_y: int, size_x: int, size_y: int,
		terrain_type: int) -> void:
	for cell_y in range(start_y, start_y + size_y):
		for cell_x in range(start_x, start_x + size_x):
			model.paint_type(cell_x, cell_y, 0, terrain_type)


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
