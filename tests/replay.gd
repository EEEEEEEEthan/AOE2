extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.ReplaySelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# replay record/replay self test")
	bridge.free()
