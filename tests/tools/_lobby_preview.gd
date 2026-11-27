extends Node
## 截取多人大厅：房主建房后，第二个客户端加入并准备，展示槽位、队伍与准备状态。


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	for frame in 30:
		await get_tree().process_frame
	var menu := get_tree().current_scene
	menu.get_node("%MultiplayerButton").pressed.emit()
	for frame in 20:
		await get_tree().process_frame
	var lobby := get_tree().current_scene
	lobby.get_node("%HostButton").pressed.emit()
	for frame in 40:
		await get_tree().process_frame
	var guest := MultiplayerSession.new()
	add_child(guest)
	guest.JoinRoom("127.0.0.1", int(lobby.get_node("%PortSpin").value), "客人")
	for frame in 20:
		await get_tree().process_frame
	guest.SetProfile("客人", 3)
	guest.SetReady(true)
	for frame in 40:
		await get_tree().process_frame
	await _save("multiplayer_lobby.png")
	guest.Leave()
	get_tree().quit(0)


func _save(file_name: String) -> void:
	await RenderingServer.frame_post_draw
	var image := get_viewport().get_texture().get_image()
	var path := _run_directory().path_join(file_name)
	image.save_png(path)
	print("SCREENSHOT %s %dx%d" % [path, image.get_width(), image.get_height()])


func _run_directory() -> String:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--run-directory="):
			return argument.trim_prefix("--run-directory=")
	return OS.get_user_data_dir()
