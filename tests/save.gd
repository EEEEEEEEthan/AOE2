extends TestBase

const MENU_SCENE := "res://presentation/main_menu.tscn"
const LOAD_SCENE := "res://presentation/load_game.tscn"


func run() -> void:
	_csharp_round_trip()
	_bridge_save_and_load()
	_scene_entries()


func _csharp_round_trip() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.SaveSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# match save self test")
	bridge.free()


func _bridge_save_and_load() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartGeneratedMatch(20261127, 2, 0, -1, PackedInt32Array())
	bridge.FastForward(45)
	var checksum_before := int(bridge.ServerChecksum())
	var tick_before := int(bridge.ClientTick())

	var path: String = str(bridge.SaveMatch("CI 存档"))
	check_true(not path.is_empty(), "match saves to disk")

	var listed := false
	var listed_entry := {}
	for entry: Dictionary in bridge.ListMatchSaves():
		if str(entry.get("path", "")) == path:
			listed = true
			listed_entry = entry
	check_true(listed, "saved match appears in the save list")
	check_eq(int(listed_entry.get("tick", -1)), tick_before, "save list exposes the game time")
	check_true(str(listed_entry.get("map", "")).length() > 0, "save list exposes the map name")
	check_true(str(listed_entry.get("civs", "")).length() > 0, "save list exposes the civilizations")

	check_true(bridge.LoadMatch(path), "match loads back")
	check_eq(int(bridge.ServerChecksum()), checksum_before, "loaded match checksum matches the save")
	check_eq(int(bridge.ClientTick()), tick_before, "loaded match tick matches the save")

	bridge.FastForward(30)
	check_true(int(bridge.ClientTick()) > tick_before, "loaded match keeps simulating")

	check_true(bridge.DeleteMatchSave(path), "deleted save reports success")
	check_true(not FileAccess.file_exists(path), "deleted save file is gone")
	bridge.queue_free()


func _scene_entries() -> void:
	var menu := load(MENU_SCENE)
	check_true(menu != null, "main menu scene exists")
	var load_scene := load(LOAD_SCENE)
	check_true(load_scene != null, "load game scene exists")
