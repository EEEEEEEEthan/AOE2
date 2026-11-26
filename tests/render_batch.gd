extends TestBase

const WORLD_VIEW := preload("res://presentation/world_view.gd")

const KIND_RESOURCE_NODE := 2
const KIND_BUILDING := 8


func run() -> void:
	_batch_columns_match_snapshot()
	_world_batches_units_and_caches_fog()


func _batch_columns_match_snapshot() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalDemo()
	var batch := bridge.SnapshotBatch()
	var snapshot := bridge.Snapshot()
	check_eq(int(batch["count"]), snapshot.size(), "batch count matches snapshot")
	var ids: PackedInt64Array = batch["ids"]
	var positions_x: PackedFloat32Array = batch["x"]
	var kinds: PackedInt32Array = batch["kind"]
	var columns_match := true
	for index in snapshot.size():
		var entity: Dictionary = snapshot[index]
		if int(ids[index]) != int(entity["id"]):
			columns_match = false
			break
		if not is_equal_approx(positions_x[index], entity["x"]):
			columns_match = false
			break
		if kinds[index] != int(entity["kind"]):
			columns_match = false
			break
	check_true(columns_match, "batch columns align with snapshot rows")
	var reused := bridge.SnapshotBatch()
	check_eq(int(reused["tick"]), int(batch["tick"]), "same tick reuses the cached batch")
	bridge.queue_free()


func _world_batches_units_and_caches_fog() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalDemo()
	var world := WORLD_VIEW.new()
	add_child(world)
	world.build_terrain(bridge)
	world.refresh(bridge)

	var batch := bridge.SnapshotBatch()
	var kinds: PackedInt32Array = batch["kind"]
	var node_remainings: PackedInt32Array = batch["node_remaining"]
	var expected_units := 0
	var expected_buildings := 0
	for index in int(batch["count"]):
		if kinds[index] == KIND_BUILDING:
			expected_buildings += 1
		elif kinds[index] != KIND_RESOURCE_NODE or node_remainings[index] > 0:
			expected_units += 1

	var instance_total := 0
	var bucket_count := 0
	for bucket: Dictionary in world.get("_unit_meshes").values():
		var multimesh: MultiMesh = bucket["multimesh"]
		instance_total += multimesh.instance_count
		bucket_count += 1
	check_eq(instance_total, expected_units, "every visible unit lives in a MultiMesh batch")
	check_true(bucket_count > 0, "units are grouped into MultiMesh buckets")
	check_eq(world.get("_units").size(), expected_buildings, "buildings stay individual nodes")

	var fog_states: PackedByteArray = world.get("_fog_states")
	check_eq(fog_states.size(), bridge.VisionStates().size(), "fog caches one state per cell")
	world.refresh(bridge)
	check_eq(int(world.get("_fog_states").size()), fog_states.size(), "fog refresh keeps its cache")
	bridge.queue_free()
	world.queue_free()
