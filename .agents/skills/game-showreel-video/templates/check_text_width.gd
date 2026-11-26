extends Node
## 示例项目里这里继承的是 TestScriptBase（测试入口）；复制后改成项目自己的测试/场景基类。
## 字幕体检：用录制时同一个字体（项目主题的 `default_font`）量每条字幕的像素宽度，
## 超过底条可用宽度（1280 − 2×56 = 1168）的会打 WIDE；同时逐字问字体链有没有字形，
## 缺一个字就打 MISSING 并以退出码 1 结束——录制前先过这一关，不许带着方框开录。

const CAPTION_FONT_SIZE := 30
const AVAILABLE_WIDTH := 1168.0
## 改成自己的 <WORK_DIR>/timeline.json。
const TIMELINE_PATH := "res://.export/showreel/timeline.json"
## 字幕字体只从项目主题的 `default_font` 取（手刻字体 + 它挂的中文子集 fallback）；
## 复制到目标项目后这里改成那个项目的主题路径。
const SUBTITLE_FONT_PATH := "res://ui/ui_theme.tres"


func _ready() -> void:
	var font := _subtitle_font()
	if font == null:
		push_error("字幕字体要取自 %s 的 default_font" % SUBTITLE_FONT_PATH)
		get_tree().quit(1)
		return
	var parsed: Variant = JSON.parse_string(
			FileAccess.get_file_as_string(TIMELINE_PATH)
		)
	if not (parsed is Dictionary):
		push_error("timeline.json 要能解析")
		get_tree().quit(1)
		return
	var widest := 0.0
	var widest_id := ""
	var over_count := 0
	var character_count := 0
	var missing_count := 0
	## 按整个字幕语料的去重字符集体检：同一个字在几条字幕里出现只算一次、只报一次。
	var seen := {}
	for chapter: Dictionary in parsed["chapters"]:
		for shot: Dictionary in chapter["shots"]:
			var caption: String = shot["caption"]
			var width := font.get_string_size(
					caption, HORIZONTAL_ALIGNMENT_LEFT, -1, CAPTION_FONT_SIZE,
				).x
			if width > widest:
				widest = width
				widest_id = shot["id"]
			if width > AVAILABLE_WIDTH:
				over_count += 1
				print("WIDE %6.0f %s %s" % [width, shot["id"], caption])
			for character in caption:
				var code_point := character.unicode_at(0)
				if seen.has(code_point):
					continue
				seen[code_point] = true
				character_count += 1
				if not font.has_char(code_point):
					missing_count += 1
					print(
							"MISSING %s U+%04X %s/%s"
							% [character, code_point, chapter["id"], shot["id"]]
						)
	print("WIDEST %.0f %s  over_limit=%d" % [widest, widest_id, over_count])
	print("GLYPH_CHECK chars=%d missing=%d over_limit=%d" % [
			character_count, missing_count, over_count,
		])
	get_tree().quit(0 if missing_count == 0 else 1)


## 字幕字体：一律取项目主题的默认字体；取不到返回 null 由调用点判红。
func _subtitle_font() -> Font:
	var theme_resource: Theme = load(SUBTITLE_FONT_PATH) as Theme
	if theme_resource == null:
		return null
	return theme_resource.default_font
