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


## 与 TerrainGrid.MaxWalkableSlope 对齐：单格四角高差超过该层数（以 1/16 格计）即视为悬崖。
const MAX_WALKABLE_SLOPE := 4

## 由共享顶点高度场生成实心台地地形。
## 缓坡格顶面按四角实际高度三角化，形成与 (宽+1)×(高+1) 顶点场一致的连续表面；
## 只有单格四角高差超过 max_slope 的陡坡格才取最高角拍平，并向下邻格补近垂直侧壁（悬崖）。
## 地图边界始终有实体厚度；cell_drops 按格下沉层数（水格传 1），仅表现层，不影响模拟。
## max_slope 默认取 TerrainGrid.MaxWalkableSlope（4 层），调用方无需传参。
static func heightfield_mesh(
	vertex_heights: PackedInt32Array,
	width: int,
	height: int,
	layer: float,
	cell_colors: PackedColorArray,
	cell_side_colors: PackedColorArray,
	cell_drops: PackedInt32Array,
	max_slope: int = MAX_WALKABLE_SLOPE) -> ArrayMesh:
	var stride := width + 1
	var lowest := 0
	for cell_y in height:
		for cell_x in width:
			var cell := cell_y * width + cell_x
			var base := cell_y * stride + cell_x
			var drop := cell_drops[cell]
			var highest := maxi(
				maxi(vertex_heights[base], vertex_heights[base + 1]),
				maxi(vertex_heights[base + stride], vertex_heights[base + stride + 1]))
			var lowest_corner := mini(
				mini(vertex_heights[base], vertex_heights[base + 1]),
				mini(vertex_heights[base + stride], vertex_heights[base + stride + 1]))
			var cell_low := highest - drop
			if highest - lowest_corner <= max_slope:
				cell_low = lowest_corner - drop
			lowest = mini(lowest, cell_low)
	var bottom := (lowest - 1) * layer
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for cell_y in height:
		for cell_x in width:
			var cell := cell_y * width + cell_x
			_emit_cell(st, cell_x, cell_y, layer, vertex_heights, stride,
				width, height, cell_drops, max_slope, bottom,
				cell_colors[cell], cell_side_colors[cell])
	return st.commit()


static func _emit_cell(
	st: SurfaceTool,
	cell_x: int,
	cell_y: int,
	layer: float,
	vertex_heights: PackedInt32Array,
	stride: int,
	width: int,
	height: int,
	drops: PackedInt32Array,
	max_slope: int,
	bottom_y: float,
	top_color: Color,
	side_color: Color) -> void:
	var x0 := float(cell_x)
	var x1 := float(cell_x + 1)
	var z0 := float(cell_y)
	var z1 := float(cell_y + 1)
	var h0 := _corner_height(vertex_heights, stride, width, height, drops, max_slope, cell_x, cell_y, 0) * layer
	var h1 := _corner_height(vertex_heights, stride, width, height, drops, max_slope, cell_x, cell_y, 1) * layer
	var h2 := _corner_height(vertex_heights, stride, width, height, drops, max_slope, cell_x, cell_y, 2) * layer
	var h3 := _corner_height(vertex_heights, stride, width, height, drops, max_slope, cell_x, cell_y, 3) * layer
	_vertex(st, Vector3(x0, h0, z0), Vector3.UP, top_color)
	_vertex(st, Vector3(x0, h2, z1), Vector3.UP, top_color)
	_vertex(st, Vector3(x1, h3, z1), Vector3.UP, top_color)
	_vertex(st, Vector3(x0, h0, z0), Vector3.UP, top_color)
	_vertex(st, Vector3(x1, h3, z1), Vector3.UP, top_color)
	_vertex(st, Vector3(x1, h1, z0), Vector3.UP, top_color)
	_emit_edge(st, vertex_heights, stride, width, height, drops, max_slope, layer, bottom_y,
		cell_x - 1, cell_y, Vector3(x0, 0.0, z1), Vector3(x0, 0.0, z0),
		3, 1, Vector3.LEFT, side_color, h2, h0)
	_emit_edge(st, vertex_heights, stride, width, height, drops, max_slope, layer, bottom_y,
		cell_x + 1, cell_y, Vector3(x1, 0.0, z0), Vector3(x1, 0.0, z1),
		0, 2, Vector3.RIGHT, side_color, h1, h3)
	_emit_edge(st, vertex_heights, stride, width, height, drops, max_slope, layer, bottom_y,
		cell_x, cell_y - 1, Vector3(x0, 0.0, z0), Vector3(x1, 0.0, z0),
		2, 3, Vector3.FORWARD, side_color, h0, h1)
	_emit_edge(st, vertex_heights, stride, width, height, drops, max_slope, layer, bottom_y,
		cell_x, cell_y + 1, Vector3(x1, 0.0, z1), Vector3(x0, 0.0, z1),
		1, 0, Vector3.BACK, side_color, h3, h2)


