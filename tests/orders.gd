extends TestBase

# 指令集语义：攻击移动 / 巡逻 / 守卫 / 跟随 / 原地待命 / 集结点 / 指令队列 / 确定性。


func run() -> void:
	var bridge := SimulationBridge.new()
	var failures := bridge.OrderSelfTest()
	for failure in failures:
		print("FAIL: %s" % failure)
	check_eq(failures.size(), 0, "c# order state machine self test")
	bridge.free()
