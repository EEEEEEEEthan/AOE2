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
