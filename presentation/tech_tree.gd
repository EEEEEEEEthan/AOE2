class_name TechTree
extends CanvasLayer

signal closed

@export var card_scene: PackedScene

var _bridge: GameBridge
var _civ := 0


func _ready() -> void:
	hide()


func open(bridge: GameBridge) -> void:
	_bridge = bridge
	_fill_civs()
	_rebuild()
	show()


func _fill_civs() -> void:
	%CivSelect.clear()
	for civ in range(_bridge.CivilizationCount):
		%CivSelect.add_item(_bridge.CivilizationName(civ), civ)
	if %CivSelect.item_count > 0:
		%CivSelect.select(0)
		_civ = %CivSelect.get_item_id(0)


func _on_civ_selected(index: int) -> void:
	_civ = %CivSelect.get_item_id(index)
	_rebuild()


func _on_close_pressed() -> void:
	hide()
	closed.emit()


func _rebuild() -> void:
	for child in %Body.get_children():
		%Body.remove_child(child)
		child.queue_free()
	if _bridge == null:
		return
	var age_column: VBoxContainer
	var building_box: VBoxContainer
	var age := -1
	var building := -1
	for entry: Dictionary in _bridge.TechTree(_civ):
		if entry["required_age"] != age:
			age = entry["required_age"]
			building = -1
			age_column = VBoxContainer.new()
			age_column.size_flags_vertical = Control.SIZE_EXPAND_FILL
			var age_label := Label.new()
			age_label.text = str(entry["required_age_name"])
			age_column.add_child(age_label)
			%Body.add_child(age_column)
		if entry["required_building"] != building:
			building = entry["required_building"]
			building_box = VBoxContainer.new()
			var building_label := Label.new()
			building_label.text = "建筑：%s" % entry["required_building_name"]
			building_box.add_child(building_label)
			age_column.add_child(building_box)
		var card := card_scene.instantiate() as TechCard
		card.setup(entry)
		building_box.add_child(card)
