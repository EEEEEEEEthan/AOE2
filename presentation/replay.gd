extends Node3D

const MENU_SCENE := "res://presentation/main_menu.tscn"

var bridge: GameBridge
var _view: WorldView
var _replays: Array = []
var _last_tick := -1
var _suppress_progress := false


func _ready() -> void:
	bridge = GameBridge.new()
	add_child(bridge)
	%PlayButton.pressed.connect(_on_play_pressed)
	%RefreshButton.pressed.connect(_refresh_list)
	%DeleteButton.pressed.connect(_on_delete_pressed)
	%BackButton.pressed.connect(_on_back_pressed)
	%ItemList.item_selected.connect(_on_item_selected)
	%Progress.drag_started.connect(_on_progress_drag_started)
	%Progress.drag_ended.connect(_on_progress_drag_ended)
	%SpeedOption.item_selected.connect(_on_speed_selected)
	_refresh_list()


func _refresh_list() -> void:
	_replays = bridge.ListReplays()
	%ItemList.clear()
	for entry: Dictionary in _replays:
		var ticks: int = int(entry.get("ticks", 0))
		if bool(entry.get("ok", false)):
			%ItemList.add_item("%s  ·  %d tick" % [entry["name"], ticks])
		else:
			%ItemList.add_item("%s  ·  损坏" % entry["name"])
	if _replays.is_empty():
		%StatusLabel.text = "暂无录像。对局结束后会自动保存到 user://replays。"
		%PlayButton.disabled = true
		%DeleteButton.disabled = true
		return
	%DeleteButton.disabled = false
	%ItemList.select(0)
	_on_item_selected(0)


func _on_item_selected(index: int) -> void:
	if index < 0 or index >= _replays.size():
		return
	var entry: Dictionary = _replays[index]
	if not bool(entry.get("ok", false)):
		_show_error("录像无法读取：%s" % entry.get("error", "未知错误"))
		%PlayButton.disabled = true
		return
	if not bridge.StartReplay(entry["path"]):
		_show_error("录像加载失败：%s" % bridge.LastReplayError)
		%PlayButton.disabled = true
		return
	_rebuild_view()
	bridge.SetReplayPaused(true)
	_last_tick = -1
	_playing(false)
	%PlayButton.disabled = false
	%StatusLabel.text = "已加载：%s" % entry["name"]
	_suppress_progress = true
	%Progress.max_value = max(1, bridge.ReplayTotalTicks)
	%Progress.value = 0
	_suppress_progress = false
	_refresh_view_and_labels()


func _rebuild_view() -> void:
	if _view != null:
		_view.queue_free()
		_view = null
	_view = WorldView.new()
	add_child(_view)
	_view.build_terrain(bridge)
	_view.refresh(bridge)
	var camera: IsoCamera = %Camera as IsoCamera
	camera.grid_width = float(bridge.GridWidth)
	camera.grid_height = float(bridge.GridHeight)
	camera.focus = Vector3(bridge.GridWidth * 0.5, 0.0, bridge.GridHeight * 0.5)
	camera.refresh_view()


func _playing(value: bool) -> void:
	bridge.SetReplayPaused(not value)
	%PlayButton.text = "暂停" if value else "播放"


func _process(delta: float) -> void:
	if bridge == null or not bridge.IsReplay:
		return
	if not bridge.ReplayFinished and %PlayButton.text == "暂停":
		bridge.Step(delta)
		_refresh_view_and_labels()
		if bridge.ReplayFinished:
			_playing(false)


func _refresh_view_and_labels() -> void:
	var tick: int = int(bridge.ReplayTick)
	if tick != _last_tick:
		_last_tick = tick
		_view.refresh(bridge)
	_suppress_progress = true
	%Progress.value = tick
	_suppress_progress = false
	%TickLabel.text = "%d / %d tick" % [tick, bridge.ReplayTotalTicks]
	%ChecksumLabel.text = bridge.ReplayChecksumStatus()


func _on_play_pressed() -> void:
	if bridge.ReplayFinished and bridge.ReplayTick >= bridge.ReplayTotalTicks:
		bridge.ReplaySeek(0)
		_refresh_view_and_labels()
		_playing(true)
		return
	_playing(%PlayButton.text == "播放")


func _on_speed_selected(index: int) -> void:
	var speeds := [0.5, 1.0, 2.0, 4.0]
	bridge.SetReplaySpeed(speeds[clampi(index, 0, speeds.size() - 1)])


func _on_progress_drag_started() -> void:
	bridge.SetReplayPaused(true)


func _on_progress_drag_ended(value_changed: bool) -> void:
	if not value_changed:
		return
	var target := int(%Progress.value)
	if bridge.ReplaySeek(target):
		_last_tick = -1
		_refresh_view_and_labels()
		_playing(false)
	else:
		_show_error("定位失败：%s" % bridge.LastReplayError)


func _on_delete_pressed() -> void:
	var selected: PackedInt32Array = %ItemList.get_selected_items()
	if selected.is_empty():
		return
	var entry: Dictionary = _replays[selected[0]]
	var path: String = entry.get("path", "")
	if path.is_empty():
		return
	var absolute := ProjectSettings.globalize_path(path)
	if FileAccess.file_exists(path) or FileAccess.file_exists(absolute):
		DirAccess.remove_absolute(absolute)
	_refresh_list()


func _on_back_pressed() -> void:
	get_tree().change_scene_to_file(MENU_SCENE)


func _show_error(message: String) -> void:
	%ErrorDialog.dialog_text = message
	%ErrorDialog.popup_centered()
