extends TestBase

# 黄金校验和：由 C# 内核在固定场景下的 world.checksum() 固化而来。
const SCENARIO_GOLDENS := [
	2819544654,
	1918973514,
	897794991,
	1273341379,
	3457656598,
	1746423188,
	4001299090,
	863920808,
]
const RNG_DIGEST_12345 := 1825340387
const RNG_DIGEST_999 := 813643442
const DEMO_300 := 3805832939


func run() -> void:
	var bridge := SimulationBridge.new()
	for index in SCENARIO_GOLDENS.size():
		var checksum := bridge.ChecksumScenario(index)
		check_eq(checksum, SCENARIO_GOLDENS[index], "scenario %d golden checksum" % index)
		check_eq(bridge.ChecksumScenario(index), checksum, "scenario %d reproducible" % index)
	check_eq(bridge.RngDigest(12345, 64), RNG_DIGEST_12345, "rng digest seed 12345 golden")
	check_eq(bridge.RngDigest(999, 200), RNG_DIGEST_999, "rng digest seed 999 golden")
	check_eq(bridge.DemoChecksum(300), DEMO_300, "demo scenario golden checksum")
	bridge.free()
