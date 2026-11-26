extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	check_true(bridge.ServerClientSelfCheck(), "local server drives client via decoded snapshots")
	bridge.free()
