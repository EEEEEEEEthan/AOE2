extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.HuntingSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# hunting self test")
	bridge.free()
