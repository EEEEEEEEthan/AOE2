class_name WorldView
extends Node3D

const VOXEL_MESHER := preload("res://presentation/voxel_mesher.gd")
const VOX_MODEL := preload("res://presentation/vox_model.gd")

const VOXELS_PER_CELL := 16
# 纯单位口径：地形横向按格（四顶点高度场）生成，不切 16×16 子体素；
# TERRAIN_LAYER 只负责把高度层换算成 1/16 单位，模型与高度层共用这一密度。
const TERRAIN_LAYER := 1.0 / float(VOXELS_PER_CELL)

const TEAM_COLORS := {
	0: Color(0.82, 0.22, 0.2),
	1: Color(0.2, 0.42, 0.85),
}
const VILLAGER_COLORS := {
	0: Color(0.95, 0.58, 0.46),
	1: Color(0.46, 0.66, 0.95),
}
const RESOURCE_NODE_COLORS := [
	Color(0.36, 0.24, 0.12),
	Color(0.25, 0.6, 0.25),
	Color(0.86, 0.73, 0.16),
	Color(0.52, 0.52, 0.55),
]
const DROP_SITE_COLOR := Color(0.25, 0.75, 0.8)

const KIND_UNIT := 0
const KIND_VILLAGER := 1
const KIND_RESOURCE_NODE := 2
const KIND_DROP_SITE := 3
const KIND_BUILDING := 8

const BUILDING_STATE_FOUNDATION := 0
const BUILDING_STATE_COMPLETE := 1
const BUILDING_STATE_RUINS := 2
const BUILDING_TYPE_GATE := 13
const GATE_STATE_OPEN := 1
const GATE_STATE_LOCKED := 2
const GATE_OPEN_MODEL_PATH := "res://assets/vox/gate_open.vox"
const BUILDING_RUIN_COLOR := Color(0.24, 0.22, 0.2)
const BUILDING_FOUNDATION_TINT := Color(0.72, 0.66, 0.55)

const TERRAIN_GRASS_TOP := Color(0.34, 0.5, 0.24)
const TERRAIN_GRASS_SIDE := Color(0.38, 0.29, 0.18)
const TERRAIN_DIRT_TOP := Color(0.46, 0.35, 0.2)
const TERRAIN_DIRT_SIDE := Color(0.36, 0.27, 0.16)
const TERRAIN_CLIFF_TOP := Color(0.45, 0.45, 0.48)
const TERRAIN_CLIFF_SIDE := Color(0.37, 0.37, 0.4)
const TERRAIN_WATER := Color(0.16, 0.36, 0.62)

const MODEL_PATHS := {
	KIND_UNIT: "res://assets/vox/soldier.vox",
	KIND_VILLAGER: "res://assets/vox/villager.vox",
	KIND_RESOURCE_NODE: "res://assets/vox/tree.vox",
	KIND_DROP_SITE: "res://assets/vox/town_center.vox",
}

# 建筑类型（BuildingTypes）到完工模型的映射；建筑按 1 格 = 16 体素建模，缩放即贴合 footprint。
const BUILDING_MODEL_PATHS := {
	1: "res://assets/vox/town_center_hall.vox",
	2: "res://assets/vox/house.vox",
	3: "res://assets/vox/barracks.vox",
	4: "res://assets/vox/wall.vox",
	5: "res://assets/vox/tower.vox",
	6: "res://assets/vox/market.vox",
	7: "res://assets/vox/farm.vox",
	12: "res://assets/vox/palisade_wall.vox",
	13: "res://assets/vox/gate.vox",
	14: "res://assets/vox/fortified_wall.vox",
}

const FOG_UNEXPLORED_COLOR := Color(0.0, 0.0, 0.0, 1.0)
const FOG_EXPLORED_COLOR := Color(0.32, 0.34, 0.38, 0.55)
const FOG_VISIBLE_COLOR := Color(0.0, 0.0, 0.0, 0.0)
const FOG_OFFSET := 0.04

