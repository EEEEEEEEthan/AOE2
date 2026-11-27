extends Node3D

const VOXEL_MESHER := preload("res://presentation/voxel_mesher.gd")

const VOXELS_PER_CELL := 16
const TERRAIN_LAYER := 1.0 / float(VOXELS_PER_CELL)

const TOOL_RAISE := 0
const TOOL_LOWER := 1
const TOOL_SMOOTH := 2
const TOOL_PAINT := 3
const TOOL_CLIFF := 4
const TOOL_SPAWN := 5
const TOOL_RESOURCE := 6
const TOOL_BUILDING := 7
const TOOL_UNIT := 8
const TOOL_ERASE := 9
const TOOL_BLOCK := 10
const TOOL_CLEAR := 11

const TOOL_LABELS := [
	"抬升高度",
	"降低高度",
	"平滑高度",
	"涂抹地形",
	"标记悬崖",
	"放置出生点",
	"放置资源",
	"放置建筑",
	"放置单位",
	"移除放置",
	"标记障碍",
	"清除障碍",
]

const RESOURCE_AMOUNT := 300
# 与 C# ResourceSubtype 对齐：木/食/金/石分别落到树/浆果丛/金矿/石矿。
const RESOURCE_SUBTYPES := [1, 2, 3, 4]

const RESOURCE_COLORS := [
	Color(0.36, 0.24, 0.12),
	Color(0.25, 0.6, 0.25),
	Color(0.86, 0.73, 0.16),
	Color(0.52, 0.52, 0.55),
]
const BUILDING_COLOR := Color(0.72, 0.7, 0.66)
const BLOCKED_COLOR := Color(0.9, 0.25, 0.2, 0.35)

const TERRAIN_GRASS_TOP := Color(0.34, 0.5, 0.24)
const TERRAIN_GRASS_SIDE := Color(0.38, 0.29, 0.18)
const TERRAIN_DIRT_TOP := Color(0.46, 0.35, 0.2)
const TERRAIN_DIRT_SIDE := Color(0.36, 0.27, 0.16)
const TERRAIN_CLIFF_TOP := Color(0.45, 0.45, 0.48)
const TERRAIN_CLIFF_SIDE := Color(0.37, 0.37, 0.4)
const TERRAIN_WATER := Color(0.16, 0.36, 0.62)

var _model: MapEditorModel
var _bridge: MapEditorBridge
var _terrain_mesh: MeshInstance3D
var _blocked_overlay: MultiMeshInstance3D
var _terrain_material: StandardMaterial3D
var _placement_materials: Dictionary = {}
var _terrain_dirty := true
var _markers_dirty := true
var _painting := false
var _last_tile := Vector2i(-1, -1)
var _building_options: Array = []
var _building_categories: Array = []
var _building_list_ids: Array = []
var _unit_options: Array = []
var _unit_categories: Array = []
var _unit_list_ids: Array = []
var _selected_building_type := 0
var _selected_unit_type := -1


func _ready() -> void:
	_model = MapEditorModel.new()
	_model.new_map(32, 32)
	_bridge = MapEditorBridge.new()
	add_child(_bridge)
	_setup_menus()
	_setup_civ_options()
	_setup_team_options(int(%PlayerCountSpin.value))
	_setup_building_palette()
	_setup_unit_palette()
	_fit_camera()


func _process(_delta: float) -> void:
	if _terrain_dirty:
		_terrain_dirty = false
		_rebuild_terrain()
	if _markers_dirty:
		_markers_dirty = false
		_rebuild_markers()
		_rebuild_blocked_overlay()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var mouse_button := event as InputEventMouseButton
		if mouse_button.button_index != MOUSE_BUTTON_LEFT:
			return
		if mouse_button.pressed:
			_begin_paint(mouse_button.position)
		else:
			_end_paint()
	elif event is InputEventMouseMotion and _painting:
		_continue_paint((event as InputEventMouseMotion).position)


func _unhandled_key_input(event: InputEvent) -> void:
	var key_event := event as InputEventKey
	if key_event == null or not key_event.pressed:
		return
	if key_event.is_command_or_control_pressed() and key_event.keycode == KEY_Z:
		if key_event.shift_pressed:
			_on_redo_pressed()
		else:
			_on_undo_pressed()
	elif key_event.is_command_or_control_pressed() and key_event.keycode == KEY_Y:
		_on_redo_pressed()
	elif key_event.is_command_or_control_pressed() and key_event.keycode == KEY_S:
		_on_save_pressed()


