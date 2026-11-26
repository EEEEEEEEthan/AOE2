class_name OptionsMenu
extends Control
## 选项界面：画面 / 音频 / 游戏 / 快捷键四个页签。改动先落在本地 pending，
## 点“应用”写入 Settings 并持久化，点“取消”直接丢弃，点“恢复默认”重置为默认值。

signal closed(applied: bool)

var _pending: Dictionary = {}
var _pending_bindings: Dictionary = {}
var _binding_labels: Dictionary = {}
var _capture_action := ""

var _window_mode: OptionButton
var _resolution: OptionButton
var _vsync: CheckButton
var _quality: OptionButton
var _ui_scale: HSlider
var _ui_scale_label: Label
var _color_blind: OptionButton
var _subtitles: CheckButton
var _master: HSlider
var _master_label: Label
var _sfx: HSlider
var _sfx_label: Label
var _music: HSlider
var _music_label: Label
var _difficulty: OptionButton
var _game_speed: OptionButton
var _tips: CheckButton
var _status: Label


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	_load_pending()
	_build_ui()
	_refresh_widgets()
	set_process_input(true)


func _store() -> SettingsStore:
	return get_node("/root/Settings") as SettingsStore


func _load_pending() -> void:
	_pending = {
		SettingsStore.SECTION_DISPLAY: _store().section_copy(SettingsStore.SECTION_DISPLAY),
		SettingsStore.SECTION_AUDIO: _store().section_copy(SettingsStore.SECTION_AUDIO),
		SettingsStore.SECTION_GAME: _store().section_copy(SettingsStore.SECTION_GAME),
	}
	_pending_bindings = {}
	for definition: Dictionary in _store().action_definitions():
		_pending_bindings[definition["action"]] = _store().bindings_for(definition["action"])


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
	panel.custom_minimum_size = Vector2(720, 520)
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
	title.text = "选项"
	title.add_theme_font_size_override("font_size", 24)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(title)

	var tabs := TabContainer.new()
	tabs.name = "TabContainer"
	tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
	tabs.custom_minimum_size = Vector2(0, 360)
	content.add_child(tabs)
	tabs.add_child(_build_display_tab())
	tabs.add_child(_build_audio_tab())
	tabs.add_child(_build_game_tab())
	tabs.add_child(_build_keyboard_tab())
	tabs.set_tab_title(0, "画面")
	tabs.set_tab_title(1, "音频")
	tabs.set_tab_title(2, "游戏")
	tabs.set_tab_title(3, "快捷键")

	_status = Label.new()
	_status.name = "StatusLabel"
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.add_theme_color_override("font_color", Color(1.0, 0.75, 0.35))
	content.add_child(_status)

	content.add_child(_build_button_row())


func _build_display_tab() -> Control:
	var body := _tab_body("DisplayTab")
	var grid := _grid(body)
	_window_mode = _option(grid, "窗口模式", SettingsStore.WINDOW_MODE_LABELS)
	_resolution = _option(grid, "分辨率", SettingsStore.RESOLUTION_LABELS)
	_vsync = _check(grid, "垂直同步")
	_quality = _option(grid, "画质（抗锯齿）", SettingsStore.QUALITY_LABELS)
	var ui_scale_row := _slider_row(grid, "UI 缩放", 80, 150, 10)
	_ui_scale = ui_scale_row[0]
	_ui_scale_label = ui_scale_row[1]
	_color_blind = _option(grid, "色盲配色", SettingsStore.COLOR_BLIND_LABELS)
	_subtitles = _check(grid, "字幕")

	_window_mode.item_selected.connect(_on_option.bind(SettingsStore.SECTION_DISPLAY, "window_mode"))
	_resolution.item_selected.connect(_on_option.bind(SettingsStore.SECTION_DISPLAY, "resolution"))
	_vsync.toggled.connect(_on_toggle.bind(SettingsStore.SECTION_DISPLAY, "vsync"))
	_quality.item_selected.connect(_on_option.bind(SettingsStore.SECTION_DISPLAY, "quality"))
	_ui_scale.value_changed.connect(_on_slider.bind(SettingsStore.SECTION_DISPLAY, "ui_scale"))
	_color_blind.item_selected.connect(_on_option.bind(SettingsStore.SECTION_DISPLAY, "color_blind"))
	_subtitles.toggled.connect(_on_toggle.bind(SettingsStore.SECTION_DISPLAY, "subtitles"))
	return body


func _build_audio_tab() -> Control:
	var body := _tab_body("AudioTab")
	var grid := _grid(body)
	var master_row := _slider_row(grid, "主音量", 0, 100, 5)
	_master = master_row[0]
	_master_label = master_row[1]
	var sfx_row := _slider_row(grid, "音效音量", 0, 100, 5)
	_sfx = sfx_row[0]
	_sfx_label = sfx_row[1]
	var music_row := _slider_row(grid, "音乐音量", 0, 100, 5)
	_music = music_row[0]
	_music_label = music_row[1]
	_master.value_changed.connect(_on_slider.bind(SettingsStore.SECTION_AUDIO, "master"))
	_sfx.value_changed.connect(_on_slider.bind(SettingsStore.SECTION_AUDIO, "sfx"))
	_music.value_changed.connect(_on_slider.bind(SettingsStore.SECTION_AUDIO, "music"))
	return body


