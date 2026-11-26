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

var _sim: GameSim
var _unit_mesh: BoxMesh
var _materials: Dictionary = {}
var _units: Dictionary = {}

func bind_sim(sim: GameSim) -> void:
	_sim = sim

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
		var x := Fixed.to_float(_sim.world.get_value(id, SimComponents.POSITION, SimComponents.X))
		var y := Fixed.to_float(_sim.world.get_value(id, SimComponents.POSITION, SimComponents.Y))
		unit.position = Vector3(x, 0.4, y)
	for id in _units.keys():
		if not alive.has(id):
			var dead: MeshInstance3D = _units[id]
			dead.queue_free()
			_units.erase(id)

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
