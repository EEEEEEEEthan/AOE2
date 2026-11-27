extends Control

const LOADING_SCENE := "res://presentation/loading.tscn"
const MATCH_SCENE := "res://presentation/main.tscn"
const MAP_EDITOR_SCENE := "res://tools/map_editor.tscn"
const REPLAY_SCENE := "res://presentation/replay.tscn"
const LOAD_GAME_SCENE := "res://presentation/load_game.tscn"
const LOBBY_SCENE := "res://presentation/lobby.tscn"
const OPTIONS_SCENE := "res://presentation/options.tscn"
const MATCH_SETUP_SCENE := "res://presentation/match_setup_dialog.tscn"
const DEDICATED_FEATURE := "dedicated_server"
const SERVER_ARGUMENT := "--server"


func _ready() -> void:
	if _is_dedicated():
		_enter_dedicated_match.call_deferred()


func _enter_dedicated_match() -> void:
	get_tree().change_scene_to_file(MATCH_SCENE)


func _on_standard_game_pressed() -> void:
	MatchSetup.reset_config()
	_open_match_setup()


## 先让玩家选择人数与文明，确认后再进入加载场景生成对局。
func _open_match_setup() -> void:
	var dialog: Node = load(MATCH_SETUP_SCENE).instantiate()
	add_child(dialog)
	dialog.confirmed.connect(_on_match_setup_confirmed)


func _on_match_setup_confirmed() -> void:
	get_tree().change_scene_to_file(LOADING_SCENE)


func _on_map_editor_pressed() -> void:
	get_tree().change_scene_to_file(MAP_EDITOR_SCENE)


func _on_custom_map_pressed() -> void:
	%CustomMapDialog.popup_centered_ratio(0.7)


func _on_custom_map_selected(path: String) -> void:
	MatchSetup.reset_config()
	MatchSetup.config["map_path"] = path
	MatchSetup.config["map_name"] = path.get_file().get_basename()
	_open_match_setup()


func _on_replay_pressed() -> void:
	get_tree().change_scene_to_file(REPLAY_SCENE)


func _on_load_game_pressed() -> void:
	get_tree().change_scene_to_file(LOAD_GAME_SCENE)


func _on_multiplayer_pressed() -> void:
	get_tree().change_scene_to_file(LOBBY_SCENE)


func _on_unavailable_pressed() -> void:
	%UnavailableDialog.popup_centered()


func _on_options_pressed() -> void:
	var options: Node = load(OPTIONS_SCENE).instantiate()
	add_child(options)


func _on_quit_pressed() -> void:
	get_tree().quit()


func _is_dedicated() -> bool:
	return OS.has_feature(DEDICATED_FEATURE) or (SERVER_ARGUMENT in OS.get_cmdline_user_args())
