extends TestBase

const MODEL := preload("res://tools/map_editor_model.gd")

const MAP_PATH := "user://map_editor_test.json"


func run() -> void:
	var model: MapEditorModel = MODEL.new()
	model.new_map(8, 8)
	check_eq(model.width, 8, "new map width")
	check_eq(model.type_at(0, 0), MapEditorModel.TYPE_GRASS, "new map default grass")

	model.begin_stroke()
	model.raise_height(4, 4, 1, 2)
	var raised := model.height_at(4, 4)
	model.end_stroke()
	check(raised > 0, "raise height changes cell")
	model.undo()
	check_eq(model.height_at(4, 4), 0, "undo restores height")
	model.redo()
	check_eq(model.height_at(4, 4), raised, "redo reapplies height")

	model.begin_stroke()
	model.paint_type(2, 2, 0, MapEditorModel.TYPE_WATER)
	model.end_stroke()
	check_eq(model.type_at(2, 2), MapEditorModel.TYPE_WATER, "paint type water")
	model.undo()
	check_eq(model.type_at(2, 2), MapEditorModel.TYPE_GRASS, "undo restores type")
	model.redo()
	check_eq(model.type_at(2, 2), MapEditorModel.TYPE_WATER, "redo restores type")

	model.begin_stroke()
	model.add_placement(MapEditorModel.KIND_SPAWN, 1, 1, 0, 0, 0)
	model.add_placement(MapEditorModel.KIND_RESOURCE, 6, 6, 0, 0, 300)
	model.end_stroke()
	check_eq(model.placements.size(), 2, "placements added")
	model.undo()
	check_eq(model.placements.size(), 0, "undo removes placements")
	model.redo()
	check_eq(model.placements.size(), 2, "redo restores placements")

	var bridge := MapEditorBridge.new()
	add_child(bridge)
	var save_result := model.save_map(bridge, MAP_PATH)
	check(save_result.get("ok", false), "save map to json")
	var loaded: MapEditorModel = MODEL.new()
	var load_result := loaded.load_map(bridge, MAP_PATH)
	check(load_result["ok"], "load map from json")
	check_eq(loaded.width, 8, "loaded width")
	check_eq(loaded.height_at(4, 4), raised, "roundtrip keeps height")
	check_eq(loaded.type_at(2, 2), MapEditorModel.TYPE_WATER, "roundtrip keeps type")
	check_eq(loaded.placements.size(), 2, "roundtrip keeps placements")

	var walled: MapEditorModel = MODEL.new()
	walled.new_map(8, 8)
	walled.begin_stroke()
	walled.add_placement(MapEditorModel.KIND_SPAWN, 1, 3, 0, 0, 0)
	walled.add_placement(MapEditorModel.KIND_SPAWN, 6, 3, 1, 0, 0)
	walled.add_placement(MapEditorModel.KIND_RESOURCE, 6, 1, 0, 0, 300)
	for cell_y in 8:
		walled.paint_type(3, cell_y, 0, MapEditorModel.TYPE_CLIFF)
	walled.end_stroke()
	var split_result := walled.validate(bridge)
	check(not split_result["spawn_connected"], "cliff wall disconnects spawns")
	check(not split_result["resources_reachable"], "cliff wall hides resources")
	walled.begin_stroke()
	walled.paint_type(3, 3, 0, MapEditorModel.TYPE_GRASS)
	walled.end_stroke()
	var open_result := walled.validate(bridge)
	check(open_result["spawn_connected"], "opening wall reconnects spawns")
	check(open_result["resources_reachable"], "opening wall makes resources reachable")

	var scene := load("res://tools/map_editor.tscn") as PackedScene
	check(scene != null, "editor scene loads")
	var editor: Node3D = scene.instantiate()
	add_child(editor)
	editor._process(0.0)
	check(editor.get_child_count() > 0, "editor scene builds preview")
	editor.queue_free()

	bridge.queue_free()
