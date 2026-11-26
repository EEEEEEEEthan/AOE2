class_name BuildingTypes
extends RefCounted
const UNAVAILABLE := 0
const TOWN_CENTER := 1
const HOUSE := 2
const BARRACKS := 3
const WALL := 4
const TOWER := 5
const MARKET := 6
const DEFINITIONS := {
	TOWN_CENTER: {"size_w": 4, "size_h": 4, "max_hp": 2400, "build_ticks": 240, "cost": 275},
	HOUSE: {"size_w": 2, "size_h": 2, "max_hp": 550, "build_ticks": 60, "cost": 25},
	BARRACKS: {"size_w": 3, "size_h": 3, "max_hp": 1200, "build_ticks": 150, "cost": 175},
	WALL: {"size_w": 1, "size_h": 1, "max_hp": 500, "build_ticks": 24, "cost": 5},
	TOWER: {"size_w": 2, "size_h": 2, "max_hp": 1000, "build_ticks": 120, "cost": 125},
	MARKET: {"size_w": 4, "size_h": 3, "max_hp": 1500, "build_ticks": 180, "cost": 175},
}
static func is_known(type: int) -> bool:
	return DEFINITIONS.has(type)
static func size_width(type: int) -> int:
	return _value(type, "size_w")
static func size_height(type: int) -> int:
	return _value(type, "size_h")
static func max_hp(type: int) -> int:
	return _value(type, "max_hp")
static func build_ticks(type: int) -> int:
	return _value(type, "build_ticks")
static func cost(type: int) -> int:
	return _value(type, "cost")
static func _value(type: int, key: String) -> int:
	var definition: Dictionary = DEFINITIONS.get(type, {})
	return int(definition.get(key, 0))
