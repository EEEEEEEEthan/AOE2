class_name VoxelMesher
extends RefCounted
## 体素贪心网格：把 Vector3i -> Color 的体素集合合并成少量四边形。
## 地形可跳过下方面；单位保留全部六个方向的面（死亡倒地时能看到底面）。

const FACE_DIRS := [
	[Vector3i(1, 0, 0), 0, 1, 2],
	[Vector3i(-1, 0, 0), 0, 1, 2],
	[Vector3i(0, 1, 0), 1, 0, 2],
	[Vector3i(0, -1, 0), 1, 0, 2],
	[Vector3i(0, 0, 1), 2, 0, 1],
	[Vector3i(0, 0, -1), 2, 0, 1],
]


## 由共享顶点高度场生成实心台地地形：每格顶面取四角最高点，向低邻格与地图边界补竖直侧壁。
## 陡坡因此呈现近垂直悬崖而非缓坡；地图边界有厚度，斜视角下地形是实体。
## cell_drops 按格下沉层数（水格传 1），仅表现层，不影响模拟。
static func heightfield_mesh(
	vertex_heights: PackedInt32Array,
	width: int,
	height: int,
	layer: float,
	cell_colors: PackedColorArray,
	cell_side_colors: PackedColorArray,
	cell_drops: PackedInt32Array) -> ArrayMesh:
	var stride := width + 1
	var cells := width * height
	var tops := PackedInt32Array()
	tops.resize(cells)
	var lowest := 0
	for cell_y in height:
		for cell_x in width:
			var base := cell_y * stride + cell_x
			var top := maxi(
				maxi(vertex_heights[base], vertex_heights[base + 1]),
				maxi(vertex_heights[base + stride], vertex_heights[base + stride + 1]))
			var cell := cell_y * width + cell_x
			top -= cell_drops[cell]
			tops[cell] = top
			lowest = mini(lowest, top)
	var bottom := (lowest - 1) * layer
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for cell_y in height:
		for cell_x in width:
			var cell := cell_y * width + cell_x
			_emit_cell(st, cell_x, cell_y, tops[cell] * layer, bottom,
				cell_colors[cell], cell_side_colors[cell], tops, width, height, layer)
	return st.commit()


static func _emit_cell(
	st: SurfaceTool,
	cell_x: int,
	cell_y: int,
	top_y: float,
	bottom_y: float,
	top_color: Color,
	side_color: Color,
	tops: PackedInt32Array,
	width: int,
	height: int,
	layer: float) -> void:
	var x0 := float(cell_x)
	var x1 := float(cell_x + 1)
	var z0 := float(cell_y)
	var z1 := float(cell_y + 1)
	_vertex(st, Vector3(x0, top_y, z0), Vector3.UP, top_color)
	_vertex(st, Vector3(x0, top_y, z1), Vector3.UP, top_color)
	_vertex(st, Vector3(x1, top_y, z1), Vector3.UP, top_color)
	_vertex(st, Vector3(x0, top_y, z0), Vector3.UP, top_color)
	_vertex(st, Vector3(x1, top_y, z1), Vector3.UP, top_color)
	_vertex(st, Vector3(x1, top_y, z0), Vector3.UP, top_color)
	_emit_neighbor_wall(st, tops, width, height, layer, cell_x - 1, cell_y,
		Vector3(x0, 0.0, z1), Vector3(x0, 0.0, z0), Vector3.LEFT,
		top_y, bottom_y, side_color)
	_emit_neighbor_wall(st, tops, width, height, layer, cell_x + 1, cell_y,
		Vector3(x1, 0.0, z0), Vector3(x1, 0.0, z1), Vector3.RIGHT,
		top_y, bottom_y, side_color)
	_emit_neighbor_wall(st, tops, width, height, layer, cell_x, cell_y - 1,
		Vector3(x0, 0.0, z0), Vector3(x1, 0.0, z0), Vector3.FORWARD,
		top_y, bottom_y, side_color)
	_emit_neighbor_wall(st, tops, width, height, layer, cell_x, cell_y + 1,
		Vector3(x1, 0.0, z1), Vector3(x0, 0.0, z1), Vector3.BACK,
		top_y, bottom_y, side_color)


static func _emit_neighbor_wall(
	st: SurfaceTool,
	tops: PackedInt32Array,
	width: int,
	height: int,
	layer: float,
	neighbor_x: int,
	neighbor_y: int,
	from: Vector3,
	to: Vector3,
	normal: Vector3,
	top_y: float,
	bottom_y: float,
	color: Color) -> void:
	var face_bottom := bottom_y
	if (neighbor_x >= 0 and neighbor_y >= 0
			and neighbor_x < width and neighbor_y < height):
		face_bottom = tops[neighbor_y * width + neighbor_x] * layer
	_emit_wall(st, from, to, top_y, face_bottom, normal, color)


