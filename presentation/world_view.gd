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
const RESOURCE_NODE_COLORS := [
	Color(0.36, 0.24, 0.12),
	Color(0.25, 0.6, 0.25),
	Color(0.86, 0.73, 0.16),
	Color(0.52, 0.52, 0.55),
]
const DROP_SITE_COLOR := Color(0.25, 0.75, 0.8)
const FOG_UNEXPLORED_COLOR := Color(0.0, 0.0, 0.0, 1.0)
const FOG_EXPLORED_COLOR := Color(0.32, 0.34, 0.38, 0.55)
const FOG_VISIBLE_COLOR := Color(0.0, 0.0, 0.0, 0.0)
const FOG_HEIGHT := 0.03

var _sim: GameSim
var _local_player := 0
var _vision: VisionGrid
var _unit_mesh: BoxMesh
var _materials: Dictionary = {}
var _units: Dictionary = {}
var _walls: Array = []
var _fog: MultiMeshInstance3D

func bind_sim(sim: GameSim, local_player: int = 0) -> void:
	_sim = sim
	_local_player = local_player
	_vision = sim.vision_for(local_player)

func build_terrain(grid: NavGrid) -> void:
	var wall_mesh := BoxMesh.new()
	wall_mesh.size = Vector3(1.0, 0.8, 1.0)
	var wall_material := StandardMaterial3D.new()
	wall_material.albedo_color = Color(0.3, 0.33, 0.27)
	wall_material.roughness = 0.95
	for y in grid.height:
		for x in grid.width:
			if not grid.is_blocked(x, y):
				continue
			var wall := MeshInstance3D.new()
			wall.mesh = wall_mesh
			wall.material_override = wall_material
			wall.position = Vector3(x + 0.5, 0.4, y + 0.5)
			add_child(wall)
			_walls.append({"node": wall, "x": x, "y": y})
	_build_fog(grid)

func refresh() -> void:
	if _sim == null:
		return
	var alive: Dictionary = {}
	for id in _sim.world.entities():
		if not _sim.world.has_component(id, SimComponents.POSITION):
			continue
		alive[id] = true
		var unit: MeshInstance3D = _units.get(id)
		if unit == null:
			unit = _create_unit(id)
			_units[id] = unit
		var x_fixed := _sim.world.get_value(id, SimComponents.POSITION, SimComponents.X)
		var y_fixed := _sim.world.get_value(id, SimComponents.POSITION, SimComponents.Y)
		unit.position = Vector3(Fixed.to_float(x_fixed), 0.4, Fixed.to_float(y_fixed))
		unit.visible = _entity_visible(id, Fixed.to_int(x_fixed), Fixed.to_int(y_fixed))
	for id in _units.keys():
		if not alive.has(id):
			var dead: MeshInstance3D = _units[id]
			dead.queue_free()
			_units.erase(id)
	_refresh_terrain_visibility()
	_refresh_fog()

func _create_unit(id: int) -> MeshInstance3D:
	if _unit_mesh == null:
		_unit_mesh = BoxMesh.new()
		_unit_mesh.size = Vector3(0.7, 0.7, 0.7)
	var unit := MeshInstance3D.new()
	unit.mesh = _unit_mesh
	unit.material_override = _material_for(_color_for(id))
	unit.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(unit)
	return unit

func _color_for(id: int) -> Color:
	if _sim.world.has_component(id, SimComponents.RESOURCE_NODE):
		return RESOURCE_NODE_COLORS[_sim.world.get_value(id, SimComponents.RESOURCE_NODE, SimComponents.NODE_TYPE)]
	if _sim.world.has_component(id, SimComponents.DROP_SITE):
		return DROP_SITE_COLOR
	var team := _sim.world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
	if _sim.world.has_component(id, SimComponents.GATHER):
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

func _entity_visible(id: int, tile_x: int, tile_y: int) -> bool:
	if _vision == null:
		return true
	if _sim.world.has_component(id, SimComponents.TEAM):
		var team := _sim.world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
		if team == _local_player:
			return true
		return _vision.visible_at(tile_x, tile_y)
	return _vision.is_explored(tile_x, tile_y)

func _refresh_terrain_visibility() -> void:
	for wall in _walls:
		var node: MeshInstance3D = wall["node"]
		node.visible = _vision == null or _vision.is_explored(wall["x"], wall["y"])

func _build_fog(grid: NavGrid) -> void:
	var mesh := MultiMesh.new()
	mesh.transform_format = MultiMesh.TRANSFORM_3D
	mesh.use_colors = true
	var quad := QuadMesh.new()
	quad.size = Vector2(1.0, 1.0)
	mesh.mesh = quad
	mesh.instance_count = grid.width * grid.height
	var basis := Basis(Vector3.RIGHT, -PI / 2.0)
	for y in grid.height:
		for x in grid.width:
			var origin := Vector3(x + 0.5, FOG_HEIGHT, y + 0.5)
			mesh.set_instance_transform(y * grid.width + x, Transform3D(basis, origin))
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

func _refresh_fog() -> void:
	if _fog == null:
		return
	var mesh := _fog.multimesh
	for y in _sim.grid.height:
		for x in _sim.grid.width:
			var cell := y * _sim.grid.width + x
			if _vision == null:
				mesh.set_instance_color(cell, FOG_VISIBLE_COLOR)
				continue
			match _vision.state_at(x, y):
				VisionGrid.STATE_VISIBLE:
					mesh.set_instance_color(cell, FOG_VISIBLE_COLOR)
				VisionGrid.STATE_EXPLORED:
					mesh.set_instance_color(cell, FOG_EXPLORED_COLOR)
				_:
					mesh.set_instance_color(cell, FOG_UNEXPLORED_COLOR)
