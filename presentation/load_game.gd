extends Control

const MENU_SCENE := "res://presentation/main_menu.tscn"
const MATCH_SCENE := "res://presentation/main.tscn"

var bridge: GameBridge
var _saves: Array = []


func _ready() -> void:
	bridge = GameBridge.new()
	add_child(bridge)
	%LoadButton.pressed.connect(_on_load_pressed)
	%RefreshButton.pressed.connect(_refresh_list)
	%DeleteButton.pressed.connect(_on_delete_pressed)
	%BackButton.pressed.connect(_on_back_pressed)
	%ItemList.item_selected.connect(_on_item_selected)
	_refresh_list()


func _refresh_list() -> void:
	_saves = bridge.ListMatchSaves()
	%ItemList.clear()
	for entry: Dictionary in _saves:
		if bool(entry.get("ok", false)):
			var tick: int = int(entry.get("tick", 0))
			%ItemList.add_item("%s  ·  %s  ·  %s  ·  %s" % [
				entry.get("name", "存档"),
				entry.get("map", "未知地图"),
				entry.get("civs", "通用"),
				_format_time(tick),
			])
		else:
			%ItemList.add_item("%s  ·  损坏" % entry.get("name", "存档"))
	if _saves.is_empty():
		%StatusLabel.text = "暂无对局存档。对局中暂停后点「保存游戏」可创建存档。"
		%LoadButton.disabled = true
		%DeleteButton.disabled = true
		return
	%DeleteButton.disabled = false
	%ItemList.select(0)
	_on_item_selected(0)


func _on_item_selected(index: int) -> void:
	if index < 0 or index >= _saves.size():
		return
	var entry: Dictionary = _saves[index]
	if not bool(entry.get("ok", false)):
		%StatusLabel.text = "存档无法读取：%s" % entry.get("error", "未知错误")
		%LoadButton.disabled = true
		return
	%LoadButton.disabled = false
	%StatusLabel.text = "地图：%s　文明：%s　游戏时间：%s" % [
		entry.get("map", "未知"),
		entry.get("civs", "通用"),
		_format_time(int(entry.get("tick", 0))),
	]


func _on_load_pressed() -> void:
	var selected: PackedInt32Array = %ItemList.get_selected_items()
	if selected.is_empty():
		return
	var entry: Dictionary = _saves[selected[0]]
	var path: String = str(entry.get("path", ""))
	if path.is_empty():
		return
	MatchSetup.set_pending_save(path)
	get_tree().change_scene_to_file(MATCH_SCENE)


func _on_delete_pressed() -> void:
	var selected: PackedInt32Array = %ItemList.get_selected_items()
	if selected.is_empty():
		return
	var entry: Dictionary = _saves[selected[0]]
	var path: String = str(entry.get("path", ""))
	if path.is_empty():
		return
	if not bridge.DeleteMatchSave(path):
		_show_error("删除失败：%s" % bridge.LastMatchError)
		return
	_refresh_list()


func _on_back_pressed() -> void:
	get_tree().change_scene_to_file(MENU_SCENE)


func _format_time(tick: int) -> String:
	var seconds := int(tick / 30)
	return "%d:%02d" % [seconds / 60, seconds % 60]


func _show_error(message: String) -> void:
	%ErrorDialog.dialog_text = message
	%ErrorDialog.popup_centered()