## 一格某角的有效顶面高度（层）：陡坡格四角统一取最高角，其余取实际角高；再减去本格下沉。
static func _corner_height(
	vertex_heights: PackedInt32Array,
	stride: int,
	width: int,
	height: int,
	drops: PackedInt32Array,
	max_slope: int,
	cell_x: int,
	cell_y: int,
	corner: int) -> int:
	var base := cell_y * stride + cell_x
	var drop := drops[cell_y * width + cell_x]
	var h00 := vertex_heights[base]
	var h10 := vertex_heights[base + 1]
	var h01 := vertex_heights[base + stride]
	var h11 := vertex_heights[base + stride + 1]
	var highest := maxi(maxi(h00, h10), maxi(h01, h11))
	var lowest := mini(mini(h00, h10), mini(h01, h11))
	if highest - lowest > max_slope:
		return highest - drop
	match corner:
		0:
			return h00 - drop
		1:
			return h10 - drop
		2:
			return h01 - drop
		_:
			return h11 - drop


## 只在当前格沿某条边高于邻格时补侧壁；缓坡因共享顶点高度相同自然无缝，水面由下沉层差形成 1 层侧壁。
static func _emit_edge(
	st: SurfaceTool,
	vertex_heights: PackedInt32Array,
	stride: int,
	width: int,
	height: int,
	drops: PackedInt32Array,
	max_slope: int,
	layer: float,
	bottom_y: float,
	neighbor_x: int,
	neighbor_y: int,
	from: Vector3,
	to: Vector3,
	from_corner: int,
	to_corner: int,
	normal: Vector3,
	color: Color,
	top_from: float,
	top_to: float) -> void:
	var bottom_from := bottom_y
	var bottom_to := bottom_y
	if (neighbor_x >= 0 and neighbor_y >= 0
			and neighbor_x < width and neighbor_y < height):
		bottom_from = _corner_height(vertex_heights, stride, width, height, drops, max_slope,
			neighbor_x, neighbor_y, from_corner) * layer
		bottom_to = _corner_height(vertex_heights, stride, width, height, drops, max_slope,
			neighbor_x, neighbor_y, to_corner) * layer
	var a_bottom := minf(bottom_from, top_from)
	var b_bottom := minf(bottom_to, top_to)
	if a_bottom >= top_from and b_bottom >= top_to:
		return
	_vertex(st, Vector3(from.x, top_from, from.z), normal, color)
	_vertex(st, Vector3(to.x, top_to, to.z), normal, color)
	_vertex(st, Vector3(to.x, b_bottom, to.z), normal, color)
	_vertex(st, Vector3(from.x, top_from, from.z), normal, color)
	_vertex(st, Vector3(to.x, b_bottom, to.z), normal, color)
	_vertex(st, Vector3(from.x, a_bottom, from.z), normal, color)


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
