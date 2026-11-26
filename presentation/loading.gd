extends Control
## 对局加载场景：先渲染界面，再分帧生成地图与地形并刷新进度；
## 完成后把准备好的桥接与地形交给对局场景，失败时显示明确错误而非黑屏。

const MATCH_SCENE := "res://presentation/main.tscn"
const MAIN_MENU_SCENE := "res://presentation/main_menu.tscn"
const WORLD_VIEW := preload("res://presentation/world_view.gd")

const TEAM_NAMES := {
	0: "蓝方",
	1: "红方",
}
const TEAM_COLORS := {
	0: Color(0.28, 0.52, 0.95),
	1: Color(0.9, 0.3, 0.28),
}
const TIPS := [
	"提示：村民伐木、采果、打猎、采金、采石各有对应的资源点。",
	"提示：在资源点旁建造伐木场、磨坊或采矿营可以缩短采集往返。",
	"提示：升入封建时代后即可建造兵营、马厩与弓场。",
	"提示：市场可以买卖资源，也能向盟友进贡。",
	"提示：把城门上锁可阻挡敌军，解锁后己方部队可通过。",
	"提示：科技能长期提升部队与经济的效率，别忘记研发。",
]

var _bridge: GameBridge
var _world: WorldView


func _ready() -> void:
	_populate_info()
	_run_loading.call_deferred()


func _populate_info() -> void:
	var config: Dictionary = MatchSetup.config
	%MapNameLabel.text = "地图：%s" % config.get("map_name", "阿拉伯")
	%TipLabel.text = TIPS[randi() % TIPS.size()]
	%ErrorLabel.hide()
	%BackButton.hide()
	for child in %PlayersList.get_children():
		child.queue_free()
	var civilization := _civilization_name()
	for team in int(config.get("player_count", 2)):
		var row := HBoxContainer.new()
		var swatch := ColorRect.new()
		swatch.custom_minimum_size = Vector2(18, 18)
		swatch.color = TEAM_COLORS.get(team, Color(0.7, 0.7, 0.7))
		var label := Label.new()
		label.text = "%s　文明：%s" % [TEAM_NAMES.get(team, "玩家 %d" % team), civilization]
		row.add_child(swatch)
		row.add_child(label)
		%PlayersList.add_child(row)


func _civilization_name() -> String:
	var probe := GameBridge.new()
	var name := probe.CivilizationName(0)
	probe.free()
	return name


func _run_loading() -> void:
	await get_tree().process_frame
	_set_progress(0.08, "生成地图…")
	await get_tree().process_frame

	var config: Dictionary = MatchSetup.config
	_bridge = GameBridge.new()
	add_child(_bridge)
	var map_path := String(config.get("map_path", ""))
	if map_path.is_empty():
		_bridge.StartGeneratedMatch(
			int(config.get("seed", MatchSetup.DEFAULT_SEED)),
			int(config.get("player_count", MatchSetup.DEFAULT_PLAYER_COUNT)),
			int(config.get("map_type", MatchSetup.ARABIA_MAP_TYPE)),
			int(config.get("ai_difficulty", 1)),
			PackedInt32Array())
	else:
		_bridge.StartFromMap(map_path, int(config.get("ai_difficulty", 1)), PackedInt32Array())

	if not _bridge.HasSession or _bridge.TerrainWidth <= 0 or _bridge.TerrainHeight <= 0:
		_fail("生成地图失败：%s" % _map_error_text())
		return

	_set_progress(0.45, "生成地形…")
	await get_tree().process_frame

	_world = WORLD_VIEW.new() as WorldView
	add_child(_world)
	await _world.build_terrain_staged(_bridge, _on_terrain_progress)

	if not _world.is_terrain_ready():
		_fail("生成地形失败：地形数据不完整。")
		return

	_set_progress(0.94, "准备战场…")
	await get_tree().process_frame

	remove_child(_bridge)
	remove_child(_world)
	MatchSetup.set_prepared(_bridge, _world)
	get_tree().root.add_child(_bridge)
	get_tree().root.add_child(_world)
	get_tree().change_scene_to_file(MATCH_SCENE)


func _on_terrain_progress(value: float) -> void:
	_set_progress(lerpf(0.45, 0.9, clampf(value, 0.0, 1.0)), "生成地形…")


func _map_error_text() -> String:
	var error := String(_bridge.LastMapError)
	return error if not error.is_empty() else "未能建立对局会话。"


func _set_progress(value: float, text: String) -> void:
	%ProgressBar.value = value * 100.0
	%ProgressLabel.text = "%s　%d%%" % [text, int(round(value * 100.0))]


func _fail(message: String) -> void:
	%ProgressLabel.text = "加载失败"
	%ErrorLabel.text = message
	%ErrorLabel.show()
	%BackButton.show()
	MatchSetup.clear()


func _on_back_pressed() -> void:
	get_tree().change_scene_to_file(MAIN_MENU_SCENE)
