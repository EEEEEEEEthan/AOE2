extends TestBase

const LOBBY_SCENE := "res://presentation/lobby.tscn"


func run() -> void:
	var lobby_scene := load(LOBBY_SCENE) as PackedScene
	check(lobby_scene != null, "lobby scene loads")
	var lobby := lobby_scene.instantiate()
	add_child(lobby)
	check(lobby.get_node("%HostButton") is Button, "lobby offers hosting")
	check(lobby.get_node("%JoinButton") is Button, "lobby offers joining")
	check(lobby.get_node("%AddressEdit") is LineEdit, "lobby takes a host address")
	check(lobby.get_node("%PortSpin") is SpinBox, "lobby takes a port")
	check(lobby.get_node("%SlotsList") != null, "lobby shows player slots")
	check(lobby.get_node("%ReadyCheck") is CheckButton, "lobby has a ready toggle")
	check(lobby.get_node("%StartButton") is Button, "lobby has a host start button")
	lobby.queue_free()

	var bridge := SimulationBridge.new()
	var failures := bridge.LobbySelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "lobby host/join/ready/start flow")
	bridge.free()
