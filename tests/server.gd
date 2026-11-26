extends TestBase


func run() -> void:
	var bridge := SimulationBridge.new()
	check_true(bridge.ServerClientSelfCheck(), "local server drives client via decoded snapshots")
	var failures := bridge.ServerTeamsSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "dedicated server assigns per-connection observer teams")
	bridge.free()
