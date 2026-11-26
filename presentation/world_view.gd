class_name WorldView
extends Node3D

const TEAM_COLORS := {
	0: Color(0.82, 0.22, 0.2),
	1: Color(0.2, 0.42, 0.85),
}
const VILLAGER_COLORS := {
	0: Color(0.95, 0.58, 0.46),
	1: Color(0.46, 0.66, 0.95),
}
const RESOURCE_NODE_COLOR := Color(0.36, 0.24, 0.12)
const DROP_SITE_COLOR := Color(0.25, 0.75, 0.8)

const KIND_UNIT := 0
const KIND_VILLAGER := 1
const KIND_RESOURCE_NODE := 2
const KIND_DROP_SITE := 3

const FOG_UNEXPLORED_COLOR := Color(0.0, 0.0, 0.0, 1.0)
const FOG_EXPLORED_COLOR := Color(0.32, 0.34, 0.38, 0.55)
const FOG_VISIBLE_COLOR := Color(0.0, 0.0, 0.0, 0.0)
const FOG_HEIGHT := 0.03

var _unit_mesh: BoxMesh
var _materials: Dictionary = {}
var _units: Dictionary = {}
var _walls: Array = []
var _fog: MultiMeshInstance3D
var _fog_width := 0
var _fog_height := 0


func build_terrain(width: int, height: int, blocked: PackedInt32Array) -> void:
	var wall_mesh := BoxMesh.new()
	wall_mesh.size = Vector3(1.0, 0.8, 1.0)
	var wall_material := StandardMaterial3D.new()
	wall_material.albedo_color = Color(0.3, 0.33, 0.27)
	wall_material.roughness = 0.95
	for index in blocked.size():
		if blocked[index] == 0:
			continue
		var x := index % width
		var y := index / width
		var wall := MeshInstance3D.new()
		wall.mesh = wall_mesh
		wall.material_override = wall_material
		wall.position = Vector3(x + 0.5, 0.4, y + 0.5)
		add_child(wall)
		_walls.append({"node": wall, "x": x, "y": y})
	_build_fog(width, height)


func refresh(bridge: GameBridge) -> void:
	var alive: Dictionary = {}
	for entity: Dictionary in bridge.Snapshot():
		var id: int = entity["id"]
		alive[id] = true
		var unit: MeshInstance3D = _units.get(id)
		if unit == null:
			unit = _create_unit(entity)
			_units[id] = unit
		unit.position = Vector3(entity["x"], 0.4, entity["y"])
	for id in _units.keys():
		if not alive.has(id):
			var dead: MeshInstance3D = _units[id]
			dead.queue_free()
			_units.erase(id)
	_refresh_terrain_visibility(bridge)
	_refresh_fog(bridge)


func _create_unit(entity: Dictionary) -> MeshInstance3D:
	if _unit_mesh == null:
		_unit_mesh = BoxMesh.new()
		_unit_mesh.size = Vector3(0.7, 0.7, 0.7)
	var unit := MeshInstance3D.new()
	unit.mesh = _unit_mesh
	unit.material_override = _material_for(_color_for(entity))
	unit.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(unit)
	return unit


func _color_for(entity: Dictionary) -> Color:
	var kind: int = entity["kind"]
	if kind == KIND_RESOURCE_NODE:
		return RESOURCE_NODE_COLOR
	if kind == KIND_DROP_SITE:
		return DROP_SITE_COLOR
	var team: int = entity["team"]
	if kind == KIND_VILLAGER:
		return VILLAGER_COLORS.get(team, Color(0.8, 0.8, 0.8))
	return TEAM_COLORS.get(team, Color(0.7, 0.7, 0.7))


func _material_for(color: Color) -> StandardMaterial3D:
	if _materials.has(color):
		return _materials[color]
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.roughness = 0.7
	_materials[color] = material
	return material


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
			var origin := Vector3(x + 0.5, FOG_HEIGHT, y + 0.5)
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


func _refresh_terrain_visibility(bridge: GameBridge) -> void:
	var states := bridge.VisionStates()
	if states.size() < _fog_width * _fog_height:
		return
	for wall in _walls:
		var index: int = wall["y"] * _fog_width + wall["x"]
		var node: MeshInstance3D = wall["node"]
		node.visible = states[index] != 0


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