var _voxel_material: StandardMaterial3D
var _models: Dictionary = {}
var _unit_meshes: Dictionary = {}
var _units: Dictionary = {}
var _fog: MultiMeshInstance3D
var _fog_states: PackedByteArray = PackedByteArray()
var _fog_width := 0
var _fog_height := 0
var _terrain_width := 0
var _terrain_height := 0
var _terrain_levels: PackedInt32Array = PackedInt32Array()
var _bridge: GameBridge
var _selected_ids: Array = []
var _entity_positions: Dictionary = {}
var _selection_mesh: MultiMeshInstance3D
var _placement_mesh: MeshInstance3D
var _placement_material: StandardMaterial3D


func build_terrain(bridge: GameBridge) -> void:
	_bridge = bridge
	_terrain_width = bridge.TerrainWidth
	_terrain_height = bridge.TerrainHeight
	if _terrain_width == 0 or _terrain_height == 0:
		return
	var types := bridge.TerrainTypes()
	var vertex_heights := PackedInt32Array(bridge.TerrainVertexHeights())
	if vertex_heights.size() != (_terrain_width + 1) * (_terrain_height + 1):
		return
	var stride := _terrain_width + 1
	var cells := _terrain_width * _terrain_height
	var cell_colors := PackedColorArray()
	var cell_side_colors := PackedColorArray()
	var cell_drops := PackedInt32Array()
	cell_colors.resize(cells)
	cell_side_colors.resize(cells)
	cell_drops.resize(cells)
	_terrain_levels.resize(cells)
	for tile_y in _terrain_height:
		for tile_x in _terrain_width:
			var index := tile_y * _terrain_width + tile_x
			var base := tile_y * stride + tile_x
			var level := maxi(
				maxi(vertex_heights[base], vertex_heights[base + 1]),
				maxi(vertex_heights[base + stride], vertex_heights[base + stride + 1]))
			var is_water: bool = types[index] == 2
			var drop := 1 if is_water else 0
			_terrain_levels[index] = level - drop
			cell_colors[index] = _terrain_color(types[index], true)
			cell_side_colors[index] = _terrain_color(types[index], false)
			cell_drops[index] = drop
	var mesh: ArrayMesh = VOXEL_MESHER.heightfield_mesh(
		vertex_heights, _terrain_width, _terrain_height, TERRAIN_LAYER,
		cell_colors, cell_side_colors, cell_drops)
	var terrain := MeshInstance3D.new()
	terrain.name = "VoxelTerrain"
	terrain.mesh = mesh
	terrain.material_override = _get_voxel_material()
	terrain.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(terrain)
	_build_fog(_terrain_width, _terrain_height)


func refresh(bridge: GameBridge) -> void:
	var batch: Dictionary = bridge.SnapshotBatch()
	if not batch.is_empty():
		_refresh_entities(batch)
	_refresh_fog(bridge)


