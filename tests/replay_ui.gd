extends TestBase

const MENU_SCENE := "res://presentation/main_menu.tscn"
const REPLAY_SCENE := "res://presentation/replay.tscn"
const KIND_VILLAGER := 1


func run() -> void:
	_record_and_replay()
	_reject_corrupted_file()
	_scene_entries()


func _record_and_replay() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	bridge.StartGeneratedMatch(20261127, 2, 0)
	check_true(bridge.HasRecording, "generated match records by default")
	var villager := _find_villager(bridge)
	if not villager.is_empty():
		bridge.MoveUnits(PackedInt64Array([int(villager["id"])]), int(villager["x"]) + 2, int(villager["y"]))
	bridge.FastForward(18)
	var path: String = str(bridge.SaveReplay("ci_replay"))
	check_true(not path.is_empty(), "replay saves to disk")

	var listed := false
	for entry: Dictionary in bridge.ListReplays():
		if str(entry.get("path", "")) == path:
			listed = true
	check_true(listed, "saved replay appears in the replay list")

	check_true(bridge.StartReplay(path), "replay loads and starts")
	check_true(bridge.IsReplay, "bridge is in replay mode")
	var total: int = int(bridge.ReplayTotalTicks)
	check_true(total > 1, "replay knows its total tick count")
	bridge.SetReplayPaused(false)
	bridge.SetReplaySpeed(4.0)
	for step in 500:
		if bridge.ReplayFinished:
			break
		bridge.Step(0.1)
	check_true(bridge.ReplayFinished, "replay runs to the end")
	check(str(bridge.ReplayChecksumStatus()) == "校验和一致", "replay checksum matches the recording")

	bridge.ReplaySeek(int(total / 2))
	check_true(bridge.ReplayTick <= total, "progress seek stays within the replay")
	check_true(not bridge.ReplayFinished, "seek to the middle is not the end")

	DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	bridge.queue_free()


func _reject_corrupted_file() -> void:
	var bridge := GameBridge.new()
	add_child(bridge)
	var bad_path := OS.get_temp_dir().path_join("aoe2_bad_%d.aoe2rep" % Time.get_ticks_usec())
	var file := FileAccess.open(bad_path, FileAccess.WRITE)
	if file != null:
		file.store_buffer(PackedByteArray([1, 2, 3, 4]))
		file.close()
	check_true(not bridge.StartReplay(bad_path), "corrupted replay is rejected")
	check_true(not bridge.LastReplayError.is_empty(), "corrupted replay reports a clear error")
	DirAccess.remove_absolute(bad_path)
	bridge.queue_free()


func _scene_entries() -> void:
	var menu := (load(MENU_SCENE) as PackedScene).instantiate()
	add_child(menu)
	check(menu.get_node("%ReplayButton") is Button, "main menu offers replay viewing")
	menu.queue_free()

	var producer := GameBridge.new()
	add_child(producer)
	producer.StartGeneratedMatch(20261127, 2, 0)
	producer.FastForward(8)
	var fixture: String = str(producer.SaveReplay("ci_scene_replay"))
	check_true(not fixture.is_empty(), "scene replay fixture is written")

	var replay_scene := load(REPLAY_SCENE) as PackedScene
	check(replay_scene != null, "replay scene loads")
	var root := replay_scene.instantiate()
	add_child(root)
	check(root.get_node("%ItemList") is ItemList, "replay scene lists replays")
	check(root.get_node("%PlayButton") is Button, "replay scene offers playback control")
	check(root.get_node("%Progress") is HSlider, "replay scene offers a progress bar")
	check(root.get_node("%SpeedOption") is OptionButton, "replay scene offers speed control")
	root.queue_free()

	DirAccess.remove_absolute(ProjectSettings.globalize_path(fixture))
	producer.queue_free()


func _find_villager(bridge: GameBridge) -> Dictionary:
	for entity: Dictionary in bridge.Snapshot():
		if int(entity["team"]) == bridge.MyTeam and int(entity["kind"]) == KIND_VILLAGER:
			return entity
	return {}