func _setup_menus() -> void:
	var menu_bar: MenuBar = %MenuBar
	menu_bar.set_menu_title(0, "文件")
	menu_bar.set_menu_title(1, "编辑")
	menu_bar.set_menu_title(2, "地图")
	menu_bar.set_menu_title(3, "玩家")
	menu_bar.set_menu_title(4, "视图")
	%FileMenu.add_item("新建地图", 0)
	%FileMenu.add_item("打开地图…", 1)
	%FileMenu.add_item("保存地图", 2)
	%FileMenu.add_item("保存并测试开局", 3)
	%FileMenu.add_separator()
	%FileMenu.add_item("返回主菜单", 4)
	%FileMenu.id_pressed.connect(_on_file_menu_pressed)
	%EditMenu.add_item("撤销", 0)
	%EditMenu.add_item("重做", 1)
	%EditMenu.add_separator()
	%EditMenu.add_item("清空放置", 2)
	%EditMenu.id_pressed.connect(_on_edit_menu_pressed)
	%MapMenu.add_item("地图尺寸…", 0)
	%MapMenu.add_item("校验地图", 1)
	%MapMenu.add_separator()
	%MapMenu.add_item("清除全部障碍", 2)
	%MapMenu.id_pressed.connect(_on_map_menu_pressed)
	%PlayerMenu.add_item("显示/隐藏玩家设置", 0)
	%PlayerMenu.id_pressed.connect(_on_player_menu_pressed)
	%ViewMenu.add_item("重置视角", 0)
	%ViewMenu.id_pressed.connect(_on_view_menu_pressed)


func _setup_civ_options() -> void:
	var probe := GameBridge.new()
	for civ in probe.CivilizationCount:
		%CivOption.add_item(probe.CivilizationName(civ))
	probe.free()


func _setup_team_options(player_count: int) -> void:
	%TeamOption.clear()
	for team in player_count:
		%TeamOption.add_item("玩家 %d" % (team + 1))


func _setup_building_palette() -> void:
	_building_options = _bridge.BuildingOptions()
	_building_categories = _distinct_categories(_building_options)
	for category in _building_categories:
		%BuildingCategory.add_item(category)
	_refresh_building_list()


func _setup_unit_palette() -> void:
	_unit_options = _bridge.UnitOptions()
	_unit_categories = _distinct_categories(_unit_options)
	for category in _unit_categories:
		%UnitCategory.add_item(category)
	_refresh_unit_list()


func _distinct_categories(options: Array) -> Array:
	var categories: Array = []
	for option: Dictionary in options:
		var category := String(option["category"])
		if not categories.has(category):
			categories.append(category)
	return categories


func _refresh_building_list() -> void:
	%BuildingList.clear()
	_building_list_ids = []
	var category: String = _building_categories[%BuildingCategory.selected] if _building_categories.size() > 0 else ""
	for option: Dictionary in _building_options:
		if String(option["category"]) != category:
			continue
		_building_list_ids.append(int(option["id"]))
		%BuildingList.add_item(String(option["name"]))


func _refresh_unit_list() -> void:
	%UnitList.clear()
	_unit_list_ids = []
	var category: String = _unit_categories[%UnitCategory.selected] if _unit_categories.size() > 0 else ""
	for option: Dictionary in _unit_options:
		if String(option["category"]) != category:
			continue
		_unit_list_ids.append(int(option["id"]))
		%UnitList.add_item(String(option["name"]))


func _fit_camera() -> void:
	var camera: IsoCamera = %Camera3D
	camera.grid_width = float(_model.width)
	camera.grid_height = float(_model.height)
	camera.focus = Vector3(_model.width * 0.5, 0.0, _model.height * 0.5)
	camera.refresh_view()


func _begin_paint(screen_position: Vector2) -> void:
	var tile := _tile_at(screen_position)
	if not _model.in_bounds(tile.x, tile.y):
		return
	_painting = true
	_last_tile = tile
	_model.begin_stroke()
	_apply_tool(tile)
	_mark_dirty()


func _continue_paint(screen_position: Vector2) -> void:
	var tile := _tile_at(screen_position)
	if not _model.in_bounds(tile.x, tile.y) or tile == _last_tile:
		return
	_last_tile = tile
	_apply_tool(tile)
	_mark_dirty()


func _end_paint() -> void:
	if not _painting:
		return
	_painting = false
	_model.end_stroke()