func _refresh_entities(batch: Dictionary) -> void:
	var ids: PackedInt64Array = batch["ids"]
	var positions_x: PackedFloat32Array = batch["x"]
	var positions_y: PackedFloat32Array = batch["y"]
	var teams: PackedInt32Array = batch["team"]
	var kinds: PackedInt32Array = batch["kind"]
	var node_types: PackedInt32Array = batch["node_type"]
	var node_remainings: PackedInt32Array = batch["node_remaining"]
	var building_types: PackedInt32Array = batch["building_type"]
	var widths: PackedInt32Array = batch["width"]
	var heights: PackedInt32Array = batch["height"]
	var building_states: PackedInt32Array = batch["building_state"]
	var building_progresses: PackedFloat32Array = batch["building_progress"]
	var building_max_progresses: PackedFloat32Array = batch["building_max_progress"]
	var gate_states: PackedInt32Array = batch["gate_state"]
	var alive: Dictionary = {}
	_entity_positions.clear()
	for bucket: Dictionary in _unit_meshes.values():
		bucket["transforms"].clear()
	for index in int(batch["count"]):
		var kind: int = kinds[index]
		if kind == KIND_BUILDING:
			var id: int = ids[index]
			alive[id] = true
			var building: Node3D = _units.get(id)
			if building == null:
				building = _create_building()
				_units[id] = building
			_update_building(building, {
				"id": id,
				"x": positions_x[index],
				"y": positions_y[index],
				"team": teams[index],
				"kind": kind,
				"node_type": node_types[index],
				"node_remaining": node_remainings[index],
				"building_type": building_types[index],
				"width": widths[index],
				"height": heights[index],
				"building_state": building_states[index],
				"building_progress": building_progresses[index],
				"building_max_progress": building_max_progresses[index],
				"gate_state": gate_states[index],
			})
			var size_w: int = maxi(1, widths[index])
			var size_h: int = maxi(1, heights[index])
			_entity_positions[id] = Vector3(
				positions_x[index] - 0.5 + size_w * 0.5,
				_ground_level_rect(positions_x[index], positions_y[index], size_w, size_h),
				positions_y[index] - 0.5 + size_h * 0.5)
		elif _is_visible_unit(kind, node_remainings[index]):
			_append_unit(
				int(ids[index]), kind, teams[index], node_types[index],
				positions_x[index], positions_y[index])
	for id in _units.keys():
		if not alive.has(id):
			var dead: Node3D = _units[id]
			dead.queue_free()
			_units.erase(id)
	_commit_units()
	_refresh_selection_rings()


func _is_visible_unit(kind: int, node_remaining: int) -> bool:
	if kind == KIND_RESOURCE_NODE:
		return node_remaining > 0
	return true


func _append_unit(
	id: int, kind: int, team: int, node_type: int, tile_x: float, tile_y: float) -> void:
	var path: String = MODEL_PATHS.get(kind, MODEL_PATHS[KIND_UNIT])
	var bucket: Dictionary = _unit_bucket(path, _color_for(kind, team, node_type))
	var ground := _ground_level(tile_x, tile_y)
	var origin: Vector3 = Vector3(tile_x, ground, tile_y) + (bucket["offset"] as Vector3)
	bucket["transforms"].append(Transform3D(Basis(), origin))
	_entity_positions[id] = Vector3(tile_x, ground, tile_y)


func _unit_bucket(path: String, color: Color) -> Dictionary:
	var key := "%s|%d" % [path, color.to_rgba32()]
	var cached: Dictionary = _unit_meshes.get(key, {})
	if not cached.is_empty():
		return cached
	var model := _model_for(path, color)
	var multimesh := MultiMesh.new()
	multimesh.transform_format = MultiMesh.TRANSFORM_3D
	multimesh.mesh = model["mesh"]
	multimesh.instance_count = 0
	var instance := MultiMeshInstance3D.new()
	instance.multimesh = multimesh
	instance.material_override = _get_voxel_material()
	instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(instance)
	var bucket := {
		"multimesh": multimesh,
		"offset": model["offset"],
		"transforms": [],
	}
	_unit_meshes[key] = bucket
	return bucket


func _commit_units() -> void:
	for bucket: Dictionary in _unit_meshes.values():
		var transforms: Array = bucket["transforms"]
		var multimesh: MultiMesh = bucket["multimesh"]
		multimesh.instance_count = transforms.size()
		for index in transforms.size():
			multimesh.set_instance_transform(index, transforms[index])


func _create_building() -> Node3D:
	var building := Node3D.new()
	var visual := MeshInstance3D.new()
	visual.name = "VoxelBuilding"
	visual.material_override = _get_voxel_material()
	visual.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	building.add_child(visual)
	add_child(building)
	return building


