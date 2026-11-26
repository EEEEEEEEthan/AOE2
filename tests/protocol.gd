extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	check_true(bridge.ProtocolSelfCheck(), "snapshot and command frames round-trip losslessly")
	bridge.free()
