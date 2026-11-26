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
var _network: Node = null
var _pending_save_path := ""


func reset_config() -> void:
	_pending_save_path = ""
	config = {
		"seed": DEFAULT_SEED,
		"player_count": DEFAULT_PLAYER_COUNT,
		"map_type": ARABIA_MAP_TYPE,
		"map_name": ARABIA_MAP_NAME,
		"map_path": "",
		"ai_difficulty": 1,
	}


## 读档入口：记录待加载的对局存档路径，由对局场景在启动时消费。
func set_pending_save(path: String) -> void:
	_pending_save_path = path


func has_pending_save() -> bool:
	return not _pending_save_path.is_empty()


func take_pending_save() -> String:
	var path := _pending_save_path
	_pending_save_path = ""
	return path


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


## 大厅把已连上的多人网络节点交给对局场景持有，避免切场景时断线。
func set_network(network: Node) -> void:
	_network = network


func has_network() -> bool:
	return _network != null


func take_network() -> Node:
	var network := _network
	_network = null
	_detach(network)
	return network


func clear_network() -> void:
	_network = null
