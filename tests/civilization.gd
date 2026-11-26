extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.CivilizationSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# civilization self test")
	bridge.free()
