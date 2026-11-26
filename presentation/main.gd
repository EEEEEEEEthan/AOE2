extends Node3D

const DEDICATED_FEATURE := "dedicated_server"
const SERVER_ARGUMENT := "--server"
const DEFAULT_PORT := 27015
const TEAM_NAMES := {
	0: "蓝方",
	1: "红方",
}
const REASON_NAMES := {
	-1: "无",
	0: "征服",
	1: "弑君",
	2: "奇观",
	3: "圣物",
	4: "分数",
	5: "投降",
	6: "平局",
}

var bridge: GameBridge


func _ready() -> void:
	bridge = GameBridge.new()
	add_child(bridge)
	%ResultPanel.hide()
	if _is_dedicated():
		%SurrenderButton.hide()
		bridge.StartDedicatedServer(_server_port())
		return
	_start_session()
	%WorldView.build_terrain(bridge.GridWidth, bridge.GridHeight, bridge.GridBlocked())
	%WorldView.refresh(bridge)


func _start_session() -> void:
	bridge.StartLocalMatch()


func _process(delta: float) -> void:
	if bridge == null or not bridge.HasSession:
		return
	bridge.Step(delta)
	%WorldView.refresh(bridge)
	_refresh_result()


func _refresh_result() -> void:
	if not bridge.MatchEnded():
		%ResultPanel.hide()
		%SurrenderButton.show()
		return
	%ResultPanel.show()
	%SurrenderButton.hide()
	%ResultTitle.text = _result_title()
	%ResultDetail.text = "胜利条件：%s　结束于第 %d tick" % [
		REASON_NAMES.get(bridge.MatchReason(), "未知"),
		bridge.MatchEndTick(),
	]
	%ResultStats.text = _result_stats()


func _result_title() -> String:
	var winner := bridge.MatchWinnerTeam()
	if winner < 0:
		return "平局"
	return "%s 胜利" % TEAM_NAMES.get(winner, "玩家 %d" % winner)


func _result_stats() -> String:
	var lines: PackedStringArray = []
	for player: Dictionary in bridge.PlayerStates():
		var team: int = player["team"]
		var state := _player_state_text(player)
		lines.append("%s　%d 分　单位/建筑 %d　木%d 食%d 金%d 石%d　%s" % [
			TEAM_NAMES.get(team, "玩家 %d" % team),
			player["score"],
			player["participants"],
			player["wood"],
			player["food"],
			player["gold"],
			player["stone"],
			state,
		])
	return "\n".join(lines)


func _player_state_text(player: Dictionary) -> String:
	if player["winner"] != 0:
		return "获胜"
	if player["defeated"] != 0:
		return "已淘汰"
	return "未获胜"


func _on_surrender_pressed() -> void:
	bridge.Surrender()


func _on_exit_pressed() -> void:
	get_tree().quit()


func _is_dedicated() -> bool:
	return OS.has_feature(DEDICATED_FEATURE) or (SERVER_ARGUMENT in OS.get_cmdline_user_args())


func _server_port() -> int:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--port="):
			return int(argument.trim_prefix("--port="))
	return DEFAULT_PORT
