class_name VoxModel
extends RefCounted
## 轻量 MagicaVoxel .vox 读取：只解析 MAIN -> SIZE / XYZI / RGBA，
## 转成 Vector3i -> Color 后交给 VoxelMesher 贪心合并。调色板索引 1 是阵营色占位。

const VOXEL_MESHER := preload("res://presentation/voxel_mesher.gd")
const TEAM_INDEX := 1


static func load_model(path: String, team_color: Color, scale: float) -> Dictionary:
	var parsed := _parse(path)
	var palette: Array = parsed["palette"]
	var voxels := {}
	var bounds := _empty_bounds()
	for entry in parsed["voxels"]:
		var key: Vector3i = entry[0]
		var color_index: int = entry[1]
		var color := team_color if color_index == TEAM_INDEX else _palette_color(palette, color_index)
		voxels[key] = color
		bounds = _grow_bounds(bounds, key)
	if voxels.is_empty():
		return {"mesh": null, "offset": Vector3.ZERO}
	var mesh: ArrayMesh = VOXEL_MESHER.greedy_mesh(voxels, Vector3(scale, scale, scale), false)
	var offset := Vector3(
		-(bounds[3] + bounds[0] + 1) * 0.5 * scale,
		-bounds[1] * scale,
		-(bounds[5] + bounds[2] + 1) * 0.5 * scale)
	return {"mesh": mesh, "offset": offset}


static func _palette_color(palette: Array, color_index: int) -> Color:
	var palette_index := color_index - 1
	if palette_index >= 0 and palette_index < palette.size():
		return palette[palette_index]
	return Color(1.0, 0.0, 1.0)


static func _parse(path: String) -> Dictionary:
	var result := {"size": Vector3i.ZERO, "voxels": [], "palette": []}
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return result
	var data := file.get_buffer(file.get_length())
	if data.size() < 8:
		return result
	_parse_chunks(data, 8, data.size(), result)
	return result


static func _parse_chunks(data: PackedByteArray, start: int, end: int, result: Dictionary) -> void:
	var offset := start
	while offset + 12 <= end:
		var chunk_id := data.slice(offset, offset + 4).get_string_from_ascii()
		var content_size := data.decode_s32(offset + 4)
		var children_size := data.decode_s32(offset + 8)
		var content_start := offset + 12
		match chunk_id:
			"MAIN":
				_parse_chunks(
					data,
					content_start + content_size,
					content_start + content_size + children_size,
					result)
			"SIZE":
				result["size"] = Vector3i(
					data.decode_s32(content_start),
					data.decode_s32(content_start + 4),
					data.decode_s32(content_start + 8))
			"XYZI":
				var count := data.decode_s32(content_start)
				var voxels: Array = result["voxels"]
				var cursor := content_start + 4
				for index in count:
					var vx := data.decode_u8(cursor)
					var vy := data.decode_u8(cursor + 1)
					var vz := data.decode_u8(cursor + 2)
					var color_index := data.decode_u8(cursor + 3)
					cursor += 4
					# MagicaVoxel 的 z 轴朝上，本项目世界 y 轴朝上。
					voxels.append([Vector3i(vx, vz, vy), color_index])
			"RGBA":
				var palette: Array = []
				for index in 256:
					var cursor := content_start + index * 4
					palette.append(Color(
						data.decode_u8(cursor) / 255.0,
						data.decode_u8(cursor + 1) / 255.0,
						data.decode_u8(cursor + 2) / 255.0,
						data.decode_u8(cursor + 3) / 255.0))
				result["palette"] = palette
		offset = content_start + content_size + children_size


static func _empty_bounds() -> Array:
	return [1000, 1000, 1000, -1000, -1000, -1000]


static func _grow_bounds(bounds: Array, key: Vector3i) -> Array:
	bounds[0] = mini(bounds[0], key.x)
	bounds[1] = mini(bounds[1], key.y)
	bounds[2] = mini(bounds[2], key.z)
	bounds[3] = maxi(bounds[3], key.x)
	bounds[4] = maxi(bounds[4], key.y)
	bounds[5] = maxi(bounds[5], key.z)
	return bounds
