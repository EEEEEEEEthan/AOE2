extends Control

## 结算界面的分数曲线：按服务器采样时间线绘制各方总分折线，纯自绘，不改模拟状态。

const AXIS_COLOR := Color(0.55, 0.6, 0.68, 0.8)
const GRID_COLOR := Color(0.35, 0.4, 0.48, 0.35)
const TEXT_COLOR := Color(0.85, 0.88, 0.92, 1.0)
const MARGIN := 10.0
const AXIS_LEFT := 46.0
const AXIS_BOTTOM := 22.0

var _samples: Array = []
var _team_count := 2
var _colors: Array = []
var _end_tick := 0


func set_series(samples: Array, team_count: int, colors: Array, end_tick: int) -> void:
	_samples = samples
	_team_count = team_count
	_colors = colors
	_end_tick = end_tick
	queue_redraw()


func _draw() -> void:
	var font := get_theme_default_font()
	var font_size := 12
	var rect := Rect2(Vector2(AXIS_LEFT, MARGIN), size - Vector2(AXIS_LEFT + MARGIN, AXIS_BOTTOM + MARGIN))
	draw_rect(rect, Color(0.08, 0.1, 0.14, 0.9), true)
	draw_rect(rect, AXIS_COLOR, false, 1.0)
	for step in range(1, 4):
		var y := rect.position.y + rect.size.y * step / 4.0
		draw_line(Vector2(rect.position.x, y), Vector2(rect.end.x, y), GRID_COLOR, 1.0)
	if _samples.is_empty():
		draw_string(font, rect.position + Vector2(12, 24), "暂无分数样本",
			HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, TEXT_COLOR)
		return
	var max_score := 1
	var last_tick := 1
	for sample in _samples:
		last_tick = maxi(last_tick, int(sample["tick"]))
		for score in sample["scores"]:
			max_score = maxi(max_score, int(score))
	last_tick = maxi(last_tick, _end_tick)
	var count: int = _samples.size()
	# 每个采样点换算到绘图区；单点时落到右端，避免折线不可见。
	for team in mini(_team_count, _colors.size()):
		var points := PackedVector2Array()
		for index in count:
			var sample: Dictionary = _samples[index]
			var scores: Array = sample["scores"]
			if team >= scores.size():
				continue
			var ratio: float = float(sample["tick"]) / float(last_tick) if last_tick > 0 else 0.0
			var value_ratio: float = float(scores[team]) / float(max_score)
			points.append(Vector2(
				rect.position.x + rect.size.x * ratio,
				rect.end.y - rect.size.y * value_ratio))
		if points.size() >= 2:
			draw_polyline(points, _colors[team], 2.0, true)
		elif points.size() == 1:
			draw_circle(points[0], 3.0, _colors[team])
	# 纵轴满分与横轴起止刻度。
	draw_string(font, Vector2(MARGIN, rect.position.y + 4), str(max_score),
		HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, TEXT_COLOR)
	draw_string(font, Vector2(rect.position.x, rect.end.y + 16), "0",
		HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, TEXT_COLOR)
	draw_string(font, Vector2(rect.end.x - 44, rect.end.y + 16), "%d tick" % last_tick,
		HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, TEXT_COLOR)
