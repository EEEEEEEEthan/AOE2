class_name VisionSystem
extends RefCounted

static func step(world: SimWorld, grids: Array) -> void:
	for vision_grid: VisionGrid in grids:
		vision_grid.clear_visible()
	var sight_store := world.store(SimComponents.SIGHT)
	if sight_store.count == 0:
		return
	for id in world.entities():
		if not sight_store.has(id):
			continue
		if not world.has_component(id, SimComponents.TEAM):
			continue
		if not world.has_component(id, SimComponents.POSITION):
			continue
		var team := world.get_value(id, SimComponents.TEAM, SimComponents.TEAM_ID)
		if team < 0 or team >= grids.size():
			continue
		var radius := world.get_value(id, SimComponents.SIGHT, SimComponents.RADIUS)
		var x := world.get_value(id, SimComponents.POSITION, SimComponents.X)
		var y := world.get_value(id, SimComponents.POSITION, SimComponents.Y)
		var target_grid: VisionGrid = grids[team]
		target_grid.reveal_circle(x, y, radius)