func _update_building(building: Node3D, entity: Dictionary) -> void:
	var state: int = entity["building_state"]
	var type: int = int(entity.get("building_type", 0))
	var path: String = BUILDING_MODEL_PATHS.get(type, "")
	if state == BUILDING_STATE_COMPLETE and not path.is_empty():
		if type == BUILDING_TYPE_GATE and int(entity.get("gate_state", 0)) == GATE_STATE_OPEN:
			path = GATE_OPEN_MODEL_PATH
		_show_building_model(building, entity, path)
	else:
		_show_building_block(building, entity, state)


func _show_building_model(building: Node3D, entity: Dictionary, path: String) -> void:
	var size_w: int = maxi(1, int(entity["width"]))
	var size_h: int = maxi(1, int(entity["height"]))
	var team_color: Color = TEAM_COLORS.get(entity["team"], Color(0.7, 0.7, 0.7))
	if int(entity.get("building_type", 0)) == BUILDING_TYPE_GATE and int(entity.get("gate_state", 0)) == GATE_STATE_LOCKED:
		team_color = team_color.darkened(0.45)
	var model := _model_for(path, team_color)
	var visual := building.get_node("VoxelBuilding") as MeshInstance3D
	var key := "model:%s:%d" % [path, team_color.to_rgba32()]
	if visual.get_meta("vox_key", "") != key:
		visual.mesh = model["mesh"]
		visual.position = model["offset"]
		visual.set_meta("vox_key", key)
	# 模型几何中心落在 footprint 中心，底面贴地；多格建筑取占地最高格避免陷入坡面。
	building.position = Vector3(
		entity["x"] - 0.5 + size_w * 0.5,
		_ground_level_rect(entity["x"], entity["y"], size_w, size_h),
		entity["y"] - 0.5 + size_h * 0.5)


func _show_building_block(building: Node3D, entity: Dictionary, state: int) -> void:
	var size_w: int = maxi(1, int(entity["width"]))
	var size_h: int = maxi(1, int(entity["height"]))
	var color := _building_color(entity, state)
	var layers := _building_layers(entity, state)
	var key := "%d:%d:%d:%d" % [size_w, size_h, layers, color.to_rgba32()]
	var visual := building.get_node("VoxelBuilding") as MeshInstance3D
	if visual.get_meta("vox_key", "") != key:
		var voxels := {}
		for voxel_y in range(0, layers):
			for voxel_z in range(0, size_h):
				for voxel_x in range(0, size_w):
					voxels[Vector3i(voxel_x, voxel_y, voxel_z)] = color
		visual.mesh = VOXEL_MESHER.greedy_mesh(
			voxels, Vector3(1.0, TERRAIN_LAYER, 1.0), true)
		visual.position = Vector3.ZERO
		visual.set_meta("vox_key", key)
	building.position = Vector3(
		entity["x"] - 0.5,
		_ground_level_rect(entity["x"], entity["y"], size_w, size_h),
		entity["y"] - 0.5)


func _building_layers(entity: Dictionary, state: int) -> int:
	# 层数翻倍，配合 TERRAIN_LAYER = 1/16 保持地基/废墟块的视觉高度不变。
	if state == BUILDING_STATE_RUINS:
		return 2
	if state == BUILDING_STATE_COMPLETE:
		return 22
	var progress: float = entity["building_progress"]
	var max_progress: float = max(1.0, entity["building_max_progress"])
	return 4 + int(round(clamp(progress / max_progress, 0.0, 1.0) * 18.0))


func _building_color(entity: Dictionary, state: int) -> Color:
	if state == BUILDING_STATE_RUINS:
		return BUILDING_RUIN_COLOR
	var team_color: Color = TEAM_COLORS.get(entity["team"], Color(0.7, 0.7, 0.7))
	if state == BUILDING_STATE_COMPLETE:
		return team_color.darkened(0.15)
	return BUILDING_FOUNDATION_TINT.lerp(team_color, 0.35)


func _model_for(path: String, color: Color, scale: float = TERRAIN_LAYER) -> Dictionary:
	var key := "%s|%d|%.4f" % [path, color.to_rgba32(), scale]
	if _models.has(key):
		return _models[key]
	var model := VOX_MODEL.load_model(path, color, scale)
	_models[key] = model
	return model


