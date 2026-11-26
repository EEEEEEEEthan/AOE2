extends Node
## 用一张受控高度场截图对比：左侧缓坡顶面连续、右侧陡坡为近垂直悬崖、中间水池低于陆地。
## 供 AOE-77 验收示意；不参与任何测试档（tests/tools/ 永不被扫描）。

const MESHER := preload("res://presentation/voxel_mesher.gd")
# 示意用：纵向放大 4 倍，便于在截图中看清缓坡与悬崖剖面。
const LAYER := 4.0 / 16.0
const WIDTH := 10
const HEIGHT := 8


func _ready() -> void:
	_capture()


func _capture() -> void:
	var waited := 0
	while get_tree().current_scene == null and waited < 240:
		await get_tree().process_frame
		waited += 1
	var viewport := SubViewport.new()
	viewport.size = Vector2i(1152, 648)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(viewport)
	viewport.add_child(build_scene())
	for frame in 30:
		await get_tree().process_frame
	await RenderingServer.frame_post_draw
	var image := viewport.get_texture().get_image()
	image.save_png(_run_directory().path_join("terrain_slope_preview.png"))
	print("SCREENSHOT terrain_slope_preview.png %dx%d" % [image.get_width(), image.get_height()])
	get_tree().quit(0)


func build_scene() -> Node3D:
	var root := Node3D.new()
	root.name = "SlopePreview"
	var mesh_instance := MeshInstance3D.new()
	var material := StandardMaterial3D.new()
	material.vertex_color_use_as_albedo = true
	material.roughness = 0.95
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	mesh_instance.material_override = material
	mesh_instance.mesh = _build_mesh()
	root.add_child(mesh_instance)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-55.0, -35.0, 0.0)
	light.light_energy = 1.1
	root.add_child(light)
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color(0.55, 0.70, 0.86)
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color.WHITE
	environment.ambient_light_energy = 0.55
	var world_environment := WorldEnvironment.new()
	world_environment.environment = environment
	root.add_child(world_environment)
	var camera := Camera3D.new()
	camera.name = "Camera3D"
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 16.0
	camera.position = Vector3(17.0, 11.0, 17.0)
	camera.look_at_from_position(camera.position, Vector3(5.0, 2.0, 4.0), Vector3.UP)
	root.add_child(camera)
	return root


func _build_mesh() -> ArrayMesh:
	var stride := WIDTH + 1
	var vertex_heights := PackedInt32Array()
	vertex_heights.resize(stride * (HEIGHT + 1))
	for vy in HEIGHT + 1:
		for vx in WIDTH + 1:
			vertex_heights[vy * stride + vx] = _vertex_height(vx)
	var cells := WIDTH * HEIGHT
	var colors := PackedColorArray()
	var side_colors := PackedColorArray()
	var drops := PackedInt32Array()
	colors.resize(cells)
	side_colors.resize(cells)
	drops.resize(cells)
	for cy in HEIGHT:
		for cx in WIDTH:
			var index := cy * WIDTH + cx
			var water := cx >= 2 and cx <= 3 and cy >= 5 and cy <= 6
			if water:
				colors[index] = Color(0.22, 0.45, 0.85)
				side_colors[index] = Color(0.16, 0.34, 0.66)
				drops[index] = 2
			else:
				var base := cy * stride + cx
				var average := (vertex_heights[base] + vertex_heights[base + 1]
					+ vertex_heights[base + stride] + vertex_heights[base + stride + 1]) / 64.0
				colors[index] = Color(0.30, 0.55, 0.28).lerp(Color(0.86, 0.78, 0.46), average)
				side_colors[index] = Color(0.44, 0.34, 0.22)
				drops[index] = 0
	return MESHER.heightfield_mesh(
		vertex_heights, WIDTH, HEIGHT, LAYER, colors, side_colors, drops)


func _vertex_height(vx: int) -> int:
	var height := 4.0
	if vx <= 1:
		height = 4.0
	elif vx <= 4:
		height = 4.0 + float(vx - 1) * (4.0 / 3.0)
	else:
		height = 8.0
	if vx >= 7:
		height = 16.0
	return int(round(height))


func _run_directory() -> String:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--run-directory="):
			return argument.trim_prefix("--run-directory=")
	return OS.get_user_data_dir()
