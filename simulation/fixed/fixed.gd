class_name Fixed
extends RefCounted

const FRACTION_BITS := 16
const SCALE := 1 << FRACTION_BITS
const HALF := SCALE >> 1

static func from_int(value: int) -> int:
	return value << FRACTION_BITS

static func from_float(value: float) -> int:
	return int(round(value * float(SCALE)))

static func to_float(value: int) -> float:
	return float(value) / float(SCALE)

static func to_int(value: int) -> int:
	return value >> FRACTION_BITS

static func add(a: int, b: int) -> int:
	return a + b

static func sub(a: int, b: int) -> int:
	return a - b

static func mul(a: int, b: int) -> int:
	return (a * b) >> FRACTION_BITS

static func mul_int(a: int, factor: int) -> int:
	return a * factor

static func div(a: int, b: int) -> int:
	if b == 0:
		return 0
	return (a << FRACTION_BITS) / b

static func div_int(a: int, divisor: int) -> int:
	if divisor == 0:
		return 0
	return a / divisor

static func neg(a: int) -> int:
	return -a

static func abs(a: int) -> int:
	return a if a >= 0 else -a

static func min(a: int, b: int) -> int:
	return a if a < b else b

static func max(a: int, b: int) -> int:
	return a if a > b else b

static func clamp(value: int, low: int, high: int) -> int:
	return min(max(value, low), high)

static func lerp(a: int, b: int, t: int) -> int:
	return a + mul(b - a, t)

static func sign(a: int) -> int:
	if a > 0:
		return 1
	if a < 0:
		return -1
	return 0

static func isqrt(value: int) -> int:
	if value < 2:
		return value if value > 0 else 0
	var x := value
	var y := (x + 1) >> 1
	while y < x:
		x = y
		y = (x + value / x) >> 1
	return x

static func sqrt(value: int) -> int:
	if value <= 0:
		return 0
	return isqrt(value << FRACTION_BITS)
