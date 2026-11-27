class_name SettingsStore
extends Node
## 玩家设置的唯一来源：内存值 + ConfigFile 持久化到 user://settings.cfg，
## 并把画面/音频/快捷键落到 DisplayServer、AudioServer 与 InputMap。

const SETTINGS_PATH := "user://settings.cfg"

const SECTION_DISPLAY := "display"
const SECTION_AUDIO := "audio"
const SECTION_GAME := "game"
const SECTION_KEYBOARD := "keyboard"

const RESOLUTIONS := [
	Vector2i(0, 0),
	Vector2i(1280, 720),
	Vector2i(1600, 900),
	Vector2i(1920, 1080),
	Vector2i(2560, 1440),
]
const RESOLUTION_LABELS := ["默认", "1280 × 720", "1600 × 900", "1920 × 1080", "2560 × 1440"]

const WINDOW_MODES := [
	DisplayServer.WINDOW_MODE_WINDOWED,
	DisplayServer.WINDOW_MODE_FULLSCREEN,
	DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN,
]
const WINDOW_MODE_LABELS := ["窗口", "全屏", "独占全屏"]

const QUALITY_LABELS := ["关闭", "2× MSAA", "4× MSAA"]
const QUALITY_MSAA := [Viewport.MSAA_DISABLED, Viewport.MSAA_2X, Viewport.MSAA_4X]

const COLOR_BLIND_LABELS := ["无", "红色盲", "绿色盲", "蓝色盲"]
const DIFFICULTY_LABELS := ["简单", "普通", "困难"]
const GAME_SPEED_LABELS := ["0.5×", "1×", "1.5×", "2×"]
const GAME_SPEEDS := [0.5, 1.0, 1.5, 2.0]

## color_blind / subtitles 为预留设置位：当前仓库无色盲配色与字幕渲染系统，
## 仅保留可持久化的值，后续接入时在此处消费。
const DISPLAY_DEFAULTS := {
	"window_mode": 0,
	"resolution": 0,
	"vsync": 1,
	"quality": 0,
	"ui_scale": 100,
	"color_blind": 0,
	"subtitles": true,
}
const AUDIO_DEFAULTS := {"master": 80, "sfx": 80, "music": 60}
## difficulty 由 MatchSetup 决定新对局 AI 难度；game_speed 由对局场景初始化
## time_scale；tips 控制加载页提示。三者消费点见 presentation 下对应脚本。
const GAME_DEFAULTS := {"difficulty": 1, "game_speed": 1, "tips": true}

const ACTION_DEFINITIONS := [
	{
		"action": "camera_pan_forward",
		"label": "镜头上移",
		"bindings": [{"type": "key", "keycode": KEY_W}, {"type": "key", "keycode": KEY_UP}],
	},
	{
		"action": "camera_pan_back",
		"label": "镜头下移",
		"bindings": [{"type": "key", "keycode": KEY_S}, {"type": "key", "keycode": KEY_DOWN}],
	},
	{
		"action": "camera_pan_left",
		"label": "镜头左移",
		"bindings": [{"type": "key", "keycode": KEY_A}, {"type": "key", "keycode": KEY_LEFT}],
	},
	{
		"action": "camera_pan_right",
		"label": "镜头右移",
		"bindings": [{"type": "key", "keycode": KEY_D}, {"type": "key", "keycode": KEY_RIGHT}],
	},
	{
		"action": "camera_zoom_in",
		"label": "镜头拉近",
		"bindings": [{"type": "mouse", "button": MOUSE_BUTTON_WHEEL_UP}],
	},
	{
		"action": "camera_zoom_out",
		"label": "镜头拉远",
		"bindings": [{"type": "mouse", "button": MOUSE_BUTTON_WHEEL_DOWN}],
	},
]

var _display: Dictionary = {}
var _audio: Dictionary = {}
var _game: Dictionary = {}
var _bindings: Dictionary = {}


