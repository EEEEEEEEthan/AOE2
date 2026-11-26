extends Control

const LOADING_SCENE := "res://presentation/loading.tscn"
const MATCH_SCENE := "res://presentation/main.tscn"
const MAP_EDITOR_SCENE := "res://tools/map_editor.tscn"
const REPLAY_SCENE := "res://presentation/replay.tscn"
const LOBBY_SCENE := "res://presentation/lobby.tscn"
const DEDICATED_FEATURE := "dedicated_server"
const SERVER_ARGUMENT := "--server"


func _ready() -> void:
	if _is_dedicated():
		_enter_dedicated_match.call_deferred()


func _enter_dedicated_match() -> void:
	get_tree().change_scene_to_file(MATCH_SCENE)


func _on_standard_game_pressed() -> void:
	MatchSetup.reset_config()
	get_tree().change_scene_to_file(LOADING_SCENE)


func _on_map_editor_pressed() -> void:
	get_tree().change_scene_to_file(MAP_EDITOR_SCENE)


func _on_custom_map_pressed() -> void:
	%CustomMapDialog.popup_centered_ratio(0.7)


func _on_custom_map_selected(path: String) -> void:
	MatchSetup.reset_config()
	MatchSetup.config["map_path"] = path
	MatchSetup.config["map_name"] = path.get_file().get_basename()
	get_tree().change_scene_to_file(LOADING_SCENE)


func _on_replay_pressed() -> void:
	get_tree().change_scene_to_file(REPLAY_SCENE)


func _on_multiplayer_pressed() -> void:
	get_tree().change_scene_to_file(LOBBY_SCENE)


func _on_unavailable_pressed() -> void:
	%UnavailableDialog.popup_centered()


func _on_quit_pressed() -> void:
	get_tree().quit()


func _is_dedicated() -> bool:
	return OS.has_feature(DEDICATED_FEATURE) or (SERVER_ARGUMENT in OS.get_cmdline_user_args())
