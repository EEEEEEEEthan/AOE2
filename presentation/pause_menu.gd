class_name PauseMenu
extends Control

signal surrender_requested
signal return_to_menu_requested

const OPTIONS_SCENE := "res://presentation/options.tscn"


func _ready() -> void:
	hide()
	%PauseUnavailableDialog.hide()
	%PauseSurrenderConfirm.hide()


func _unhandled_input(event: InputEvent) -> void:
	if not get_tree().paused or not visible:
		return
	if %PauseSurrenderConfirm.visible:
		return
	if not event is InputEventKey:
		return
	var key_event := event as InputEventKey
	if key_event.pressed and not key_event.echo and key_event.keycode == KEY_ESCAPE:
		get_viewport().set_input_as_handled()
		close()


func open() -> void:
	show()
	get_tree().paused = true


func close() -> void:
	hide()
	get_tree().paused = false


func _on_resume_pressed() -> void:
	close()


func _on_options_pressed() -> void:
	var options: Node = load(OPTIONS_SCENE).instantiate()
	add_child(options)


func _on_save_pressed() -> void:
	_show_unavailable("保存功能开发中，敬请期待。")


func _on_restart_pressed() -> void:
	_show_unavailable("重新开始功能开发中，敬请期待。")


func _on_surrender_pressed() -> void:
	%PauseSurrenderConfirm.popup_centered()


func _on_surrender_confirmed() -> void:
	close()
	surrender_requested.emit()


func _on_exit_pressed() -> void:
	close()
	return_to_menu_requested.emit()


func _show_unavailable(message: String) -> void:
	%PauseUnavailableDialog.dialog_text = message
	%PauseUnavailableDialog.popup_centered()