func _ready() -> void:
	reload()
	apply()


func reload() -> void:
	_display = DISPLAY_DEFAULTS.duplicate(true)
	_audio = AUDIO_DEFAULTS.duplicate(true)
	_game = GAME_DEFAULTS.duplicate(true)
	_bindings = _default_bindings()
	var config := ConfigFile.new()
	if config.load(SETTINGS_PATH) != OK:
		return
	_load_section(config, SECTION_DISPLAY, _display)
	_load_section(config, SECTION_AUDIO, _audio)
	_load_section(config, SECTION_GAME, _game)
	for definition: Dictionary in ACTION_DEFINITIONS:
		var action: String = definition["action"]
		_bindings[action] = _normalize_bindings(
			config.get_value(SECTION_KEYBOARD, action, null), definition["bindings"])


func save() -> void:
	var config := ConfigFile.new()
	_store_section(config, SECTION_DISPLAY, _display)
	_store_section(config, SECTION_AUDIO, _audio)
	_store_section(config, SECTION_GAME, _game)
	for action: String in _bindings:
		config.set_value(SECTION_KEYBOARD, action, _bindings[action])
	config.save(SETTINGS_PATH)


func apply() -> void:
	_apply_input_map()
	_apply_display()
	_apply_audio()


func reset_defaults() -> void:
	_display = DISPLAY_DEFAULTS.duplicate(true)
	_audio = AUDIO_DEFAULTS.duplicate(true)
	_game = GAME_DEFAULTS.duplicate(true)
	_bindings = _default_bindings()


func value(section: String, key: String) -> Variant:
	return _section(section).get(key, null)


func set_value(section: String, key: String, new_value: Variant) -> void:
	var target := _section(section)
	if target.has(key):
		target[key] = new_value


func section_copy(section: String) -> Dictionary:
	return _section(section).duplicate(true)


func default_bindings() -> Dictionary:
	return _default_bindings()


func defaults_for(section: String) -> Dictionary:
	match section:
		SECTION_DISPLAY:
			return DISPLAY_DEFAULTS.duplicate(true)
		SECTION_AUDIO:
			return AUDIO_DEFAULTS.duplicate(true)
		SECTION_GAME:
			return GAME_DEFAULTS.duplicate(true)
	return {}


func bindings_for(action: String) -> Array:
	return _bindings.get(action, []).duplicate(true)


func set_bindings(action: String, bindings: Array) -> void:
	if _bindings.has(action):
		_bindings[action] = _normalize_bindings(bindings, [])


func action_definitions() -> Array:
	return ACTION_DEFINITIONS


func binding_conflict(action: String, binding: Dictionary) -> String:
	for definition: Dictionary in ACTION_DEFINITIONS:
		var other: String = definition["action"]
		if other == action:
			continue
		for existing: Dictionary in _bindings.get(other, []):
			if same_binding(existing, binding):
				return str(definition["label"])
	return ""


func same_binding(first: Dictionary, second: Dictionary) -> bool:
	if str(first.get("type", "key")) != str(second.get("type", "key")):
		return false
	if str(first.get("type", "key")) == "mouse":
		return int(first.get("button", -1)) == int(second.get("button", -2))
	return int(first.get("keycode", 0)) == int(second.get("keycode", 0))


func binding_label(binding: Dictionary) -> String:
	if str(binding.get("type", "key")) == "mouse":
		match int(binding.get("button", -1)):
			MOUSE_BUTTON_WHEEL_UP:
				return "滚轮上"
			MOUSE_BUTTON_WHEEL_DOWN:
				return "滚轮下"
			_:
				return "鼠标 %d" % int(binding.get("button", -1))
	var keycode := int(binding.get("keycode", 0))
	return OS.get_keycode_string(keycode) if keycode != 0 else "未绑定"


func _section(section: String) -> Dictionary:
	match section:
		SECTION_DISPLAY:
			return _display
		SECTION_AUDIO:
			return _audio
		SECTION_GAME:
			return _game
	return {}


