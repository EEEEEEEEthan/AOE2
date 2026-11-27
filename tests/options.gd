extends TestBase

const OPTIONS_SCENE := "res://presentation/options.tscn"
const CAMERA_ACTIONS := [
	"camera_pan_forward", "camera_pan_back", "camera_pan_left",
	"camera_pan_right", "camera_zoom_in", "camera_zoom_out",
]


func run() -> void:
	var store := get_node("/root/Settings") as SettingsStore
	check_true(store != null, "settings autoload exists")
	if store == null:
		return
	_check_input_map(store)
	_check_conflict(store)
	_check_persistence(store)
	_check_options_scene()
	store.reset_defaults()
	store.apply()
	store.save()


func _check_input_map(store: SettingsStore) -> void:
	store.apply()
	for action: String in CAMERA_ACTIONS:
		check_true(InputMap.has_action(action), "InputMap registers %s" % action)
		check_true(InputMap.action_get_events(action).size() > 0, "%s has a default binding" % action)


func _check_conflict(store: SettingsStore) -> void:
	var forward: Dictionary = {"type": "key", "keycode": KEY_W}
	check_true(store.binding_conflict("camera_pan_back", forward) != "",
			"W is reported as a conflict for camera_pan_back")
	var free_binding: Dictionary = {"type": "key", "keycode": KEY_Q}
	check(store.binding_conflict("camera_pan_back", free_binding) == "",
			"an unused key is not a conflict")
	check_true(store.binding_conflict("camera_pan_back", {"type": "mouse", "button": MOUSE_BUTTON_WHEEL_UP}) != "",
			"wheel-up conflicts with the zoom-in action")


func _check_persistence(store: SettingsStore) -> void:
	store.set_value(SettingsStore.SECTION_DISPLAY, "vsync", 0)
	store.set_value(SettingsStore.SECTION_AUDIO, "master", 35)
	store.set_bindings("camera_pan_forward", [{"type": "key", "keycode": KEY_Q}])
	store.save()
	store.reload()
	check_eq(int(store.value(SettingsStore.SECTION_DISPLAY, "vsync")), 0,
			"display setting survives a save/reload")
	check_eq(int(store.value(SettingsStore.SECTION_AUDIO, "master")), 35,
			"audio setting survives a save/reload")
	var bindings := store.bindings_for("camera_pan_forward")
	check_eq(bindings.size(), 1, "rebinding keeps a single key")
	check_eq(int(bindings[0]["keycode"]), KEY_Q, "rebound key survives a save/reload")
	store.apply()
	var events := InputMap.action_get_events("camera_pan_forward")
	check_eq(events.size(), 1, "apply pushes the rebound key into InputMap")
	if events.size() == 1:
		check_eq(int((events[0] as InputEventKey).physical_keycode), KEY_Q,
				"InputMap event matches the rebound key")


func _check_options_scene() -> void:
	var scene := load(OPTIONS_SCENE) as PackedScene
	check_true(scene != null, "options scene loads")
	if scene == null:
		return
	var options: Control = scene.instantiate()
	add_child(options)
	var tabs := options.get_node("Center/Panel/Margin/Content/TabContainer") as TabContainer
	check_true(tabs != null, "options exposes a tab container")
	if tabs != null:
		check_eq(tabs.get_tab_count(), 4, "options has four tabs")
		check(tabs.get_tab_title(0) == "画面", "first tab is display")
		check(tabs.get_tab_title(1) == "音频", "second tab is audio")
		check(tabs.get_tab_title(2) == "游戏", "third tab is game")
		check(tabs.get_tab_title(3) == "快捷键", "fourth tab is keyboard")
	check_true(options.get_node("Center/Panel/Margin/Content/ButtonRow/ApplyButton") is Button,
			"options has an apply button")
	check_true(options.get_node("Center/Panel/Margin/Content/ButtonRow/CancelButton") is Button,
			"options has a cancel button")
	check_true(options.get_node("Center/Panel/Margin/Content/ButtonRow/ResetButton") is Button,
			"options has a reset button")
	options.queue_free()
