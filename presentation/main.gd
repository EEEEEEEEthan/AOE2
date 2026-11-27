extends Node3D

const DEDICATED_FEATURE := "dedicated_server"
const SERVER_ARGUMENT := "--server"
const DEFAULT_PORT := 27015
const DEFAULT_SEED := 20261127
const DEFAULT_PLAYER_COUNT := 2
const ARABIA_MAP_TYPE := 0
const DEFAULT_AI_DIFFICULTY := 1
const TEAM_NAMES := {
	0: "蓝方",
	1: "红方",
}
const TEAM_COLORS := [
	Color(0.32, 0.56, 1.0, 1.0),
	Color(1.0, 0.42, 0.36, 1.0),
	Color(0.45, 0.86, 0.45, 1.0),
	Color(0.95, 0.8, 0.3, 1.0),
]
const SCORE_TABLE_HEADERS := ["阵营", "总分", "军事", "经济", "科技", "击杀", "损失", "采集", "存活", "状态"]
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
var _last_tick := -1
var _result_built_tick := -1
var _result_hidden := false


func _ready() -> void:
	bridge = GameBridge.new()
	add_child(bridge)
	%ResultPanel.hide()
	%ReopenButton.hide()
	if _is_dedicated():
		%TechTreeButton.hide()
		bridge.StartDedicatedServer(_server_port())
		return
	if MatchSetup.has_network():
		_adopt_network_match()
		return
	if MatchSetup.has_prepared():
		_adopt_prepared_match()
		return
	_start_session()
	%WorldView.build_terrain(bridge)
	_finish_setup()


## 大厅已连上服务器，本机只消费快照渲染与转发指令。
func _adopt_network_match() -> void:
	bridge.AdoptMultiplayer(MatchSetup.take_network())
	if bridge.TerrainWidth > 0:
		%WorldView.build_terrain(bridge)
	_finish_setup()


## 加载场景已生成对局与地形，这里直接接管，避免再次同步建图黑屏。
func _adopt_prepared_match() -> void:
	var prepared_bridge := MatchSetup.take_bridge() as GameBridge
	var prepared_world := MatchSetup.take_world() as WorldView
	if prepared_bridge == null or prepared_world == null:
		_start_session()
		%WorldView.build_terrain(bridge)
		_finish_setup()
		return
	bridge.queue_free()
	bridge = prepared_bridge
	add_child(bridge)
	%WorldView.adopt(prepared_world)
	_finish_setup()


func _finish_setup() -> void:
	%WorldView.refresh(bridge)
	%BattleController.setup(bridge)
	_last_tick = bridge.ClientTick()


func _exit_tree() -> void:
	Engine.time_scale = 1.0


func _start_session() -> void:
	var civs: PackedInt32Array = MatchSetup.config.get("civ_ids", PackedInt32Array())
	var player_count := int(MatchSetup.config.get("player_count", DEFAULT_PLAYER_COUNT))
	bridge.StartGeneratedMatch(DEFAULT_SEED, player_count, ARABIA_MAP_TYPE, DEFAULT_AI_DIFFICULTY, civs)


func _process(delta: float) -> void:
	if bridge == null or not bridge.HasSession:
		return
	bridge.Step(delta)
	if bridge.IsRemote and bridge.TerrainWidth > 0 and not %WorldView.is_terrain_ready():
		%WorldView.build_terrain(bridge)
	var tick := bridge.ClientTick()
	if tick != _last_tick:
		_last_tick = tick
		%WorldView.refresh(bridge)
		%BattleController.refresh()
	_refresh_result()


func _refresh_result() -> void:
	if not bridge.MatchEnded():
		%ResultPanel.hide()
		%ReopenButton.hide()
		_result_built_tick = -1
		_result_hidden = false
		_set_play_hud_visible(true)
		return
	if not bridge.IsReplay:
		var saved: String = str(bridge.SaveReplayIfNeeded())
		if not saved.is_empty() and %SaveReplayButton.text != "本局录像已保存":
			%SaveReplayButton.text = "本局录像已保存"
	if _result_built_tick != bridge.MatchEndTick():
		_result_built_tick = bridge.MatchEndTick()
		_result_hidden = false
		_build_result()
	%ResultPanel.visible = not _result_hidden
	%ReopenButton.visible = _result_hidden


## 结算期间隐藏会误操作的建造/生产 HUD；对局仍在进行时恢复。
func _set_play_hud_visible(visible_state: bool) -> void:
	get_node("Hud/CommandPanel").visible = visible_state
	get_node("Hud/MenuBar").visible = visible_state
	if not visible_state:
		%MarketPanel.hide()


