extends TestBase

const LOADING_SCENE := "res://presentation/loading.tscn"
const WORLD_VIEW := preload("res://presentation/world_view.gd")


func run() -> void:
	check(get_node_or_null("/root/MatchSetup") != null, "match setup autoload is registered")

	var loading_scene := load(LOADING_SCENE) as PackedScene
	check(loading_scene != null, "loading scene loads")
	if loading_scene == null:
		return
	var loading := loading_scene.instantiate()
	check(loading is Control, "loading root is a control")
	check(loading.get_node("%MapNameLabel") is Label, "loading shows the map name")
	check(loading.get_node("%PlayersList") is VBoxContainer, "loading lists players")
	check(loading.get_node("%ProgressBar") is ProgressBar, "loading has a progress bar")
	check(loading.get_node("%ProgressLabel") is Label, "loading shows progress text")
	check(loading.get_node("%TipLabel") is Label, "loading shows a random tip")
	check(loading.get_node("%ErrorLabel") is Label, "loading has an error slot")
	loading.free()

	var world := WORLD_VIEW.new()
	check(world.has_method("build_terrain_staged"), "world view can build terrain across frames")
	check(world.has_method("adopt"), "world view can adopt prebuilt terrain")
	world.free()
