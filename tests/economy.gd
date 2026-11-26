extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.EconomySelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# economy self test")
	bridge.free()