func _build_game_tab() -> Control:
	var body := _tab_body("GameTab")
	var grid := _grid(body)
	_difficulty = _option(grid, "默认难度", SettingsStore.DIFFICULTY_LABELS)
	_game_speed = _option(grid, "默认游戏速度", SettingsStore.GAME_SPEED_LABELS)
	_tips = _check(grid, "提示与教程")
	_difficulty.item_selected.connect(_on_option.bind(SettingsStore.SECTION_GAME, "difficulty"))
	_game_speed.item_selected.connect(_on_option.bind(SettingsStore.SECTION_GAME, "game_speed"))
	_tips.toggled.connect(_on_toggle.bind(SettingsStore.SECTION_GAME, "tips"))
	return body


func _build_keyboard_tab() -> Control:
	var body := _tab_body("KeyboardTab")
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 6)
	body.add_child(column)

	var hint := Label.new()
	hint.text = "点击“重新绑定”后按下新的按键或鼠标滚轮；与该功能冲突的按键不会被接受。"
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(hint)

	for definition: Dictionary in _store().action_definitions():
		var action: String = definition["action"]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		column.add_child(row)

		var name_label := Label.new()
		name_label.text = str(definition["label"])
		name_label.custom_minimum_size = Vector2(140, 0)
		row.add_child(name_label)

		var binding_label := Label.new()
		binding_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_binding_labels[action] = binding_label
		row.add_child(binding_label)

		var rebind := Button.new()
		rebind.text = "重新绑定"
		rebind.pressed.connect(_begin_capture.bind(action))
		row.add_child(rebind)
	return body


func _build_button_row() -> Control:
	var row := HBoxContainer.new()
	row.name = "ButtonRow"
	row.alignment = BoxContainer.ALIGNMENT_END
	row.add_theme_constant_override("separation", 8)

	var reset := Button.new()
	reset.name = "ResetButton"
	reset.text = "恢复默认"
	reset.pressed.connect(_on_reset_pressed)
	row.add_child(reset)

	var cancel := Button.new()
	cancel.name = "CancelButton"
	cancel.text = "取消"
	cancel.pressed.connect(_on_cancel_pressed)
	row.add_child(cancel)

	var apply := Button.new()
	apply.name = "ApplyButton"
	apply.text = "应用"
	apply.pressed.connect(_on_apply_pressed)
	row.add_child(apply)
	return row


func _tab_body(node_name: String) -> MarginContainer:
	var body := MarginContainer.new()
	body.name = node_name
	for side in ["left", "top", "right", "bottom"]:
		body.add_theme_constant_override("margin_" + side, 12)
	return body


func _grid(parent: Control) -> GridContainer:
	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 16)
	grid.add_theme_constant_override("v_separation", 10)
	parent.add_child(grid)
	return grid


func _option(grid: GridContainer, label_text: String, items: Array) -> OptionButton:
	var label := Label.new()
	label.text = label_text
	grid.add_child(label)
	var option := OptionButton.new()
	option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for item in items:
		option.add_item(str(item))
	grid.add_child(option)
	return option


func _check(grid: GridContainer, label_text: String) -> CheckButton:
	var label := Label.new()
	label.text = label_text
	grid.add_child(label)
	var check := CheckButton.new()
	grid.add_child(check)
	return check


func _slider_row(grid: GridContainer, label_text: String, min_value: float, max_value: float,
		step: float) -> Array:
	var label := Label.new()
	label.text = label_text
	grid.add_child(label)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	grid.add_child(row)
	var slider := HSlider.new()
	slider.min_value = min_value
	slider.max_value = max_value
	slider.step = step
	slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	slider.custom_minimum_size = Vector2(220, 0)
	row.add_child(slider)
	var value_label := Label.new()
	value_label.custom_minimum_size = Vector2(52, 0)
	row.add_child(value_label)
	return [slider, value_label]


func _refresh_widgets() -> void:
	var display: Dictionary = _pending[SettingsStore.SECTION_DISPLAY]
	var audio: Dictionary = _pending[SettingsStore.SECTION_AUDIO]
	var game: Dictionary = _pending[SettingsStore.SECTION_GAME]
	_window_mode.select(clampi(int(display["window_mode"]), 0, SettingsStore.WINDOW_MODE_LABELS.size() - 1))
	_resolution.select(clampi(int(display["resolution"]), 0, SettingsStore.RESOLUTION_LABELS.size() - 1))
	_vsync.set_pressed_no_signal(bool(display["vsync"]))
	_quality.select(clampi(int(display["quality"]), 0, SettingsStore.QUALITY_LABELS.size() - 1))
	_ui_scale.set_value_no_signal(int(display["ui_scale"]))
	_color_blind.select(clampi(int(display["color_blind"]), 0, SettingsStore.COLOR_BLIND_LABELS.size() - 1))
	_subtitles.set_pressed_no_signal(bool(display["subtitles"]))
	_master.set_value_no_signal(int(audio["master"]))
	_sfx.set_value_no_signal(int(audio["sfx"]))
	_music.set_value_no_signal(int(audio["music"]))
	_difficulty.select(clampi(int(game["difficulty"]), 0, SettingsStore.DIFFICULTY_LABELS.size() - 1))
	_game_speed.select(clampi(int(game["game_speed"]), 0, SettingsStore.GAME_SPEED_LABELS.size() - 1))
	_tips.set_pressed_no_signal(bool(game["tips"]))
	_refresh_slider_labels()
	_refresh_binding_labels()


