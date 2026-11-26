class_name TestBase
extends Node

var checks := 0
var failures := 0

func _ready() -> void:
	run()
	if failures == 0:
		print("PASS %d checks" % checks)
		get_tree().quit(0)
	else:
		print("FAIL %d of %d checks" % [failures, checks])
		get_tree().quit(1)

func run() -> void:
	pass

func check(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures += 1
		print("FAIL: %s" % message)

func check_eq(actual: int, expected: int, message: String) -> void:
	check(actual == expected, "%s (expected %d, got %d)" % [message, expected, actual])

func check_ne(actual: int, unexpected: int, message: String) -> void:
	check(actual != unexpected, "%s (unexpected %d)" % [message, unexpected])

func check_true(condition: bool, message: String) -> void:
	check(condition, message)