func _get_voxel_material() -> StandardMaterial3D:
	if _voxel_material != null:
		return _voxel_material
	_voxel_material = StandardMaterial3D.new()
	_voxel_material.vertex_color_use_as_albedo = true
	_voxel_material.roughness = 0.9
	# 贪心网格已剔除内部面；关闭背面剔除让单位/地形没有绕序瑕疵。
	_voxel_material.cull_mode = BaseMaterial3D.CULL_DISABLED
	return _voxel_material


func _terrain_color(type: int, is_top: bool) -> Color:
	match type:
		1:
			return TERRAIN_DIRT_TOP if is_top else TERRAIN_DIRT_SIDE
		2:
			return TERRAIN_WATER
		3:
			return TERRAIN_CLIFF_TOP if is_top else TERRAIN_CLIFF_SIDE
		_:
			return TERRAIN_GRASS_TOP if is_top else TERRAIN_GRASS_SIDE


func _ground_level(tile_x: float, tile_y: float) -> float:
	var x := int(floor(tile_x))
	var y := int(floor(tile_y))
	if x < 0 or y < 0 or x >= _terrain_width or y >= _terrain_height:
		return 0.0
	var index := y * _terrain_width + x
	if index >= _terrain_levels.size():
		return 0.0
	return _terrain_levels[index] * TERRAIN_LAYER


func _ground_level_rect(tile_x: float, tile_y: float, size_w: int, size_h: int) -> float:
	var x := int(floor(tile_x))
	var y := int(floor(tile_y))
	var level := 0
	for cell_y in size_h:
		for cell_x in size_w:
			level = maxi(level, _height_at(x + cell_x, y + cell_y))
	return level * TERRAIN_LAYER


func _color_for(kind: int, team: int, node_type: int) -> Color:
	if kind == KIND_RESOURCE_NODE:
		return RESOURCE_NODE_COLORS[node_type]
	if kind == KIND_DROP_SITE:
		return DROP_SITE_COLOR
	return TEAM_COLORS.get(team, Color(0.8, 0.8, 0.8))


func _build_fog(width: int, height: int) -> void:
	_fog_width = width
	_fog_height = height
	_fog_states = PackedByteArray()
	var quad := QuadMesh.new()
	quad.size = Vector2(1.0, 1.0)
	var mesh := MultiMesh.new()
	mesh.transform_format = MultiMesh.TRANSFORM_3D
	mesh.use_colors = true
	mesh.mesh = quad
	mesh.instance_count = width * height
	var basis := Basis(Vector3.RIGHT, -PI / 2.0)
	for y in height:
		for x in width:
			var origin := Vector3(x + 0.5, _height_at(x, y) * TERRAIN_LAYER + FOG_OFFSET, y + 0.5)
			mesh.set_instance_transform(y * width + x, Transform3D(basis, origin))
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(1.0, 1.0, 1.0, 1.0)
	material.vertex_color_use_as_albedo = true
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	_fog = MultiMeshInstance3D.new()
	_fog.name = "Fog"
	_fog.multimesh = mesh
	_fog.material_override = material
	add_child(_fog)


func _height_at(tile_x: int, tile_y: int) -> int:
	var index := tile_y * _terrain_width + tile_x
	if index < 0 or index >= _terrain_levels.size():
		return 0
	return _terrain_levels[index]


func _refresh_fog(bridge: GameBridge) -> void:
	if _fog == null:
		return
	var states: PackedByteArray = bridge.VisionStates()
	if states.size() < _fog_width * _fog_height:
		return
	if _fog_states.size() != states.size():
		_fog_states.resize(states.size())
		_fog_states.fill(255)
	var mesh := _fog.multimesh
	for index in states.size():
		var state: int = states[index]
		if state == _fog_states[index]:
			continue
		_fog_states[index] = state
		match state:
			2:
				mesh.set_instance_color(index, FOG_VISIBLE_COLOR)
			1:
				mesh.set_instance_color(index, FOG_EXPLORED_COLOR)
			_:
				mesh.set_instance_color(index, FOG_UNEXPLORED_COLOR)

