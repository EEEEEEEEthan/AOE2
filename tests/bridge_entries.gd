extends TestBase


func run() -> void:
	_match_entry_has_no_auto_demo()
	_demo_entry_spawns_units()


func _match_entry_has_no_auto_demo() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalMatch()
	check_true(not bridge.HasSession, "formal entry starts no match session")
	check_eq(bridge.Snapshot().size(), 0, "formal entry spawns no units")
	check_true(bridge.GridWidth > 0 and bridge.GridHeight > 0, "formal entry keeps the map grid")
	bridge.queue_free()


func _demo_entry_spawns_units() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartLocalDemo()
	check_true(bridge.Snapshot().size() > 0, "demo entry spawns units")
	bridge.queue_free()