static func _emit_wall(
	st: SurfaceTool,
	from: Vector3,
	to: Vector3,
	top_y: float,
	bottom_y: float,
	normal: Vector3,
	color: Color) -> void:
	if bottom_y >= top_y:
		return
	var a := Vector3(from.x, top_y, from.z)
	var b := Vector3(to.x, top_y, to.z)
	var c := Vector3(to.x, bottom_y, to.z)
	var d := Vector3(from.x, bottom_y, from.z)
	_vertex(st, a, normal, color)
	_vertex(st, b, normal, color)
	_vertex(st, c, normal, color)
	_vertex(st, a, normal, color)
	_vertex(st, c, normal, color)
	_vertex(st, d, normal, color)


static func greedy_mesh(voxels: Dictionary, scale: Vector3, skip_bottom: bool) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for entry in FACE_DIRS:
		var dir: Vector3i = entry[0]
		if skip_bottom and dir == Vector3i(0, -1, 0):
			continue
		_collect_faces(st, voxels, dir, int(entry[1]), int(entry[2]), int(entry[3]), scale)
	return st.commit()


static func _collect_faces(
	st: SurfaceTool,
	voxels: Dictionary,
	dir: Vector3i,
	axis: int,
	u_axis: int,
	v_axis: int,
	scale: Vector3) -> void:
	var positive := _component(dir, axis) > 0
	var layers := {}
	for key in voxels:
		if voxels.has(key + dir):
			continue
		var color: Color = voxels[key]
		var slice := _component(key, axis) + (1 if positive else 0)
		var layer_key := "%d:%d" % [slice, color.to_rgba32()]
		var layer: Variant = layers.get(layer_key)
		if layer == null:
			layer = {"slice": slice, "color": color, "cells": {}}
			layers[layer_key] = layer
		var cells: Dictionary = layer["cells"]
		cells[Vector2i(_component(key, u_axis), _component(key, v_axis))] = true
	for layer_key in layers:
		_emit_layer(st, layers[layer_key], axis, u_axis, v_axis, scale, dir)


static func _emit_layer(
	st: SurfaceTool,
	layer: Dictionary,
	axis: int,
	u_axis: int,
	v_axis: int,
	scale: Vector3,
	dir: Vector3i) -> void:
	var cells: Dictionary = layer["cells"]
	var color: Color = layer["color"]
	var slice: int = layer["slice"]
	var coords: Array = cells.keys()
	coords.sort_custom(func(a: Vector2i, b: Vector2i) -> bool:
		return a.x * 100000 + a.y < b.x * 100000 + b.y)
	var consumed := {}
	for coord in coords:
		if consumed.has(coord):
			continue
		var u0: int = coord.x
		var v0: int = coord.y
		var du := 0
		while true:
			var next := Vector2i(u0 + du, v0)
			if not cells.has(next) or consumed.has(next):
				break
			du += 1
		var dv := 1
		while true:
			var row_full := true
			for i in du:
				var next := Vector2i(u0 + i, v0 + dv)
				if not cells.has(next) or consumed.has(next):
					row_full = false
					break
			if not row_full:
				break
			dv += 1
		for i in du:
			for j in dv:
				consumed[Vector2i(u0 + i, v0 + j)] = true
		_emit_quad(st, slice, u0, u0 + du, v0, v0 + dv, axis, u_axis, v_axis, color, scale, dir)


static func _emit_quad(
	st: SurfaceTool,
	slice: int,
	u0: int,
	u1: int,
	v0: int,
	v1: int,
	axis: int,
	u_axis: int,
	v_axis: int,
	color: Color,
	scale: Vector3,
	dir: Vector3i) -> void:
	var a := float(slice) * _axis_scale(scale, axis)
	var u_scale := _axis_scale(scale, u_axis)
	var v_scale := _axis_scale(scale, v_axis)
	var au0 := float(u0) * u_scale
	var au1 := float(u1) * u_scale
	var av0 := float(v0) * v_scale
	var av1 := float(v1) * v_scale
	var p00 := _world(axis, a, au0, av0)
	var p10 := _world(axis, a, au1, av0)
	var p11 := _world(axis, a, au1, av1)
	var p01 := _world(axis, a, au0, av1)
	var normal := Vector3(dir.x, dir.y, dir.z)
	_vertex(st, p00, normal, color)
	_vertex(st, p01, normal, color)
	_vertex(st, p11, normal, color)
	_vertex(st, p00, normal, color)
	_vertex(st, p11, normal, color)
	_vertex(st, p10, normal, color)


static func _vertex(st: SurfaceTool, point: Vector3, normal: Vector3, color: Color) -> void:
	st.set_color(color)
	st.set_normal(normal)
	st.add_vertex(point)


static func _world(axis: int, a: float, u: float, v: float) -> Vector3:
	match axis:
		0:
			return Vector3(a, u, v)
		1:
			return Vector3(u, a, v)
		_:
			return Vector3(u, v, a)


static func _axis_scale(scale: Vector3, axis: int) -> float:
	match axis:
		0:
			return scale.x
		1:
			return scale.y
		_:
			return scale.z


static func _component(vector: Vector3i, axis: int) -> int:
	match axis:
		0:
			return vector.x
		1:
			return vector.y
		_:
			return vector.z