func _apply_tool(tile: Vector2i) -> void:
	var radius := int(%RadiusSpin.value)
	var tool: int = %ToolOption.selected
	match tool:
		TOOL_RAISE:
			_model.raise_height(tile.x, tile.y, radius, 1)
		TOOL_LOWER:
			_model.raise_height(tile.x, tile.y, radius, -1)
		TOOL_SMOOTH:
			_model.smooth_height(tile.x, tile.y, radius)
		TOOL_PAINT:
			_model.paint_type(tile.x, tile.y, radius, %TypeOption.selected)
		TOOL_CLIFF:
			_model.paint_type(tile.x, tile.y, radius, MapEditorModel.TYPE_CLIFF)
		TOOL_SPAWN:
			_model.add_placement(
				MapEditorModel.KIND_SPAWN, tile.x, tile.y, %TeamOption.selected, 0, 0)
		TOOL_RESOURCE:
			_model.add_placement(
				MapEditorModel.KIND_RESOURCE, tile.x, tile.y, 0,
				%NodeOption.selected, RESOURCE_AMOUNT,
				RESOURCE_SUBTYPES[%NodeOption.selected])
		TOOL_BUILDING:
			_model.add_placement(
				MapEditorModel.KIND_BUILDING, tile.x, tile.y, %TeamOption.selected, 0, 0,
				0, _selected_building_type)
		TOOL_UNIT:
			_model.add_placement(
				MapEditorModel.KIND_UNIT, tile.x, tile.y, %TeamOption.selected, 0, 0,
				0, 0, _selected_unit_type)
		TOOL_ERASE:
			_model.remove_placement_at(tile.x, tile.y)
		TOOL_BLOCK:
			_model.paint_blocked(tile.x, tile.y, radius, true)
		TOOL_CLEAR:
			_model.paint_blocked(tile.x, tile.y, radius, false)


func _tile_at(screen_position: Vector2) -> Vector2i:
	var camera: IsoCamera = %Camera3D
	var origin := camera.project_ray_origin(screen_position)
	var direction := camera.project_ray_normal(screen_position)
	if is_zero_approx(direction.y):
		return Vector2i(-1, -1)
	var distance := -origin.y / direction.y
	if distance <= 0.0:
		return Vector2i(-1, -1)
	var point := origin + direction * distance
	return Vector2i(floori(point.x), floori(point.z))


func _mark_dirty() -> void:
	_terrain_dirty = true
	_markers_dirty = true


func _rebuild_terrain() -> void:
	var cells := _model.width * _model.height
	var cell_colors := PackedColorArray()
	var cell_side_colors := PackedColorArray()
	var cell_drops := PackedInt32Array()
	cell_colors.resize(cells)
	cell_side_colors.resize(cells)
	cell_drops.resize(cells)
	for index in cells:
		var type: int = _model.types[index]
		cell_colors[index] = _terrain_color(type, true)
		cell_side_colors[index] = _terrain_color(type, false)
		cell_drops[index] = 1 if type == MapEditorModel.TYPE_WATER else 0
	if _terrain_mesh == null:
		_terrain_mesh = MeshInstance3D.new()
		_terrain_mesh.name = "VoxelTerrain"
		_terrain_mesh.material_override = _get_terrain_material()
		_terrain_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		%TerrainRoot.add_child(_terrain_mesh)
	_terrain_mesh.mesh = VOXEL_MESHER.heightfield_mesh(
		_model.vertex_heights, _model.width, _model.height, TERRAIN_LAYER,
		cell_colors, cell_side_colors, cell_drops)


func _rebuild_markers() -> void:
	for child in %MarkerRoot.get_children():
		child.queue_free()
	for placement: Dictionary in _model.placements:
		var marker := MeshInstance3D.new()
		var visual := _placement_visual(placement["kind"])
		marker.mesh = visual["mesh"]
		marker.material_override = _get_placement_material(placement)
		var base := _model.height_at(placement["x"], placement["y"]) * TERRAIN_LAYER
		marker.position = Vector3(
			placement["x"] + 0.5, base + visual["height"], placement["y"] + 0.5)
		%MarkerRoot.add_child(marker)


