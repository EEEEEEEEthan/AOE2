extends TestBase

const MESHER := preload("res://presentation/voxel_mesher.gd")
const MODEL := preload("res://presentation/vox_model.gd")

const MODEL_PATHS := [
	"res://assets/vox/villager.vox",
	"res://assets/vox/soldier.vox",
	"res://assets/vox/tree.vox",
	"res://assets/vox/town_center.vox",
	"res://assets/vox/town_center_hall.vox",
	"res://assets/vox/house.vox",
	"res://assets/vox/barracks.vox",
	"res://assets/vox/wall.vox",
	"res://assets/vox/tower.vox",
	"res://assets/vox/market.vox",
]

# BuildingTypes TownCenter..Market 各自一个模型文件。
const BUILDING_MODEL_PATHS := [
	"res://assets/vox/town_center_hall.vox",
	"res://assets/vox/house.vox",
	"res://assets/vox/barracks.vox",
	"res://assets/vox/wall.vox",
	"res://assets/vox/tower.vox",
	"res://assets/vox/market.vox",
]


func run() -> void:
	var block := {}
	for x in 2:
		for z in 2:
			block[Vector3i(x, 0, z)] = Color.RED
	var full: ArrayMesh = MESHER.greedy_mesh(block, Vector3.ONE, false)
	check_eq(full.get_surface_count(), 1, "voxel block surface count")
	var full_vertices: PackedVector3Array = full.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
	check_eq(full_vertices.size(), 36, "2x2 block greedy merges into 6 quads")
	var trimmed: ArrayMesh = MESHER.greedy_mesh(block, Vector3.ONE, true)
	var trimmed_vertices: PackedVector3Array = trimmed.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
	check_eq(trimmed_vertices.size(), 30, "terrain drop bottom face leaves 5 quads")

	var flat_heights := PackedInt32Array([0, 0, 0, 0])
	var flat_colors := PackedColorArray([Color.GREEN])
	var side_colors := PackedColorArray([Color.DARK_GREEN])
	var no_drop := PackedInt32Array([0])
	var flat_mesh: ArrayMesh = MESHER.heightfield_mesh(
		flat_heights, 1, 1, 0.125, flat_colors, side_colors, no_drop)
	check(_min_y(flat_mesh) < 0.0, "heightfield extends boundary wall below top")

	var steep_heights := PackedInt32Array([8, 0, 0, 0])
	var steep_mesh: ArrayMesh = MESHER.heightfield_mesh(
		steep_heights, 1, 1, 0.125, flat_colors, side_colors, no_drop)
	check(_max_y(steep_mesh) - _min_y(steep_mesh) > _max_y(flat_mesh) - _min_y(flat_mesh),
		"steep cell raises taller side walls than flat ground")

	var sunk_mesh: ArrayMesh = MESHER.heightfield_mesh(
		flat_heights, 1, 1, 0.125, flat_colors, side_colors, PackedInt32Array([1]))
	check(is_equal_approx(_max_y(flat_mesh) - _max_y(sunk_mesh), 0.125),
		"sunken cell sits one layer below its neighbours")

	# 缓坡：两格共享顶点从 0 抬到 2 层（<= MaxWalkableSlope），顶面应连续过渡、无台阶侧壁。
	var ramp_heights := PackedInt32Array([0, 0, 0, 2, 2, 2])
	var ramp_colors := PackedColorArray([Color.GREEN, Color.GREEN])
	var ramp_sides := PackedColorArray([Color.DARK_GREEN, Color.DARK_GREEN])
	var ramp_drops := PackedInt32Array([0, 0])
	var ramp_mesh: ArrayMesh = MESHER.heightfield_mesh(
		ramp_heights, 2, 1, 0.125, ramp_colors, ramp_sides, ramp_drops)
	var ramp_up := _up_vertices(ramp_mesh)
	var low_edge := _vertices_near(ramp_up, 2, 0.0)
	var high_edge := _vertices_near(ramp_up, 2, 1.0)
	check(low_edge.size() > 0 and _all_near(low_edge, 0.0),
		"gentle ramp keeps shared low edge at 0 instead of flattening to max")
	check(high_edge.size() > 0 and _all_near(high_edge, 0.25),
		"gentle ramp keeps shared high edge at two layers")
	check(_no_side_faces_at(ramp_mesh, 0, 1.0),
		"gentle ramp has no vertical wall on the shared interior edge")

	for path: String in MODEL_PATHS:
		var model: Dictionary = MODEL.load_model(path, Color(0.82, 0.22, 0.2), 0.125)
		var mesh: Variant = model["mesh"]
		check(mesh != null, "%s loaded" % path)
		if mesh != null:
			check_eq((mesh as ArrayMesh).get_surface_count(), 1, "%s one surface" % path)
			check((mesh as ArrayMesh).get_aabb().size.length() > 0.0, "%s has volume" % path)

	check_eq(BUILDING_MODEL_PATHS.size(), 6, "six building types have a model")
	for path: String in BUILDING_MODEL_PATHS:
		var model: Dictionary = MODEL.load_model(path, Color(0.82, 0.22, 0.2), 0.125)
		var mesh: Variant = model["mesh"]
		check(mesh != null, "building model %s loads" % path)
		if mesh != null:
			check((mesh as ArrayMesh).get_aabb().size.length() > 0.0, "building model %s has volume" % path)


func _min_y(mesh: ArrayMesh) -> float:
	var vertices: PackedVector3Array = mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
	var lowest := INF
	for vertex in vertices:
		lowest = minf(lowest, vertex.y)
	return lowest


func _max_y(mesh: ArrayMesh) -> float:
	var vertices: PackedVector3Array = mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
	var highest := -INF
	for vertex in vertices:
		highest = maxf(highest, vertex.y)
	return highest


func _up_vertices(mesh: ArrayMesh) -> PackedVector3Array:
	var arrays: Array = mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
	var result := PackedVector3Array()
	for index in vertices.size():
		if normals[index].y > 0.9:
			result.append(vertices[index])
	return result


func _vertices_near(vertices: PackedVector3Array, axis: int, value: float) -> PackedVector3Array:
	var result := PackedVector3Array()
	for vertex in vertices:
		if is_equal_approx(_axis_value(vertex, axis), value):
			result.append(vertex)
	return result


func _all_near(vertices: PackedVector3Array, y: float) -> bool:
	for vertex in vertices:
		if not is_equal_approx(vertex.y, y):
			return false
	return true


func _no_side_faces_at(mesh: ArrayMesh, axis: int, value: float) -> bool:
	var arrays: Array = mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
	for index in range(0, vertices.size(), 3):
		var on_plane := true
		var face_normal := Vector3.ZERO
		for offset in 3:
			var vertex := vertices[index + offset]
			if not is_equal_approx(_axis_value(vertex, axis), value):
				on_plane = false
				break
			face_normal = normals[index + offset]
		if on_plane and absf(face_normal.y) <= 0.9:
			return false
	return true


func _axis_value(vector: Vector3, axis: int) -> float:
	match axis:
		0:
			return vector.x
		1:
			return vector.y
		_:
			return vector.z