func _load_section(config: ConfigFile, section: String, target: Dictionary) -> void:
	for key: String in target:
		target[key] = config.get_value(section, key, target[key])


func _store_section(config: ConfigFile, section: String, source: Dictionary) -> void:
	for key: String in source:
		config.set_value(section, key, source[key])


func _default_bindings() -> Dictionary:
	var result := {}
	for definition: Dictionary in ACTION_DEFINITIONS:
		result[definition["action"]] = (definition["bindings"] as Array).duplicate(true)
	return result


func _normalize_bindings(stored: Variant, fallback: Array) -> Array:
	if not stored is Array or (stored as Array).is_empty():
		return fallback.duplicate(true)
	var result: Array = []
	for item in stored:
		if not item is Dictionary:
			continue
		var entry: Dictionary = item
		if str(entry.get("type", "key")) == "mouse":
			result.append({"type": "mouse", "button": int(entry.get("button", MOUSE_BUTTON_WHEEL_UP))})
		else:
			result.append({"type": "key", "keycode": int(entry.get("keycode", 0))})
	return result if not result.is_empty() else fallback.duplicate(true)


func _apply_input_map() -> void:
	for definition: Dictionary in ACTION_DEFINITIONS:
		var action: String = definition["action"]
		if not InputMap.has_action(action):
			InputMap.add_action(action)
		InputMap.action_erase_events(action)
		for binding: Dictionary in _bindings.get(action, []):
			InputMap.action_add_event(action, _event_from_binding(binding))


func _event_from_binding(binding: Dictionary) -> InputEvent:
	if str(binding.get("type", "key")) == "mouse":
		var mouse_button := InputEventMouseButton.new()
		mouse_button.button_index = int(binding.get("button", MOUSE_BUTTON_LEFT))
		return mouse_button
	var key := InputEventKey.new()
	key.physical_keycode = int(binding.get("keycode", 0))
	return key


func _apply_display() -> void:
	var mode_index := clampi(int(_display["window_mode"]), 0, WINDOW_MODES.size() - 1)
	DisplayServer.window_set_mode(WINDOW_MODES[mode_index])
	var resolution_index := clampi(int(_display["resolution"]), 0, RESOLUTIONS.size() - 1)
	if mode_index == 0 and resolution_index > 0:
		DisplayServer.window_set_size(RESOLUTIONS[resolution_index])
	DisplayServer.window_set_vsync_mode(
		DisplayServer.VSYNC_ENABLED if int(_display["vsync"]) == 1 else DisplayServer.VSYNC_DISABLED)
	var viewport := get_viewport()
	if viewport == null:
		return
	var quality_index := clampi(int(_display["quality"]), 0, QUALITY_MSAA.size() - 1)
	viewport.msaa_3d = QUALITY_MSAA[quality_index]
	viewport.content_scale_factor = clampf(int(_display["ui_scale"]) / 100.0, 0.75, 1.5)


func _apply_audio() -> void:
	_ensure_bus("SFX")
	_ensure_bus("Music")
	_set_bus_volume("Master", int(_audio["master"]))
	_set_bus_volume("SFX", int(_audio["sfx"]))
	_set_bus_volume("Music", int(_audio["music"]))


func _ensure_bus(bus_name: String) -> void:
	if AudioServer.get_bus_index(bus_name) >= 0:
		return
	AudioServer.add_bus()
	AudioServer.set_bus_name(AudioServer.bus_count - 1, bus_name)


func _set_bus_volume(bus_name: String, percent: int) -> void:
	var index := AudioServer.get_bus_index(bus_name)
	if index < 0:
		return
	var clamped := clampi(percent, 0, 100)
	AudioServer.set_bus_mute(index, clamped == 0)
	AudioServer.set_bus_volume_db(index, linear_to_db(maxf(clamped / 100.0, 0.0001)))
