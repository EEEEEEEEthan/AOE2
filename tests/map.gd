extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.MapSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# map generator self test")
	bridge.free()
