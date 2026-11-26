class_name SimRng
extends RefCounted

const MASK32 := 0xFFFF_FFFF

var state: int

func _init(seed_value: int = 1) -> void:
	seed_value = seed_value & MASK32
	state = seed_value if seed_value != 0 else 0x9E37_79B9

func next_u32() -> int:
	var x := state
	x = x ^ ((x << 13) & MASK32)
	x = x ^ (x >> 17)
	x = x ^ ((x << 5) & MASK32)
	state = x & MASK32
	return state

func next_int_range(low: int, high: int) -> int:
	if high <= low:
		return low
	return low + (next_u32() % (high - low + 1))

func next_bool() -> bool:
	return (next_u32() & 1) == 1

func next_fixed() -> int:
	return (next_u32() >> (32 - Fixed.FRACTION_BITS))

func next_fixed_range(low: int, high: int) -> int:
	if high <= low:
		return low
	return low + (next_u32() % (high - low + 1))

func next_float() -> float:
	return float(next_u32()) / float(MASK32)

func clone() -> SimRng:
	var other := SimRng.new(1)
	other.state = state
	return other
