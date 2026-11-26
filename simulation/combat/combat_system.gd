class_name CombatSystem
extends RefCounted

static func step(world: SimWorld, spatial: SpatialHash) -> int:
	spatial.rebuild(world)
	var combat_store := world.store(SimComponents.COMBAT)
	if combat_store.count == 0:
		return 0
	var dead := PackedInt64Array()
	for id in world.entities():
		if not combat_store.has(id):
			continue
		if not world.is_alive(id):
			continue
		var cooldown_left := world.get_value(id, SimComponents.COMBAT, SimComponents.COOLDOWN_LEFT)
		if cooldown_left > 0:
			world.set_value(id, SimComponents.COMBAT, SimComponents.COOLDOWN_LEFT, cooldown_left - 1)
			continue
		var target := _nearest_enemy(world, spatial, id)
		if target < 0:
			continue
		_apply_damage(world, id, target, dead)
	if dead.size() > 0:
		for id in dead:
			world.despawn(id)
	return dead.size()

static func _nearest_enemy(world: SimWorld, spatial: SpatialHash, attacker: int) -> int:
	var team := world.get_value(attacker, SimComponents.TEAM, SimComponents.TEAM_ID)
	var range_sq := world.get_value(attacker, SimComponents.COMBAT, SimComponents.RANGE_SQ)
	var x := world.get_value(attacker, SimComponents.POSITION, SimComponents.X)
	var y := world.get_value(attacker, SimComponents.POSITION, SimComponents.Y)
	var range_fixed := Fixed.sqrt(range_sq)
	var candidates := spatial.query_radius(x, y, range_fixed)
	var best := -1
	var best_distance := 0
	for candidate in candidates:
		if candidate == attacker:
			continue
		if not world.is_alive(candidate):
			continue
		if not world.has_component(candidate, SimComponents.HEALTH):
			continue
		if world.get_value(candidate, SimComponents.TEAM, SimComponents.TEAM_ID) == team:
			continue
		var cx := world.get_value(candidate, SimComponents.POSITION, SimComponents.X)
		var cy := world.get_value(candidate, SimComponents.POSITION, SimComponents.Y)
		var dx := Fixed.sub(cx, x)
		var dy := Fixed.sub(cy, y)
		var distance := Fixed.add(Fixed.mul(dx, dx), Fixed.mul(dy, dy))
		if distance > range_sq:
			continue
		if best < 0 or distance < best_distance or (distance == best_distance and candidate < best):
			best = candidate
			best_distance = distance
	return best

static func _apply_damage(world: SimWorld, attacker: int, target: int, dead: PackedInt64Array) -> void:
	var damage := world.get_value(attacker, SimComponents.COMBAT, SimComponents.DAMAGE)
	var cooldown := world.get_value(attacker, SimComponents.COMBAT, SimComponents.COOLDOWN)
	world.set_value(attacker, SimComponents.COMBAT, SimComponents.COOLDOWN_LEFT, max(1, cooldown))
	var hp := world.get_value(target, SimComponents.HEALTH, SimComponents.HP) - damage
	world.set_value(target, SimComponents.HEALTH, SimComponents.HP, hp)
	if hp <= 0 and not dead.has(target):
		dead.append(target)
