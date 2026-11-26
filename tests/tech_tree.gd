extends TestBase


func run() -> void:
	_check_catalog()
	_check_bridge()


func _check_catalog() -> void:
	var sim := SimulationBridge.new()
	var failures := sim.TechTreeSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# tech tree self test")
	sim.free()


func _check_bridge() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	check_true(bridge.CivilizationCount >= 2, "catalog lists civilizations")
	check_true(bridge.TechCount > 0, "catalog lists techs")
	var disabled_seen := false
	for civ in range(bridge.CivilizationCount):
		var tree := bridge.TechTree(civ)
		check_eq(tree.size(), bridge.TechCount, "civ tree covers every tech")
		var available := 0
		for entry: Dictionary in tree:
			check_true(not str(entry["name"]).is_empty(), "tech name present")
			check_true(not str(entry["description"]).is_empty(), "tech description present")
			check_true(not str(entry["required_age_name"]).is_empty(), "tech age name present")
			check_true(not str(entry["required_building_name"]).is_empty(), "tech building name present")
			check_true(int(entry["research_ticks"]) > 0, "tech research ticks positive")
			check_true(int(entry["food"]) >= 0 and int(entry["gold"]) >= 0, "tech cost non-negative")
			if entry["available"]:
				available += 1
		if available < tree.size():
			disabled_seen = true
	check_true(disabled_seen, "some civilization disables a tech")
	bridge.queue_free()