func set_selection(ids: Array) -> void:
	_selected_ids = ids.duplicate()
	_refresh_selection_rings()


func ground_level_at(tile_x: float, tile_y: float) -> float:
	return _ground_level(tile_x, tile_y)


func _ensure_selection_mesh() -> void:
	if _selection_mesh != null:
		return
	var torus := TorusMesh.new()
	torus.inner_radius = 0.26
	torus.outer_radius = 0.4
	torus.rings = 20
	torus.ring_segments = 8
	var mesh := MultiMesh.new()
	mesh.transform_format = MultiMesh.TRANSFORM_3D
	mesh.mesh = torus
	_selection_mesh = MultiMeshInstance3D.new()
	_selection_mesh.name = "SelectionRings"
	_selection_mesh.multimesh = mesh
	_selection_mesh.material_override = _flat_material(Color(0.35, 1.0, 0.4, 0.85))
	_selection_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_selection_mesh)


func _refresh_selection_rings() -> void:
	_ensure_selection_mesh()
	var transforms: Array[Transform3D] = []
	for id in _selected_ids:
		if not _entity_positions.has(id):
			continue
		var position: Vector3 = _entity_positions[id]
		transforms.append(Transform3D(Basis(), Vector3(position.x, position.y + 0.03, position.z)))
	var mesh := _selection_mesh.multimesh
	mesh.instance_count = transforms.size()
	_selection_mesh.set_visible(not transforms.is_empty())
	for index in transforms.size():
		mesh.set_instance_transform(index, transforms[index])


func show_placement(building_type: int, cell_x: int, cell_y: int, is_valid: bool) -> void:
	if cell_x < 0 or cell_y < 0:
		hide_placement()
		return
	_ensure_placement_mesh()
	var size_width := maxi(1, _bridge.BuildingWidth(building_type))
	var size_height := maxi(1, _bridge.BuildingHeight(building_type))
	var ground := _ground_level_rect(float(cell_x), float(cell_y), size_width, size_height)
	_placement_mesh.position = Vector3(
		cell_x + size_width * 0.5, ground + 0.5, cell_y + size_height * 0.5)
	_placement_mesh.scale = Vector3(size_width, 1.0, size_height)
	_placement_material.albedo_color = Color(0.35, 1.0, 0.4, 0.35) if is_valid else Color(1.0, 0.3, 0.25, 0.35)
	_placement_mesh.show()


func hide_placement() -> void:
	if _placement_mesh != null:
		_placement_mesh.hide()


func _ensure_placement_mesh() -> void:
	if _placement_mesh != null:
		return
	var box := BoxMesh.new()
	box.size = Vector3.ONE
	_placement_material = _flat_material(Color(0.35, 1.0, 0.4, 0.35))
	_placement_mesh = MeshInstance3D.new()
	_placement_mesh.name = "PlacementPreview"
	_placement_mesh.mesh = box
	_placement_mesh.material_override = _placement_material
	_placement_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_placement_mesh)


func show_order_marker(cell_x: int, cell_y: int, color: Color) -> Node3D:
	var marker := Node3D.new()
	var visual := MeshInstance3D.new()
	var quad := QuadMesh.new()
	quad.size = Vector2(0.9, 0.9)
	visual.mesh = quad
	visual.rotation_degrees.x = -90.0
	visual.material_override = _flat_material(color)
	visual.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	marker.add_child(visual)
	marker.position = Vector3(
		cell_x + 0.5, _ground_level(cell_x + 0.5, cell_y + 0.5) + 0.06, cell_y + 0.5)
	add_child(marker)
	return marker


func _flat_material(color: Color) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	return material
