extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.WaterSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# water gameplay self test")
	bridge.free()
