extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.MatchSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# match flow self test")
	bridge.free()
