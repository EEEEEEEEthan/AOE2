extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.MarketSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# market self test")
	bridge.free()
