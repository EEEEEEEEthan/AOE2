class_name TechCard
extends PanelContainer

const AVAILABLE_MODULATE := Color(1.0, 1.0, 1.0, 1.0)
const DISABLED_MODULATE := Color(0.55, 0.55, 0.55, 1.0)


func setup(entry: Dictionary) -> void:
	var available: bool = entry["available"]
	%Name.text = str(entry["name"])
	%Cost.text = "食物 %d　黄金 %d" % [entry["food"], entry["gold"]]
	%Prereq.text = "前置：%s（%s）" % [entry["required_building_name"], entry["required_age_name"]]
	%Time.text = "研发：%d tick" % entry["research_ticks"]
	%State.text = "可研究" if available else "本文明禁用"
	modulate = AVAILABLE_MODULATE if available else DISABLED_MODULATE
	tooltip_text = "%s\n%s\n造价：食物 %d　黄金 %d\n前置：%s（%s）\n研发：%d tick\n%s" % [
		entry["name"],
		"可研究" if available else "本文明禁用",
		entry["food"],
		entry["gold"],
		entry["required_building_name"],
		entry["required_age_name"],
		entry["research_ticks"],
		entry["description"],
	]
