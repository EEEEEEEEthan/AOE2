class_name PlayerColors
extends RefCounted
## 玩家配色的唯一真源：世界渲染与 HUD 文案都从这里取色与取名。
## 默认按原作映射 P1 蓝、P2 红，并预留到 8 色；单局可按玩家覆盖自定义颜色与名称。

const MAX_PLAYERS := 8
const NEUTRAL_COLOR := Color(0.7, 0.7, 0.7)

const DEFAULT_TEAMS := [
	{"name": "蓝方", "color": Color(0.2, 0.42, 0.85)},
	{"name": "红方", "color": Color(0.82, 0.22, 0.2)},
	{"name": "绿方", "color": Color(0.22, 0.62, 0.28)},
	{"name": "黄方", "color": Color(0.9, 0.78, 0.18)},
	{"name": "青方", "color": Color(0.18, 0.72, 0.74)},
	{"name": "紫方", "color": Color(0.58, 0.32, 0.78)},
	{"name": "橙方", "color": Color(0.92, 0.52, 0.16)},
	{"name": "灰方", "color": Color(0.62, 0.64, 0.66)},
]

static var _custom_colors: Dictionary = {}
static var _custom_names: Dictionary = {}


static func color_of(team: int) -> Color:
	if team < 0:
		return NEUTRAL_COLOR
	if _custom_colors.has(team):
		return _custom_colors[team]
	return DEFAULT_TEAMS[team % MAX_PLAYERS]["color"]


static func name_of(team: int) -> String:
	if _custom_names.has(team):
		return _custom_names[team]
	if team < 0 or team >= MAX_PLAYERS:
		return "玩家 %d" % team
	return DEFAULT_TEAMS[team]["name"]


static func set_color(team: int, color: Color) -> void:
	_custom_colors[team] = color


static func set_name(team: int, display_name: String) -> void:
	_custom_names[team] = display_name
