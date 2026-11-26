class_name UnitOverlay
extends Control

const KIND_BUILDING := 8
const HEALTH_BAR_WIDTH := 30.0
const HEALTH_BAR_HEIGHT := 4.0
const HEALTH_BAR_RAISE := 0.9
const GROUP_BADGE_RADIUS := 8.0
const GROUP_BADGE_RAISE := 12.0
const FLAG_SECONDS := 1.2
const FLAG_POLE_HEIGHT := 18.0
const FLAG_WIDTH := 11.0
const FLAG_HEIGHT := 7.0

var _entities: Array = []
var _selected: Dictionary = {}
var _groups: Dictionary = {}
var _flags: Array = []
var _camera: Camera3D
var _world_view: WorldView


func configure(camera: Camera3D, world_view: WorldView) -> void:
	_camera = camera
	_world_view = world_view


func update(entities: Array, selected_ids: Array, groups: Dictionary) -> void:
	_entities = entities
	_selected.clear()
	for entity_id in selected_ids:
		_selected[int(entity_id)] = true
	_groups = groups
	queue_redraw()


func show_flag(world_x: float, world_y: float, color: Color) -> void:
	_flags.append({
		"x": world_x,
		"y": world_y,
		"color": color,
		"until": Time.get_ticks_msec() + int(FLAG_SECONDS * 1000.0),
	})
	queue_redraw()


func _process(_delta: float) -> void:
	_expire_flags()
	queue_redraw()


func _expire_flags() -> void:
	if _flags.is_empty():
		return
	var now := Time.get_ticks_msec()
	var alive: Array = []
	for flag in _flags:
		if int(flag["until"]) > now:
			alive.append(flag)
	_flags = alive


func _draw() -> void:
	if _camera == null:
		return
	var bounds := Rect2(Vector2.ZERO, size)
	var font := get_theme_default_font()
	var font_size := get_theme_default_font_size()
	for entry in _entities:
		var entity_id := int(entry["id"])
		var screen := _entity_screen_position(entry)
		if not bounds.has_point(screen):
			continue
		if _selected.has(entity_id) or int(entry["hp"]) < int(entry["max_hp"]):
			_draw_health_bar(screen, entry)
		if _groups.has(entity_id):
			_draw_group_badge(screen, int(_groups[entity_id]), font, font_size)
	for flag in _flags:
		_draw_flag(flag)


func _entity_screen_position(entry: Dictionary) -> Vector2:
	var center_x := float(entry["x"])
	var center_y := float(entry["y"])
	if int(entry["kind"]) == KIND_BUILDING:
		center_x = center_x - 0.5 + float(entry["width"]) * 0.5
		center_y = center_y - 0.5 + float(entry["height"]) * 0.5
	var ground := _world_view.ground_level_at(center_x, center_y)
	return _camera.unproject_position(Vector3(center_x, ground + HEALTH_BAR_RAISE, center_y))


func _draw_health_bar(screen: Vector2, entry: Dictionary) -> void:
	var ratio := clampf(float(entry["hp"]) / float(maxi(1, int(entry["max_hp"]))), 0.0, 1.0)
	var origin := screen + Vector2(-HEALTH_BAR_WIDTH * 0.5, 0.0)
	draw_rect(Rect2(origin, Vector2(HEALTH_BAR_WIDTH, HEALTH_BAR_HEIGHT)), Color(0.02, 0.02, 0.02, 0.8))
	var fill_color := Color(0.35, 0.85, 0.3)
	if ratio <= 0.25:
		fill_color = Color(0.9, 0.28, 0.22)
	elif ratio <= 0.5:
		fill_color = Color(0.92, 0.76, 0.22)
	draw_rect(Rect2(origin, Vector2(HEALTH_BAR_WIDTH * ratio, HEALTH_BAR_HEIGHT)), fill_color)


func _draw_group_badge(screen: Vector2, group: int, font: Font, font_size: int) -> void:
	var center := screen + Vector2(0.0, -GROUP_BADGE_RAISE)
	draw_circle(center, GROUP_BADGE_RADIUS, Color(0.05, 0.09, 0.18, 0.85))
	draw_arc(center, GROUP_BADGE_RADIUS, 0.0, TAU, 24, Color(0.88, 0.9, 0.95), 1.0)
	var text := str(group)
	var text_size := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size)
	var baseline := center + Vector2(-text_size.x * 0.5, text_size.y * 0.35)
	draw_string(font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color.WHITE)


func _draw_flag(flag: Dictionary) -> void:
	var world_x := float(flag["x"])
	var world_y := float(flag["y"])
	var ground := _world_view.ground_level_at(world_x, world_y)
	var base := _camera.unproject_position(Vector3(world_x, ground, world_y))
	var top := base - Vector2(0.0, FLAG_POLE_HEIGHT)
	var color: Color = flag["color"]
	draw_line(base, top, Color(0.94, 0.94, 0.9), 2.0)
	draw_colored_polygon(
		PackedVector2Array([
			top,
			top + Vector2(FLAG_WIDTH, FLAG_HEIGHT * 0.5),
			top + Vector2(0.0, FLAG_HEIGHT),
		]),
		color)