func _rebuild_blocked_overlay() -> void:
	var cells: Array[Vector3] = []
	for tile_y in _model.height:
		for tile_x in _model.width:
			if not _model.is_blocked(tile_x, tile_y):
				continue
			var level := _model.height_at(tile_x, tile_y) * TERRAIN_LAYER
			cells.append(Vector3(tile_x + 0.5, level + 0.02, tile_y + 0.5))
	if _blocked_overlay == null:
		var quad := PlaneMesh.new()
		quad.size = Vector2(1.0, 1.0)
		var material := StandardMaterial3D.new()
		material.albedo_color = Color(1.0, 1.0, 1.0, 1.0)
		material.vertex_color_use_as_albedo = true
		material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		var multimesh := MultiMesh.new()
		multimesh.transform_format = MultiMesh.TRANSFORM_3D
		multimesh.use_colors = true
		multimesh.mesh = quad
		_blocked_overlay = MultiMeshInstance3D.new()
		_blocked_overlay.name = "BlockedOverlay"
		_blocked_overlay.multimesh = multimesh
		_blocked_overlay.material_override = material
		%TerrainRoot.add_child(_blocked_overlay)
	var multimesh := _blocked_overlay.multimesh
	multimesh.instance_count = cells.size()
	var basis := Basis(Vector3.RIGHT, -PI / 2.0)
	for index in cells.size():
		multimesh.set_instance_transform(index, Transform3D(basis, cells[index]))
		multimesh.set_instance_color(index, BLOCKED_COLOR)


func _placement_visual(kind: String) -> Dictionary:
	match kind:
		MapEditorModel.KIND_SPAWN:
			return {"mesh": _cylinder_mesh(0.28, 1.1), "height": 0.55}
		MapEditorModel.KIND_RESOURCE:
			return {"mesh": _box_mesh(Vector3(0.6, 0.6, 0.6)), "height": 0.3}
		MapEditorModel.KIND_UNIT:
			return {"mesh": _cylinder_mesh(0.22, 0.9), "height": 0.45}
		_:
			return {"mesh": _box_mesh(Vector3(1.0, 0.9, 1.0)), "height": 0.45}


func _cylinder_mesh(radius: float, height: float) -> Mesh:
	var mesh := CylinderMesh.new()
	mesh.top_radius = radius
	mesh.bottom_radius = radius
	mesh.height = height
	return mesh


func _box_mesh(size: Vector3) -> Mesh:
	var mesh := BoxMesh.new()
	mesh.size = size
	return mesh


func _get_placement_material(placement: Dictionary) -> StandardMaterial3D:
	var key := "%s|%d|%d" % [placement["kind"], placement["team"], placement["node_type"]]
	if _placement_materials.has(key):
		return _placement_materials[key]
	var material := StandardMaterial3D.new()
	material.albedo_color = _placement_color(placement)
	material.roughness = 0.8
	_placement_materials[key] = material
	return material


func _placement_color(placement: Dictionary) -> Color:
	match String(placement["kind"]):
		MapEditorModel.KIND_SPAWN, MapEditorModel.KIND_UNIT:
			return PlayerColors.color_of(int(placement["team"]))
		MapEditorModel.KIND_RESOURCE:
			return RESOURCE_COLORS[int(placement["node_type"]) % RESOURCE_COLORS.size()]
		_:
			return BUILDING_COLOR


func _get_terrain_material() -> StandardMaterial3D:
	if _terrain_material != null:
		return _terrain_material
	_terrain_material = StandardMaterial3D.new()
	_terrain_material.vertex_color_use_as_albedo = true
	_terrain_material.roughness = 0.9
	_terrain_material.cull_mode = BaseMaterial3D.CULL_DISABLED
	return _terrain_material


func _terrain_color(terrain_type: int, is_top: bool) -> Color:
	match terrain_type:
		MapEditorModel.TYPE_DIRT:
			return TERRAIN_DIRT_TOP if is_top else TERRAIN_DIRT_SIDE
		MapEditorModel.TYPE_WATER:
			return TERRAIN_WATER
		MapEditorModel.TYPE_CLIFF:
			return TERRAIN_CLIFF_TOP if is_top else TERRAIN_CLIFF_SIDE
		_:
			return TERRAIN_GRASS_TOP if is_top else TERRAIN_GRASS_SIDE



func _set_status(text: String) -> void:
	%StatusLabel.text = text


func _on_file_menu_pressed(id: int) -> void:
	match id:
		0:
			_on_new_pressed()
		1:
			%OpenDialog.popup_centered_ratio(0.7)
		2:
			_on_save_pressed()
		3:
			_on_quick_test_pressed()
		4:
			_on_main_menu_pressed()


func _on_edit_menu_pressed(id: int) -> void:
	match id:
		0:
			_on_undo_pressed()
		1:
			_on_redo_pressed()
		2:
			_model.begin_stroke()
			_model.clear_placements()
			_model.end_stroke()
			_mark_dirty()
			_set_status("已清空放置")


