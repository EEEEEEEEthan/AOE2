class_name WorldView
extends Node3D
const TEAM_COLORS := {
	0: Color(0.82, 0.22, 0.2),
	1: Color(0.2, 0.42, 0.85),
}
const RUIN_COLOR := Color(0.24, 0.22, 0.2)
const FOUNDATION_TINT := Color(0.72, 0.66, 0.55)
var _sim: GameSim
var _unit_mesh: BoxMesh
var _materials: Dictionary = {}
var _units: Dictionary = {}
var _buildings: Dictionary = {}
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
			if not grid.is_terrain_blocked(x, y):
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
		if _sim.world.has_component(id, SimComponents.BUILDING):
			_refresh_building(id)
		else:
			_refresh_unit(id)
	_free_stale(_units, alive)
	_free_stale(_buildings, alive)
func _refresh_unit(id: int) -> void:
	var unit: MeshInstance3D = _units.get(id)
	if unit == null:
		unit = _create_unit(id)
		_units[id] = unit
	var x := Fixed.to_float(_sim.world.get_value(id, SimComponents.POSITION, SimComponents.X))
	var y := Fixed.to_float(_sim.world.get_value(id, SimComponents.POSITION, SimComponents.Y))
	unit.position = Vector3(x, 0.4, y)
func _refresh_building(id: int) -> void:
	var building: MeshInstance3D = _buildings.get(id)
	if building == null:
		building = _create_building(id)
		_buildings[id] = building
	var origin_x := Fixed.to_float(_sim.world.get_value(id, SimComponents.POSITION, SimComponents.X))
	var origin_y := Fixed.to_float(_sim.world.get_value(id, SimComponents.POSITION, SimComponents.Y))
	var size_w := _sim.world.get_value(id, SimComponents.BUILDING, SimComponents.SIZE_W)
	var size_h := _sim.world.get_value(id, SimComponents.BUILDING, SimComponents.SIZE_H)
	var state := _sim.world.get_value(id, SimComponents.BUILDING, SimComponents.STATE)
	var height := _building_height(id, state)
	var mesh := building.mesh as BoxMesh
	mesh.size = Vector3(size_w, height, size_h)
	building.position = Vector3(origin_x + size_w * 0.5, height * 0.5, origin_y + size_h * 0.5)
	var team := _sim.world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
	building.material_override = _building_material(team, state)
func _building_height(id: int, state: int) -> float:
	if state == BuildingSystem.STATE_RUINS:
		return 0.2
	if state == BuildingSystem.STATE_COMPLETE:
		return 1.4
	var progress := _sim.world.get_value(id, SimComponents.BUILDING, SimComponents.PROGRESS)
	var max_progress := _sim.world.get_value(id, SimComponents.BUILDING, SimComponents.MAX_PROGRESS)
	var ratio := float(progress) / float(max_progress)
	return 0.25 + ratio * 1.0
func _free_stale(group: Dictionary, alive: Dictionary) -> void:
	for id in group.keys():
		if alive.has(id):
			continue
		var node: MeshInstance3D = group[id]
		node.queue_free()
		group.erase(id)
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
func _create_building(id: int) -> MeshInstance3D:
	var building := MeshInstance3D.new()
	building.mesh = BoxMesh.new()
	building.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(building)
	return building
func _material_for(team: int) -> StandardMaterial3D:
	if _materials.has(team):
		return _materials[team]
	var material := StandardMaterial3D.new()
	material.albedo_color = TEAM_COLORS.get(team, Color(0.7, 0.7, 0.7))
	material.roughness = 0.7
	_materials[team] = material
	return material
func _building_material(team: int, state: int) -> StandardMaterial3D:
	var key := "%d_%d" % [team, state]
	if _materials.has(key):
		return _materials[key]
	var color: Color
	if state == BuildingSystem.STATE_RUINS:
		color = RUIN_COLOR
	elif state == BuildingSystem.STATE_COMPLETE:
		color = TEAM_COLORS.get(team, Color(0.7, 0.7, 0.7)).darkened(0.15)
	else:
		color = FOUNDATION_TINT.lerp(TEAM_COLORS.get(team, Color(0.7, 0.7, 0.7)), 0.35)
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.roughness = 0.9
	_materials[key] = material
	return material
