extends Control
## 多人大厅：建房/按地址加入、房间槽位与准备状态、房主开始对局。

const MENU_SCENE := "res://presentation/main_menu.tscn"
const MATCH_SCENE := "res://presentation/main.tscn"
const COLOR_NAMES := ["蓝", "红", "绿", "黄", "橙", "紫", "青", "灰"]
const COLOR_VALUES := [
	Color(0.28, 0.52, 0.95),
	Color(0.9, 0.3, 0.28),
	Color(0.3, 0.75, 0.42),
	Color(0.88, 0.75, 0.2),
	Color(0.85, 0.5, 0.2),
	Color(0.62, 0.38, 0.85),
	Color(0.25, 0.75, 0.78),
	Color(0.7, 0.7, 0.72),
]

var _network: MultiplayerSession
var _room_version := -1
var _handing_off := false


func _ready() -> void:
	_network = MultiplayerSession.new()
	add_child(_network)
	%RoomPanel.hide()
	%RoomError.hide()
	%StartButton.hide()
	for index in COLOR_NAMES.size():
		%ColorOption.add_item(COLOR_NAMES[index], index)
	_set_status("输入名称后创建房间，或填写房主地址加入。")
	%NameEdit.text = "玩家"


func _process(_delta: float) -> void:
	if _network == null or _handing_off:
		return
	var room: Dictionary = _network.Snapshot()
	if int(room.get("version", -1)) != _room_version:
		_room_version = int(room.get("version", -1))
		_refresh_room(room)
	var error: String = str(room.get("error", ""))
	if not error.is_empty():
		_set_status("")
		_show_room_error(error)
	if bool(room.get("started", false)) and _network.MatchStarted:
		_enter_match()


func _enter_match() -> void:
	_handing_off = true
	_set_status("对局开始，正在载入…")
	_network.PrepareHandoff()
	MatchSetup.set_network(_network)
	remove_child(_network)
	_network = null
	get_tree().change_scene_to_file(MATCH_SCENE)


func _refresh_room(room: Dictionary) -> void:
	var slots: Array = room.get("slots", [])
	if slots.is_empty():
		return
	%RoomError.hide()
	_show_room()
	var host_seat: int = int(room.get("host_seat", 0))
	var host_name := ""
	for slot: Dictionary in slots:
		if int(slot["seat"]) == host_seat:
			host_name = str(slot["name"])
	%RoomInfo.text = "地图：%s　　房主：%s　　玩家：%d 人" % [
		str(room.get("map_name", "阿拉伯")), host_name, slots.size()]
	for child in %SlotsList.get_children():
		child.queue_free()
	var all_ready := true
	for slot: Dictionary in slots:
		%SlotsList.add_child(_slot_row(slot))
		if int(slot["seat"]) != host_seat and not bool(slot["ready"]):
			all_ready = false
	%StartButton.visible = _network.IsHost
	%StartButton.disabled = not all_ready
	_update_own_controls(slots, int(room.get("my_seat", -1)))
	_set_status("已连接到房间，等待房主开始。" if not _network.IsHost else "房间已创建，等待其他玩家准备。")


func _slot_row(slot: Dictionary) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 16)
	var seat := Label.new()
	seat.text = "玩家 %d" % (int(slot["seat"]) + 1)
	seat.custom_minimum_size = Vector2(90, 0)
	var name_label := Label.new()
	name_label.text = str(slot["name"])
	name_label.custom_minimum_size = Vector2(160, 0)
	var team_label := Label.new()
	team_label.text = "队伍 %d" % (int(slot["seat"]) + 1)
	team_label.custom_minimum_size = Vector2(80, 0)
	var swatch := ColorRect.new()
	swatch.custom_minimum_size = Vector2(18, 18)
	swatch.color = COLOR_VALUES[int(slot["color"]) % COLOR_VALUES.size()]
	var state := Label.new()
	state.text = "已准备" if bool(slot["ready"]) else "未准备"
	var tags: PackedStringArray = []
	if bool(slot["host"]):
		tags.append("房主")
	if bool(slot["mine"]):
		tags.append("我")
	var tag_label := Label.new()
	tag_label.text = " ".join(tags)
	row.add_child(seat)
	row.add_child(name_label)
	row.add_child(team_label)
	row.add_child(swatch)
	row.add_child(state)
	row.add_child(tag_label)
	return row


func _update_own_controls(slots: Array, my_seat: int) -> void:
	for slot: Dictionary in slots:
		if int(slot["seat"]) != my_seat:
			continue
		%ColorOption.select(int(slot["color"]) % COLOR_NAMES.size())
		%ReadyCheck.set_pressed_no_signal(bool(slot["ready"]))
		return


func _show_room() -> void:
	%RoomPanel.show()
	%HostButton.disabled = true
	%JoinButton.disabled = true


func _on_host_pressed() -> void:
	if _network.HostRoom(int(%PortSpin.value), _player_name()):
		_set_status("房间已创建，等待其他玩家加入。")
		_show_room()
	else:
		_show_room_error("无法创建房间：%s" % _network.LastError)


func _on_join_pressed() -> void:
	if _network.JoinRoom(%AddressEdit.text.strip_edges(), int(%PortSpin.value), _player_name()):
		_set_status("连接成功，等待房主开始。")
		_show_room()
	else:
		_show_room_error("连接失败：%s" % _network.LastError)


func _on_color_selected(index: int) -> void:
	_network.SetProfile(_player_name(), int(%ColorOption.get_item_id(index)))


func _on_ready_toggled(pressed: bool) -> void:
	_network.SetReady(pressed)


func _on_name_submitted(_text: String) -> void:
	_network.SetProfile(_player_name(), int(%ColorOption.get_selected_id()))


func _on_start_pressed() -> void:
	_network.RequestStart()


func _on_leave_pressed() -> void:
	_network.Leave()
	get_tree().change_scene_to_file(MENU_SCENE)


func _on_back_pressed() -> void:
	_network.Leave()
	get_tree().change_scene_to_file(MENU_SCENE)


func _player_name() -> String:
	var player_name: String = %NameEdit.text.strip_edges()
	return player_name if not player_name.is_empty() else "玩家"


func _set_status(text: String) -> void:
	%StatusLabel.text = text


func _show_room_error(text: String) -> void:
	%RoomError.text = text
	%RoomError.show()
