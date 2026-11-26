extends TestBase

func run() -> void:
	var world := SimWorld.new(1)
	var a := world.spawn()
	var b := world.spawn()
	check_ne(a, b, "entity ids are unique")
	check_true(world.is_alive(a), "spawned entity alive")
	var alive := world.entities()
	check_eq(alive.size(), 2, "two alive entities")
	world.set_value(a, &"position", &"x", Fixed.from_int(5))
	check_eq(world.get_value(a, &"position", &"x"), Fixed.from_int(5), "component set and get")
	world.attach(a, &"health")
	world.set_value(a, &"health", &"hp", 100)
	check_true(world.has_component(a, &"health"), "component attached")
	check_true(not world.has_component(b, &"health"), "component not shared")
	world.despawn(b)
	check_true(not world.is_alive(b), "despawned entity not alive")
	check_eq(world.entities().size(), 1, "alive list shrinks")
	check_true(not world.has_component(b, &"position"), "despawn clears components")
	var first := _checksum_of_sample()
	var second := _checksum_of_sample()
	check_eq(first, second, "checksum deterministic for identical operations")
	check_ne(first, _checksum_with_extra_damage(), "checksum changes with state")
	check_eq(world.entities()[0], a, "entities stay in ascending id order")
	_columns_sort_by_content()
	_checksum_tracks_rng()

func _columns_sort_by_content() -> void:
	var store := ComponentStore.new()
	store.add(0)
	store.set_value(0, &"zeta", 1)
	store.set_value(0, &"alpha", 2)
	store.set_value(0, &"mu", 3)
	var sorted := store.columns_sorted()
	check_true(sorted[0] == "alpha", "first column sorted by content")
	check_true(sorted[1] == "mu", "second column sorted by content")
	check_true(sorted[2] == "zeta", "third column sorted by content")

func _checksum_tracks_rng() -> void:
	var world := _checksum_world()
	var before := world.checksum()
	world.rng.next_u32()
	check_ne(before, world.checksum(), "checksum includes rng state")

func _checksum_world() -> SimWorld:
	var world := SimWorld.new(9)
	for i in 4:
		var id := world.spawn()
		world.attach(id, &"position")
		world.set_value(id, &"position", &"x", Fixed.from_int(i))
	return world

func _checksum_of_sample() -> int:
	var world := SimWorld.new(5)
	var ids: Array[int] = []
	for i in 12:
		var id := world.spawn()
		ids.append(id)
		world.attach(id, &"position")
		world.set_value(id, &"position", &"x", Fixed.from_int(i))
		world.set_value(id, &"position", &"y", Fixed.from_int(i * 2))
	world.tick = 3
	return world.checksum()

func _checksum_with_extra_damage() -> int:
	var world := SimWorld.new(5)
	var ids: Array[int] = []
	for i in 12:
		var id := world.spawn()
		ids.append(id)
		world.attach(id, &"position")
		world.set_value(id, &"position", &"x", Fixed.from_int(i))
		world.set_value(id, &"position", &"y", Fixed.from_int(i * 2))
	world.tick = 3
	world.set_value(ids[4], &"position", &"x", Fixed.from_int(400))
	return world.checksum()
