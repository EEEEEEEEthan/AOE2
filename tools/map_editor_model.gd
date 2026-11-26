class_name MapEditorModel
extends RefCounted

const TYPE_GRASS := 0
const TYPE_DIRT := 1
const TYPE_WATER := 2
const TYPE_CLIFF := 3

const MAX_HEIGHT := 16

const KIND_SPAWN := "spawn"
const KIND_RESOURCE := "resource"
const KIND_BUILDING := "building"

var width := 0
var height := 0
var types := PackedInt32Array()
var vertex_heights := PackedInt32Array()
var blocked := PackedInt32Array()
var placements: Array = []
var map_name := "editor_map"
var map_seed := 20261126

var _undo_stack: Array = []
var _redo_stack: Array = []
var _stroke_active := false
var _stroke_cells: Dictionary = {}
var _stroke_vertices: Dictionary = {}
var _stroke_placements_before: Array = []
var _stroke_placements_touched := false


func new_map(map_width: int, map_height: int) -> void:
	width = map_width
	height = map_height
	var cells := width * height
	types.resize(cells)
	types.fill(TYPE_GRASS)
	vertex_heights.resize(vertex_count())
	vertex_heights.fill(0)
	blocked.resize(cells)
	blocked.fill(0)
	placements = []
	map_name = "editor_map"
	map_seed = 20261126
	_undo_stack.clear()
	_redo_stack.clear()


func load_map(bridge: MapEditorBridge, path: String) -> Dictionary:
	var data := bridge.LoadMap(path)
	if data.has("error"):
		return {"ok": false, "error": data["error"]}
	width = int(data["width"])
	height = int(data["height"])
	types = data["types"]
	vertex_heights = data["vertex_heights"]
	blocked = data["blocked"]
	map_name = String(data["name"])
	map_seed = int(data["seed"])
	placements = []
	for entry: Variant in data["placements"]:
		placements.append((entry as Dictionary).duplicate(true))
	_undo_stack.clear()
	_redo_stack.clear()
	return {"ok": true, "error": ""}


func save_map(bridge: MapEditorBridge, path: String) -> Dictionary:
	return bridge.SaveMap(
		path, map_name, map_seed, width, height, types, vertex_heights, blocked, placements)


func validate(bridge: MapEditorBridge) -> Dictionary:
	return bridge.Validate(width, height, types, vertex_heights, blocked, placements)


func in_bounds(cell_x: int, cell_y: int) -> bool:
	return cell_x >= 0 and cell_y >= 0 and cell_x < width and cell_y < height


func vertex_count() -> int:
	return (width + 1) * (height + 1)


func cell_index(cell_x: int, cell_y: int) -> int:
	return cell_y * width + cell_x


func vertex_index(vertex_x: int, vertex_y: int) -> int:
	return vertex_y * (width + 1) + vertex_x


func in_vertex_bounds(vertex_x: int, vertex_y: int) -> bool:
	return vertex_x >= 0 and vertex_y >= 0 and vertex_x <= width and vertex_y <= height


func vertex_height_at(vertex_x: int, vertex_y: int) -> int:
	if not in_vertex_bounds(vertex_x, vertex_y):
		return 0
	return vertex_heights[vertex_index(vertex_x, vertex_y)]


func height_at(cell_x: int, cell_y: int) -> int:
	if not in_bounds(cell_x, cell_y):
		return 0
	var top_left := vertex_height_at(cell_x, cell_y)
	var top_right := vertex_height_at(cell_x + 1, cell_y)
	var bottom_left := vertex_height_at(cell_x, cell_y + 1)
	var bottom_right := vertex_height_at(cell_x + 1, cell_y + 1)
	return maxi(maxi(top_left, top_right), maxi(bottom_left, bottom_right))


func type_at(cell_x: int, cell_y: int) -> int:
	if not in_bounds(cell_x, cell_y):
		return TYPE_CLIFF
	return types[cell_index(cell_x, cell_y)]


func is_blocked(cell_x: int, cell_y: int) -> bool:
	if not in_bounds(cell_x, cell_y):
		return true
	return blocked[cell_index(cell_x, cell_y)] != 0


func begin_stroke() -> void:
	_stroke_active = true
	_stroke_cells.clear()
	_stroke_vertices.clear()
	_stroke_placements_before = placements.duplicate(true)
	_stroke_placements_touched = false


func end_stroke() -> void:
	if not _stroke_active:
		return
	_stroke_active = false
	var cells_before := {}
	var cells_after := {}
	for index: int in _stroke_cells:
		var before: Array = _stroke_cells[index]
		var after := [types[index], blocked[index]]
		if before == after:
			continue
		cells_before[index] = before
		cells_after[index] = after
	var vertices_before := {}
	var vertices_after := {}
	for index: int in _stroke_vertices:
		var before: int = _stroke_vertices[index]
		var after := vertex_heights[index]
		if before == after:
			continue
		vertices_before[index] = before
		vertices_after[index] = after
	if cells_before.is_empty() and vertices_before.is_empty() and not _stroke_placements_touched:
		_stroke_cells.clear()
		_stroke_vertices.clear()
		return
	_undo_stack.append({
		"cells_before": cells_before,
		"cells_after": cells_after,
		"vertices_before": vertices_before,
		"vertices_after": vertices_after,
		"placements_before": _stroke_placements_before,
		"placements_after": placements.duplicate(true),
	})
	_redo_stack.clear()
	_stroke_cells.clear()
	_stroke_vertices.clear()


func can_undo() -> bool:
	return not _undo_stack.is_empty()


func can_redo() -> bool:
	return not _redo_stack.is_empty()


