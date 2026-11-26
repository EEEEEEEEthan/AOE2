extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.ComputerPlayerSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# computer player self test")
	bridge.free()
