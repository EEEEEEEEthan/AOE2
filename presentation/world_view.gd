class_name WorldView
extends Node3D

const TEAM_COLORS := {
	0: Color(0.82, 0.22, 0.2),
	1: Color(0.2, 0.42, 0.85),
}

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
	var team := _sim.world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
	unit.material_override = _material_for(team)
	unit.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(unit)
	return unit

func _material_for(team: int) -> StandardMaterial3D:
	if _materials.has(team):
		return _materials[team]
	var material := StandardMaterial3D.new()
	material.albedo_color = TEAM_COLORS.get(team, Color(0.7, 0.7, 0.7))
	material.roughness = 0.7
	_materials[team] = material
	return material
