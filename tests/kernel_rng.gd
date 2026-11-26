extends TestBase

func run() -> void:
	var first := SimRng.new(12345)
	var second := SimRng.new(12345)
	var same := true
	for i in 64:
		if first.next_u32() != second.next_u32():
			same = false
	check_true(same, "same seed produces identical sequence")
	var third := SimRng.new(999)
	check_ne(SimRng.new(12345).next_u32(), third.next_u32(), "different seed diverges")
	var rng := SimRng.new(7)
	var in_range := true
	for i in 500:
		var value := rng.next_int_range(3, 9)
		if value < 3 or value > 9:
			in_range = false
	check_true(in_range, "integer range respected")
	var source := SimRng.new(42)
	for i in 10:
		source.next_u32()
	var clone := source.clone()
	var clone_matches := true
	for i in 20:
		if source.next_u32() != clone.next_u32():
			clone_matches = false
	check_true(clone_matches, "clone continues identical stream")
	var fixed_rng := SimRng.new(99)
	var fixed_in_bounds := true
	for i in 200:
		var value := fixed_rng.next_fixed()
		if value < 0 or value >= Fixed.SCALE:
			fixed_in_bounds = false
	check_true(fixed_in_bounds, "fixed values stay in fractional range")