func _build_result() -> void:
	%ResultBannerLabel.text = _result_title()
	var winner := bridge.MatchWinnerTeam()
	var banner_color := Color(0.3, 0.34, 0.42, 0.95) if winner < 0 else _team_color(winner)
	var style := StyleBoxFlat.new()
	style.bg_color = banner_color
	style.corner_radius_top_left = 6
	style.corner_radius_top_right = 6
	style.corner_radius_bottom_left = 6
	style.corner_radius_bottom_right = 6
	%ResultBanner.add_theme_stylebox_override("panel", style)
	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color = Color(0.08, 0.1, 0.14, 0.97)
	panel_style.border_color = Color(0.35, 0.4, 0.48, 0.9)
	panel_style.set_border_width_all(2)
	panel_style.set_corner_radius_all(8)
	%ResultPanel.add_theme_stylebox_override("panel", panel_style)
	%ResultDetail.text = "胜利条件：%s　结束于第 %d tick" % [
		REASON_NAMES.get(bridge.MatchReason(), "未知"),
		bridge.MatchEndTick(),
	]
	_build_score_table()
	_build_score_chart()
	get_node("Hud/CommandPanel").hide()
	get_node("Hud/MenuBar").hide()
	%MarketPanel.hide()


func _build_score_table() -> void:
	for child in %ScoreTable.get_children():
		child.free()
	for header in SCORE_TABLE_HEADERS:
		var label := Label.new()
		label.text = header
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.add_theme_font_size_override("font_size", 14)
		%ScoreTable.add_child(label)
	for player: Dictionary in bridge.PlayerStates():
		var team: int = player["team"]
		var cells := [
			TEAM_NAMES.get(team, "玩家 %d" % team),
			str(player["score"]),
			str(player["score_military"]),
			str(player["score_economy"]),
			str(player["score_tech"]),
			str(player["kills"]),
			str(player["losses"]),
			str(player["gathered"]),
			str(player["participants"]),
			_player_state_text(player),
		]
		for index in cells.size():
			var label := Label.new()
			label.text = str(cells[index])
			label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT if index == 0 else HORIZONTAL_ALIGNMENT_RIGHT
			if index == 0:
				label.add_theme_color_override("font_color", _team_color(team))
			%ScoreTable.add_child(label)


func _build_score_chart() -> void:
	var samples: Array = []
	for entry in bridge.ScoreTimeline():
		samples.append(entry)
	var end_tick := int(bridge.MatchEndTick())
	var last_tick := int(samples[-1]["tick"]) if not samples.is_empty() else -1
	if last_tick < end_tick:
		var scores: Array = []
		for player: Dictionary in bridge.PlayerStates():
			scores.append(int(player["score"]))
		samples.append({"tick": end_tick, "scores": scores})
	var colors: Array = []
	var player_count := bridge.PlayerStates().size()
	for team in player_count:
		colors.append(_team_color(team))
	%ScoreChart.set_series(samples, player_count, colors, end_tick)


func _team_color(team: int) -> Color:
	return TEAM_COLORS[team % TEAM_COLORS.size()]


func _on_continue_spectating_pressed() -> void:
	_hide_result_for_viewing("继续观战中：可自由移动镜头，点击右上角「查看结算」重新打开")


func _on_view_map_pressed() -> void:
	_hide_result_for_viewing("查看地图中：点击右上角「查看结算」重新打开")


func _hide_result_for_viewing(message: String) -> void:
	_result_hidden = true
	%ResultPanel.hide()
	%ReopenButton.show()
	%NoticeLabel.text = message


func _on_reopen_result_pressed() -> void:
	_result_hidden = false
	%ReopenButton.hide()
	%ResultPanel.show()


func _on_save_replay_pressed() -> void:
	var saved: String = str(bridge.SaveReplay())
	if saved.is_empty():
		%UnavailableDialog.dialog_text = "保存录像失败：%s" % bridge.LastReplayError
		%UnavailableDialog.popup_centered()
		return
	%SaveReplayButton.text = "本局录像已保存"
	%NoticeLabel.text = "录像已保存，可在主菜单「观看录像」中重放"


func _result_title() -> String:
	var winner := bridge.MatchWinnerTeam()
	if winner < 0:
		return "平局"
	return "%s 胜利" % TEAM_NAMES.get(winner, "玩家 %d" % winner)


func _player_state_text(player: Dictionary) -> String:
	if player["winner"] != 0:
		return "获胜"
	if player["defeated"] != 0:
		return "已淘汰"
	return "未获胜"


func _on_tech_tree_pressed() -> void:
	%TechTreeButton.hide()
	%TechTree.open(bridge)


func _on_tech_tree_closed() -> void:
	%TechTreeButton.show()


func _on_surrender_pressed() -> void:
	bridge.Surrender()


func _on_return_to_menu_pressed() -> void:
	get_tree().change_scene_to_file("res://presentation/main_menu.tscn")


func _is_dedicated() -> bool:
	return OS.has_feature(DEDICATED_FEATURE) or (SERVER_ARGUMENT in OS.get_cmdline_user_args())


func _server_port() -> int:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--port="):
			return int(argument.trim_prefix("--port="))
	return DEFAULT_PORT
