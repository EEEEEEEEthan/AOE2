extends Node
## 对局参数与预加载结果的跨场景中转站。
## 菜单先配置参数，加载场景据此生成对局，再把准备好的桥接与地形交给对局场景，
## 避免对局场景在 _ready 里同步重建成千上万的格子而黑屏。

const DEFAULT_SEED := 20261127
const DEFAULT_PLAYER_COUNT := 2
const ARABIA_MAP_TYPE := 0
const ARABIA_MAP_NAME := "阿拉伯"

var config: Dictionary = {
	"seed": DEFAULT_SEED,
	"player_count": DEFAULT_PLAYER_COUNT,
	"map_type": ARABIA_MAP_TYPE,
	"map_name": ARABIA_MAP_NAME,
	"map_path": "",
	"ai_difficulty": 1,
}

var _prepared_bridge: Node = null
var _prepared_world: Node = null


func reset_config() -> void:
	config = {
		"seed": DEFAULT_SEED,
		"player_count": DEFAULT_PLAYER_COUNT,
		"map_type": ARABIA_MAP_TYPE,
		"map_name": ARABIA_MAP_NAME,
		"map_path": "",
		"ai_difficulty": 1,
	}


func has_prepared() -> bool:
	return _prepared_bridge != null and _prepared_world != null


func set_prepared(bridge: Node, world: Node) -> void:
	_prepared_bridge = bridge
	_prepared_world = world


## 取出已生成的对局桥接并脱离旧父节点，交给对局场景重新挂载。
func take_bridge() -> Node:
	var bridge := _prepared_bridge
	_prepared_bridge = null
	_detach(bridge)
	return bridge


func take_world() -> Node:
	var world := _prepared_world
	_prepared_world = null
	_detach(world)
	return world


func clear() -> void:
	_prepared_bridge = null
	_prepared_world = null


func _detach(node: Node) -> void:
	if node != null and node.get_parent() != null:
		node.get_parent().remove_child(node)
