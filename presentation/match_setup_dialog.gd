class_name MatchSetupDialog
extends Control
## 对局设置：选择玩家人数与每位玩家的文明，确认后写入 MatchSetup.config
## 再交给加载场景开局。文明列表直接读 GameBridge 暴露的 CivilizationCatalog。

signal confirmed
signal cancelled

const PLAYER_CHOICES := [2, 3, 4]

var _civ_names: PackedStringArray = PackedStringArray()
var _selections: PackedInt32Array = PackedInt32Array([0, 0, 0, 0])
var _player_count: OptionButton
var _civ_rows: VBoxContainer


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	_civ_names = _load_civ_names()
	_load_selections()
	_build_ui()
	_rebuild_civ_rows()


func _load_civ_names() -> PackedStringArray:
	var names := PackedStringArray()
	var probe := GameBridge.new()
	for civ in probe.CivilizationCount:
		names.append(probe.CivilizationName(civ))
	probe.free()
	return names


func _load_selections() -> void:
	var civs: PackedInt32Array = MatchSetup.config.get("civ_ids", PackedInt32Array())
	for index in mini(civs.size(), _selections.size()):
		_selections[index] = civs[index]


func _build_ui() -> void:
	var dim := ColorRect.new()
	dim.name = "Dim"
	dim.color = Color(0, 0, 0, 0.72)
	dim.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.name = "Center"
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var panel := PanelContainer.new()
	panel.name = "Panel"
	panel.custom_minimum_size = Vector2(420, 0)
	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color = Color(0.1, 0.12, 0.16, 1.0)
	panel_style.set_corner_radius_all(8)
	panel.add_theme_stylebox_override("panel", panel_style)
	center.add_child(panel)

	var margin := MarginContainer.new()
	margin.name = "Margin"
	for side in ["left", "top", "right", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 20)
	panel.add_child(margin)

	var content := VBoxContainer.new()
	content.name = "Content"
	content.add_theme_constant_override("separation", 12)
	margin.add_child(content)

	var title := Label.new()
	title.text = "对局设置"
	title.add_theme_font_size_override("font_size", 24)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(title)

	var count_row := HBoxContainer.new()
	count_row.add_theme_constant_override("separation", 8)
	content.add_child(count_row)
	var count_label := Label.new()
	count_label.text = "玩家人数"
	count_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	count_row.add_child(count_label)
	_player_count = OptionButton.new()
	for count: int in PLAYER_CHOICES:
		_player_count.add_item("%d 人" % count)
	_player_count.select(clampi(int(MatchSetup.config.get("player_count", 2)) - PLAYER_CHOICES[0], 0, PLAYER_CHOICES.size() - 1))
	_player_count.item_selected.connect(_on_player_count_selected)
	count_row.add_child(_player_count)

	var civ_title := Label.new()
	civ_title.text = "文明选择"
	content.add_child(civ_title)

	_civ_rows = VBoxContainer.new()
	_civ_rows.name = "CivRows"
	_civ_rows.add_theme_constant_override("separation", 8)
	content.add_child(_civ_rows)

	var button_row := HBoxContainer.new()
	button_row.alignment = BoxContainer.ALIGNMENT_END
	button_row.add_theme_constant_override("separation", 8)
	content.add_child(button_row)
	var cancel := Button.new()
	cancel.text = "取消"
	cancel.pressed.connect(_on_cancel_pressed)
	button_row.add_child(cancel)
	var start := Button.new()
	start.text = "开始对局"
	start.pressed.connect(_on_confirm_pressed)
	button_row.add_child(start)


func _rebuild_civ_rows() -> void:
	for child in _civ_rows.get_children():
		_civ_rows.remove_child(child)
		child.queue_free()
	var player_count: int = PLAYER_CHOICES[_player_count.selected]
	for player in player_count:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		var label := Label.new()
		label.text = PlayerColors.name_of(player)
		label.custom_minimum_size = Vector2(72, 0)
		row.add_child(label)
		var option := OptionButton.new()
		option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		for civ in _civ_names.size():
			option.add_item(_civ_names[civ])
		option.select(clampi(_selections[player], 0, maxi(_civ_names.size() - 1, 0)))
		option.item_selected.connect(_on_civ_selected.bind(player))
		row.add_child(option)
		_civ_rows.add_child(row)


func _on_player_count_selected(_index: int) -> void:
	_rebuild_civ_rows()


func _on_civ_selected(index: int, player: int) -> void:
	_selections[player] = index


func _on_confirm_pressed() -> void:
	var player_count: int = PLAYER_CHOICES[_player_count.selected]
	MatchSetup.config["player_count"] = player_count
	var civs := PackedInt32Array()
	for player in player_count:
		civs.append(_selections[player])
	MatchSetup.config["civ_ids"] = civs
	confirmed.emit()
	queue_free()


func _on_cancel_pressed() -> void:
	cancelled.emit()
	queue_free()
