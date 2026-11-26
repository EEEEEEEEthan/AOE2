extends Node3D

const DEDICATED_FEATURE := "dedicated_server"
const SERVER_ARGUMENT := "--server"
const DEFAULT_PORT := 27015

var bridge: GameBridge


func _ready() -> void:
	bridge = GameBridge.new()
	add_child(bridge)
	if _is_dedicated():
		bridge.StartDedicatedServer(_server_port())
		return
	bridge.StartLocalDemo()
	%WorldView.build_terrain(bridge.GridWidth, bridge.GridHeight, bridge.GridBlocked())
	%WorldView.refresh(bridge)


func _process(delta: float) -> void:
	if bridge == null or not bridge.HasSession:
		return
	bridge.Step(delta)
	%WorldView.refresh(bridge)


func _is_dedicated() -> bool:
	return OS.has_feature(DEDICATED_FEATURE) or (SERVER_ARGUMENT in OS.get_cmdline_user_args())


func _server_port() -> int:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--port="):
			return int(argument.trim_prefix("--port="))
	return DEFAULT_PORT
