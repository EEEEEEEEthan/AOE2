extends Camera3D

const PAN_SPEED := 14.0
const ZOOM_STEP := 2.0
const MIN_ORTHO_SIZE := 8.0
const MAX_ORTHO_SIZE := 24.0

@export var focus := Vector3(16.0, 0.0, 16.0)
@export var distance := 40.0
@export var ortho_size := 14.0
@export var grid_width := 32.0
@export var grid_height := 32.0

func _ready() -> void:
	projection = Camera3D.PROJECTION_ORTHOGONAL
	_apply_view()

func _process(delta: float) -> void:
	var pan := Vector3.ZERO
	var basis := global_transform.basis
	var forward := -basis.z
	forward.y = 0.0
	forward = forward.normalized()
	var right := basis.x
	right.y = 0.0
	right = right.normalized()
	if Input.is_physical_key_pressed(KEY_W) or Input.is_physical_key_pressed(KEY_UP):
		pan += forward
	if Input.is_physical_key_pressed(KEY_S) or Input.is_physical_key_pressed(KEY_DOWN):
		pan -= forward
	if Input.is_physical_key_pressed(KEY_A) or Input.is_physical_key_pressed(KEY_LEFT):
		pan -= right
	if Input.is_physical_key_pressed(KEY_D) or Input.is_physical_key_pressed(KEY_RIGHT):
		pan += right
	if pan == Vector3.ZERO:
		return
	focus += pan.normalized() * PAN_SPEED * delta
	_apply_view()

func _unhandled_input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	var mouse_button := event as InputEventMouseButton
	if not mouse_button.pressed:
		return
	if mouse_button.button_index == MOUSE_BUTTON_WHEEL_UP:
		_zoom_by(-ZOOM_STEP)
	elif mouse_button.button_index == MOUSE_BUTTON_WHEEL_DOWN:
		_zoom_by(ZOOM_STEP)

func _zoom_by(amount: float) -> void:
	ortho_size = clampf(ortho_size + amount, MIN_ORTHO_SIZE, MAX_ORTHO_SIZE)
	_apply_view()

func _apply_view() -> void:
	focus.x = clampf(focus.x, 0.0, grid_width)
	focus.y = 0.0
	focus.z = clampf(focus.z, 0.0, grid_height)
	size = ortho_size
	position = focus + Vector3(1.0, 1.0, 1.0).normalized() * distance
	look_at(focus, Vector3.UP)