func undo() -> void:
	if _undo_stack.is_empty():
		return
	var entry: Dictionary = _undo_stack.pop_back()
	_apply_entry(entry, true)
	_redo_stack.append(entry)


func redo() -> void:
	if _redo_stack.is_empty():
		return
	var entry: Dictionary = _redo_stack.pop_back()
	_apply_entry(entry, false)
	_undo_stack.append(entry)


func raise_height(center_x: int, center_y: int, radius: int, delta: int) -> void:
	var touched := {}
	for cell_y in range(center_y - radius, center_y + radius + 1):
		for cell_x in range(center_x - radius, center_x + radius + 1):
			if not _in_brush(cell_x, cell_y, center_x, center_y, radius):
				continue
			for vertex: Vector2i in _cell_vertices(cell_x, cell_y):
				var index := vertex_index(vertex.x, vertex.y)
				if touched.has(index):
					continue
				touched[index] = true
				_record_vertex(index)
				vertex_heights[index] = clampi(vertex_heights[index] + delta, 0, MAX_HEIGHT)


func smooth_height(center_x: int, center_y: int, radius: int) -> void:
	var updates := {}
	for cell_y in range(center_y - radius, center_y + radius + 1):
		for cell_x in range(center_x - radius, center_x + radius + 1):
			if not _in_brush(cell_x, cell_y, center_x, center_y, radius):
				continue
			for vertex: Vector2i in _cell_vertices(cell_x, cell_y):
				var index := vertex_index(vertex.x, vertex.y)
				updates[index] = _vertex_neighbor_average(vertex.x, vertex.y)
	for index: int in updates:
		_record_vertex(index)
		vertex_heights[index] = updates[index]


func paint_type(center_x: int, center_y: int, radius: int, terrain_type: int) -> void:
	for cell_y in range(center_y - radius, center_y + radius + 1):
		for cell_x in range(center_x - radius, center_x + radius + 1):
			if not _in_brush(cell_x, cell_y, center_x, center_y, radius):
				continue
			var index := cell_index(cell_x, cell_y)
			_record_cell(index)
			types[index] = terrain_type


func paint_blocked(center_x: int, center_y: int, radius: int, value: bool) -> void:
	for cell_y in range(center_y - radius, center_y + radius + 1):
		for cell_x in range(center_x - radius, center_x + radius + 1):
			if not _in_brush(cell_x, cell_y, center_x, center_y, radius):
				continue
			var index := cell_index(cell_x, cell_y)
			_record_cell(index)
			blocked[index] = 1 if value else 0


func add_placement(
	kind: String, cell_x: int, cell_y: int, team: int, node_type: int, amount: int,
	subtype: int = 0) -> bool:
	if not in_bounds(cell_x, cell_y):
		return false
	placements = placements.filter(func(entry: Dictionary) -> bool:
		return not (entry["kind"] == kind and entry["x"] == cell_x and entry["y"] == cell_y))
	placements.append({
		"kind": kind,
		"x": cell_x,
		"y": cell_y,
		"team": team,
		"node_type": node_type,
		"subtype": subtype,
		"amount": amount,
	})
	_stroke_placements_touched = true
	return true


func remove_placement_at(cell_x: int, cell_y: int) -> bool:
	var remaining := placements.filter(func(entry: Dictionary) -> bool:
		return not (entry["x"] == cell_x and entry["y"] == cell_y))
	if remaining.size() == placements.size():
		return false
	placements = remaining
	_stroke_placements_touched = true
	return true


func placement_at(cell_x: int, cell_y: int) -> Dictionary:
	for entry: Dictionary in placements:
		if entry["x"] == cell_x and entry["y"] == cell_y:
			return entry
	return {}


func _cell_vertices(cell_x: int, cell_y: int) -> Array[Vector2i]:
	return [
		Vector2i(cell_x, cell_y),
		Vector2i(cell_x + 1, cell_y),
		Vector2i(cell_x, cell_y + 1),
		Vector2i(cell_x + 1, cell_y + 1),
	]


func _in_brush(cell_x: int, cell_y: int, center_x: int, center_y: int, radius: int) -> bool:
	if not in_bounds(cell_x, cell_y):
		return false
	return Vector2(cell_x - center_x, cell_y - center_y).length() <= float(radius) + 0.5


func _vertex_neighbor_average(vertex_x: int, vertex_y: int) -> int:
	var total := 0
	var count := 0
	for offset_y in range(-1, 2):
		for offset_x in range(-1, 2):
			var sample_x := vertex_x + offset_x
			var sample_y := vertex_y + offset_y
			if not in_vertex_bounds(sample_x, sample_y):
				continue
			total += vertex_height_at(sample_x, sample_y)
			count += 1
	return roundi(float(total) / float(count))


func _record_cell(index: int) -> void:
	if _stroke_cells.has(index):
		return
	_stroke_cells[index] = [types[index], blocked[index]]


func _record_vertex(index: int) -> void:
	if _stroke_vertices.has(index):
		return
	_stroke_vertices[index] = vertex_heights[index]


func _apply_entry(entry: Dictionary, use_before: bool) -> void:
	var cells: Dictionary = entry["cells_before"] if use_before else entry["cells_after"]
	for index: int in cells:
		var values: Array = cells[index]
		types[index] = values[0]
		blocked[index] = values[1]
	var vertices: Dictionary = entry["vertices_before"] if use_before else entry["vertices_after"]
	for index: int in vertices:
		vertex_heights[index] = vertices[index]
	var placement_key := "placements_before" if use_before else "placements_after"
	placements = (entry[placement_key] as Array).duplicate(true)
