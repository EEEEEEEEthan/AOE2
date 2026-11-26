class_name Minimap
extends Control

signal jump_requested(world_position: Vector2)

const TEAM_COLORS := {
	0: Color(0.9, 0.32, 0.28),
	1: Color(0.3, 0.55, 0.95),
}
const NEUTRAL_COLOR := Color(0.65, 0.58, 0.42)
const RESOURCE_COLOR := Color(0.46, 0.36, 0.22)
const UNEXPLORED_COLOR := Color(0.02, 0.02, 0.03)
const EXPLORED_COLOR := Color(0.17, 0.2, 0.24)
const CAMERA_COLOR := Color(0.95, 0.95, 0.9, 0.9)
const BACKGROUND_COLOR := Color(0.05, 0.06, 0.08)

const KIND_UNIT := 0
const KIND_VILLAGER := 1
const KIND_RESOURCE_NODE := 2
const KIND_BUILDING := 8
const KIND_TRADE_CART := 9
const KIND_HUNTING_ANIMAL := 10
const KIND_TRANSPORT_SHIP := 11

var _camera: IsoCamera
var _ids: PackedInt64Array = PackedInt64Array()
var _positions_x: PackedFloat32Array = PackedFloat32Array()
var _positions_y: PackedFloat32Array = PackedFloat32Array()
var _teams: PackedInt32Array = PackedInt32Array()
var _kinds: PackedInt32Array = PackedInt32Array()
var _fog: PackedByteArray = PackedByteArray()
var _width := 0
var _height := 0
var _my_team := 0


func setup(camera: IsoCamera) -> void:
	_camera = camera


func update_snapshot(batch: Dictionary, fog: PackedByteArray, my_team: int) -> void:
	if batch.is_empty():
		return
	_ids = batch["ids"]
	_positions_x = batch["x"]
	_positions_y = batch["y"]
	_teams = batch["team"]
	_kinds = batch["kind"]
	_fog = fog
	_my_team = my_team
	queue_redraw()


func set_map_size(width: int, height: int) -> void:
	_width = width
	_height = height
	queue_redraw()


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), BACKGROUND_COLOR)
	if _width <= 0:
		return
	var layout := _layout()
	var cell_size: float = layout["cell_size"]
	var origin: Vector2 = layout["origin"]
	if _fog.size() >= _width * _height and _height > 0:
		for tile_y in _height:
			for tile_x in _width:
				var state := _fog[tile_y * _width + tile_x]
				var color := EXPLORED_COLOR if state == 1 else Color(0.09, 0.13, 0.16)
				if state == 0:
					color = UNEXPLORED_COLOR
				draw_rect(Rect2(origin + Vector2(tile_x, tile_y) * cell_size, Vector2(cell_size, cell_size)), color)
	for index in _ids.size():
		var kind: int = _kinds[index]
		if kind != KIND_UNIT and kind != KIND_VILLAGER and kind != KIND_BUILDING and kind != KIND_TRADE_CART and kind != KIND_HUNTING_ANIMAL and kind != KIND_TRANSPORT_SHIP:
			if kind == KIND_RESOURCE_NODE:
				draw_rect(Rect2(origin + Vector2(_positions_x[index], _positions_y[index]) * cell_size, Vector2.ONE * maxf(1.0, cell_size * 0.5)), RESOURCE_COLOR)
			continue
		var team: int = _teams[index]
		var color := NEUTRAL_COLOR
		if team >= 0:
			color = TEAM_COLORS.get(team, NEUTRAL_COLOR)
		if team == _my_team:
			color = color.lightened(0.2)
		var point := origin + Vector2(_positions_x[index], _positions_y[index]) * cell_size
		var extent := 2.0 if kind == KIND_UNIT or kind == KIND_VILLAGER or kind == KIND_TRANSPORT_SHIP else 3.5
		draw_rect(Rect2(point - Vector2.ONE * extent * 0.5, Vector2.ONE * extent), color)
	draw_rect(Rect2(origin, Vector2(_width, _height) * cell_size), Color(0.35, 0.4, 0.46, 0.8), false, 1.0)
	_draw_camera(origin, cell_size)


func _draw_camera(origin: Vector2, cell_size: float) -> void:
	if _camera == null:
		return
	var half := _camera.ortho_size * 0.5
	var viewport_size := get_viewport_rect().size
	var aspect := viewport_size.x / maxf(1.0, viewport_size.y)
	var world_size := Vector2(half * aspect * 2.0, half * 2.0)
	var focus := Vector2(_camera.focus.x, _camera.focus.z)
	var rect := Rect2(origin + (focus - world_size * 0.5) * cell_size, world_size * cell_size)
	draw_rect(rect, CAMERA_COLOR, false, 1.5)


func _layout() -> Dictionary:
	var usable := size
	var map_width := maxf(1.0, float(_width))
	var map_height := maxf(1.0, float(maxi(_height, 1)))
	var cell_size := minf(usable.x / map_width, usable.y / map_height)
	var draw_size := Vector2(map_width, map_height) * cell_size
	return {
		"cell_size": cell_size,
		"origin": (usable - draw_size) * 0.5,
	}


func _gui_input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	var mouse_button := event as InputEventMouseButton
	if not mouse_button.pressed or mouse_button.button_index != MOUSE_BUTTON_LEFT:
		return
	var layout := _layout()
	var cell_size: float = layout["cell_size"]
	var origin: Vector2 = layout["origin"]
	var world := (mouse_button.position - origin) / maxf(0.001, cell_size)
	jump_requested.emit(world)
	accept_event()