func _refresh_slider_labels() -> void:
	_ui_scale_label.text = "%d%%" % int(_ui_scale.value)
	_master_label.text = "%d%%" % int(_master.value)
	_sfx_label.text = "%d%%" % int(_sfx.value)
	_music_label.text = "%d%%" % int(_music.value)


func _refresh_binding_labels() -> void:
	for definition: Dictionary in _store().action_definitions():
		var action: String = definition["action"]
		var label: Label = _binding_labels.get(action)
		if label == null:
			continue
		var parts: PackedStringArray = []
		for binding: Dictionary in _pending_bindings.get(action, []):
			parts.append(_store().binding_label(binding))
		label.text = " / ".join(parts) if not parts.is_empty() else "未绑定"


func _on_option(index: int, section: String, key: String) -> void:
	_pending[section][key] = index


func _on_toggle(pressed: bool, section: String, key: String) -> void:
	_pending[section][key] = pressed


func _on_slider(value: float, section: String, key: String) -> void:
	_pending[section][key] = int(round(value))
	_refresh_slider_labels()


func _begin_capture(action: String) -> void:
	_capture_action = action
	_status.text = "请按下新的按键或鼠标滚轮，Esc 取消。"


func _input(event: InputEvent) -> void:
	if not event is InputEventKey and not event is InputEventMouseButton:
		return
	if event is InputEventKey:
		var key := event as InputEventKey
		if not key.pressed or key.echo:
			return
		if key.keycode == KEY_ESCAPE:
			get_viewport().set_input_as_handled()
			if _capture_action.is_empty():
				_on_cancel_pressed()
			else:
				_cancel_capture()
			return
		if _capture_action.is_empty():
			return
		var keycode := key.physical_keycode if key.physical_keycode != 0 else key.keycode
		_commit_capture({"type": "key", "keycode": int(keycode)})
		get_viewport().set_input_as_handled()
		return
	var mouse := event as InputEventMouseButton
	if not mouse.pressed:
		return
	if _capture_action.is_empty():
		return
	if mouse.button_index != MOUSE_BUTTON_WHEEL_UP and mouse.button_index != MOUSE_BUTTON_WHEEL_DOWN:
		return
	_commit_capture({"type": "mouse", "button": mouse.button_index})
	get_viewport().set_input_as_handled()


func _commit_capture(binding: Dictionary) -> void:
	var conflict := _pending_conflict(_capture_action, binding)
	if conflict != "":
		_status.text = "冲突：该按键已被「%s」使用，请换一个。" % conflict
		_capture_action = ""
		return
	_pending_bindings[_capture_action] = [binding]
	_refresh_binding_labels()
	_status.text = "「%s」已改为 %s，点击“应用”后生效。" % [
		_action_label(_capture_action), _store().binding_label(binding),
	]
	_capture_action = ""


func _cancel_capture() -> void:
	_capture_action = ""
	_status.text = "已取消本次按键绑定。"


func _pending_conflict(action: String, binding: Dictionary) -> String:
	for definition: Dictionary in _store().action_definitions():
		var other: String = definition["action"]
		if other == action:
			continue
		for existing: Dictionary in _pending_bindings.get(other, []):
			if _store().same_binding(existing, binding):
				return str(definition["label"])
	return ""


func _action_label(action: String) -> String:
	for definition: Dictionary in _store().action_definitions():
		if definition["action"] == action:
			return str(definition["label"])
	return action


func _on_reset_pressed() -> void:
	_pending = {
		SettingsStore.SECTION_DISPLAY: _store().defaults_for(SettingsStore.SECTION_DISPLAY),
		SettingsStore.SECTION_AUDIO: _store().defaults_for(SettingsStore.SECTION_AUDIO),
		SettingsStore.SECTION_GAME: _store().defaults_for(SettingsStore.SECTION_GAME),
	}
	_pending_bindings = _store().default_bindings()
	_refresh_widgets()
	_status.text = "已恢复默认值，点击“应用”后生效。"


func _on_apply_pressed() -> void:
	for section: String in _pending:
		for key: String in _pending[section]:
			_store().set_value(section, key, _pending[section][key])
	for action: String in _pending_bindings:
		_store().set_bindings(action, _pending_bindings[action])
	_store().apply()
	_store().save()
	closed.emit(true)
	queue_free()


func _on_cancel_pressed() -> void:
	closed.emit(false)
	queue_free()
