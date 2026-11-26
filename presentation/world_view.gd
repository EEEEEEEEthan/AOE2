class_name WorldView
extends Node3D

const VOXEL_MESHER := preload("res://presentation/voxel_mesher.gd")
const VOX_MODEL := preload("res://presentation/vox_model.gd")

const TERRAIN_LAYER := 1.0 / 8.0

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

const FOG_UNEXPLORED_COLOR := Color(0.0, 0.0, 0.0, 1.0)
const FOG_EXPLORED_COLOR := Color(0.32, 0.34, 0.38, 0.55)
const FOG_VISIBLE_COLOR := Color(0.0, 0.0, 0.0, 0.0)
const FOG_OFFSET := 0.04

var _voxel_material: StandardMaterial3D
var _models: Dictionary = {}
var _units: Dictionary = {}
var _fog: MultiMeshInstance3D
var _fog_width := 0
var _fog_height := 0
var _terrain_width := 0
var _terrain_height := 0
var _terrain_levels: PackedInt32Array = PackedInt32Array()


func build_terrain(bridge: GameBridge) -> void:
	_terrain_width = bridge.TerrainWidth
	_terrain_height = bridge.TerrainHeight
	var types := bridge.TerrainTypes()
	var heights := bridge.TerrainHeights()
	if _terrain_width == 0 or _terrain_height == 0:
		return
	_terrain_levels = PackedInt32Array(heights)
	var voxels := {}
	for tile_y in _terrain_height:
		for tile_x in _terrain_width:
			var index := tile_y * _terrain_width + tile_x
			var level: int = heights[index]
			var type: int = types[index]
			if type == 2 and level == 0:
				level = 1
			# 高度 0 的平地也铺一层地基体素（y = -1），顶面正好落在 y = 0。
			for voxel_y in range(-1, level):
				voxels[Vector3i(tile_x, voxel_y, tile_y)] = _terrain_color(type, voxel_y == level - 1)
	var mesh: ArrayMesh = VOXEL_MESHER.greedy_mesh(
		voxels, Vector3(1.0, TERRAIN_LAYER, 1.0), true)
	var terrain := MeshInstance3D.new()
	terrain.name = "VoxelTerrain"
	terrain.mesh = mesh
	terrain.material_override = _get_voxel_material()
	terrain.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(terrain)
	_build_fog(_terrain_width, _terrain_height)


func refresh(bridge: GameBridge) -> void:
	var alive: Dictionary = {}
	for entity: Dictionary in bridge.Snapshot():
		var id: int = entity["id"]
		alive[id] = true
		var unit: Node3D = _units.get(id)
		if unit == null:
			unit = _create_unit(entity)
			_units[id] = unit
		unit.position = Vector3(
			entity["x"],
			_ground_level(entity["x"], entity["y"]),
			entity["y"])
		unit.visible = _is_visible_entity(entity)
	for id in _units.keys():
		if not alive.has(id):
			var dead: Node3D = _units[id]
			dead.queue_free()
			_units.erase(id)
	_refresh_fog(bridge)


func _create_unit(entity: Dictionary) -> Node3D:
	var kind: int = entity["kind"]
	var path: String = MODEL_PATHS.get(kind, MODEL_PATHS[KIND_UNIT])
	var model := _model_for(path, _color_for(entity))
	var root := Node3D.new()
	var visual := MeshInstance3D.new()
	visual.mesh = model["mesh"]
	visual.material_override = _get_voxel_material()
	visual.position = model["offset"]
	visual.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	root.add_child(visual)
	add_child(root)
	return root


func _model_for(path: String, color: Color) -> Dictionary:
	var key := "%s|%d" % [path, color.to_rgba32()]
	if _models.has(key):
		return _models[key]
	var model := VOX_MODEL.load_model(path, color, TERRAIN_LAYER)
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


func _is_visible_entity(entity: Dictionary) -> bool:
	if entity["kind"] == KIND_RESOURCE_NODE:
		return entity["node_remaining"] > 0
	return true


func _color_for(entity: Dictionary) -> Color:
	var kind: int = entity["kind"]
	if kind == KIND_RESOURCE_NODE:
		return RESOURCE_NODE_COLORS[entity["node_type"]]
	if kind == KIND_DROP_SITE:
		return DROP_SITE_COLOR
	var team: int = entity["team"]
	return TEAM_COLORS.get(team, Color(0.8, 0.8, 0.8))


func _build_fog(width: int, height: int) -> void:
	_fog_width = width
	_fog_height = height
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
	var states := bridge.VisionStates()
	if states.size() < _fog_width * _fog_height:
		return
	var mesh := _fog.multimesh
	for index in states.size():
		match states[index]:
			2:
				mesh.set_instance_color(index, FOG_VISIBLE_COLOR)
			1:
				mesh.set_instance_color(index, FOG_EXPLORED_COLOR)
			_:
				mesh.set_instance_color(index, FOG_UNEXPLORED_COLOR)