func _on_map_menu_pressed(id: int) -> void:
	match id:
		0:
			%SizeWidthSpin.value = _model.width
			%SizeHeightSpin.value = _model.height
			%SizeDialog.popup_centered()
		1:
			_on_validate_pressed()
		2:
			_model.begin_stroke()
			_model.clear_blocked()
			_model.end_stroke()
			_mark_dirty()
			_set_status("已清除全部障碍标记")


func _on_player_menu_pressed(_id: int) -> void:
	%PlayerPanel.visible = not %PlayerPanel.visible


func _on_view_menu_pressed(_id: int) -> void:
	_fit_camera()
	_set_status("已重置视角")


func _on_open_file_selected(path: String) -> void:
	%PathEdit.text = path
	_on_load_pressed()


func _on_size_confirmed() -> void:
	_model.new_map(int(%SizeWidthSpin.value), int(%SizeHeightSpin.value))
	_mark_dirty()
	_fit_camera()
	_set_status("已新建 %d×%d 地图" % [_model.width, _model.height])


func _on_tool_selected(index: int) -> void:
	_set_status("当前工具：%s" % TOOL_LABELS[index])


func _on_building_category_selected(_index: int) -> void:
	_refresh_building_list()


func _on_unit_category_selected(_index: int) -> void:
	_refresh_unit_list()


func _on_building_selected(index: int) -> void:
	_selected_building_type = _building_list_ids[index]
	%ToolOption.select(TOOL_BUILDING)
	_set_status("放置建筑：%s（左键点击地图）" % %BuildingList.get_item_text(index))


func _on_unit_selected(index: int) -> void:
	_selected_unit_type = _unit_list_ids[index]
	%ToolOption.select(TOOL_UNIT)
	_set_status("放置单位：%s（左键点击地图）" % %UnitList.get_item_text(index))


func _on_player_count_changed(value: float) -> void:
	_setup_team_options(int(value))
	_set_status("玩家数量：%d（出生点按阵营颜色区分）" % int(value))


func _on_main_menu_pressed() -> void:
	get_tree().change_scene_to_file("res://presentation/main_menu.tscn")


func _on_undo_pressed() -> void:
	_model.undo()
	_mark_dirty()
	_set_status("已撤销")


func _on_redo_pressed() -> void:
	_model.redo()
	_mark_dirty()
	_set_status("已重做")


func _on_new_pressed() -> void:
	_model.new_map(32, 32)
	_mark_dirty()
	_fit_camera()
	_set_status("已新建 32×32 地图")


func _on_save_pressed() -> bool:
	var path: String = %PathEdit.text.strip_edges()
	if path.is_empty():
		_set_status("请填写保存路径")
		return false
	var result := _model.save_map(_bridge, path)
	if result.get("ok", false):
		_set_status("已保存到 %s" % path)
		return true
	_set_status("保存失败：%s" % result.get("error", "未知错误"))
	return false


func _on_quick_test_pressed() -> void:
	if not _on_save_pressed():
		return
	MatchSetup.reset_config()
	MatchSetup.config["map_path"] = %PathEdit.text.strip_edges()
	MatchSetup.config["map_name"] = String(%PathEdit.text.strip_edges()).get_file().get_basename()
	get_tree().change_scene_to_file("res://presentation/loading.tscn")


func _on_load_pressed() -> void:
	var path: String = %PathEdit.text.strip_edges()
	if path.is_empty():
		_set_status("请填写读取路径")
		return
	var result := _model.load_map(_bridge, path)
	if not result["ok"]:
		_set_status("读取失败：%s" % result["error"])
		return
	_mark_dirty()
	_fit_camera()
	_set_status("已读取 %s（%d×%d）" % [path, _model.width, _model.height])


func _on_validate_pressed() -> void:
	var result := _model.validate(_bridge)
	var lines := PackedStringArray()
	lines.append("出生点连通：%s" % ("通过" if result["spawn_connected"] else "未通过"))
	lines.append("资源可达：%s" % ("通过" if result["resources_reachable"] else "未通过"))
	lines.append("出生点 %d 个，资源点 %d 个" % [result["spawn_count"], result["resource_count"]])
	for message in result["messages"]:
		lines.append("· %s" % message)
	%ValidationDialog.dialog_text = "\n".join(lines)
	%ValidationDialog.popup_centered()
	_set_status("校验通过" if result["ok"] else "校验未通过")
