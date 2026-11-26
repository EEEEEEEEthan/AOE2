extends TestBase

const MENU_SCENE := "res://presentation/main_menu.tscn"
const MATCH_SCENE := "res://presentation/main.tscn"
const MAP_EDITOR_SCENE := "res://tools/map_editor.tscn"


func run() -> void:
	check(str(ProjectSettings.get_setting("application/run/main_scene")) == MENU_SCENE,
			"main scene is the menu")
	var menu_scene := load(MENU_SCENE) as PackedScene
	check(menu_scene != null, "menu scene loads")
	var menu := menu_scene.instantiate()
	add_child(menu)
	check(menu is Control, "menu root is a control")
	check(menu.get_node("%StandardGameButton") is Button, "menu offers standard game")
	check(menu.get_node("%MapEditorButton") is Button, "menu offers map editor")
	check(menu.get_node("%CustomMapButton") is Button, "menu offers custom map")
	check(menu.get_node("%CustomMapDialog") is FileDialog, "menu offers a map file picker")
	check(menu.get_node("%QuitButton") is Button, "menu offers quit")
	for child in menu.get_children():
		check(not child.has_method("StartLocalMatch"), "menu does not create a match bridge")
	menu.queue_free()

	var match_scene := load(MATCH_SCENE) as PackedScene
	var match_root := match_scene.instantiate()
	add_child(match_root)
	var bridge: Variant = match_root.get("bridge")
	check_true(bridge != null, "match scene creates a bridge")
	if bridge != null:
		check_true(bridge.HasSession, "direct match scene still starts a session")
	match_root.queue_free()

	var editor_scene := load(MAP_EDITOR_SCENE) as PackedScene
	var editor := editor_scene.instantiate()
	add_child(editor)
	check(editor.get_node("%MainMenuButton") is Button, "map editor returns to the menu")
	editor.queue_free()
