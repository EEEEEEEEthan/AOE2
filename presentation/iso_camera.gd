class_name IsoCamera
extends Camera3D

const PAN_SPEED := 14.0
const ZOOM_STEP := 2.0
const MIN_ORTHO_SIZE := 8.0
const MAX_ORTHO_SIZE := 24.0
const ACTION_PAN_FORWARD := "camera_pan_forward"
const ACTION_PAN_BACK := "camera_pan_back"
const ACTION_PAN_LEFT := "camera_pan_left"
const ACTION_PAN_RIGHT := "camera_pan_right"
const ACTION_ZOOM_IN := "camera_zoom_in"
const ACTION_ZOOM_OUT := "camera_zoom_out"

@export var focus := Vector3(16.0, 0.0, 16.0)
@export var distance := 40.0
@export var ortho_size := 14.0
@export var grid_width := 32.0
@export var grid_height := 32.0

func _ready() -> void:
	projection = Camera3D.PROJECTION_ORTHOGONAL
	refresh_view()

func _process(delta: float) -> void:
	var pan := Vector3.ZERO
	var basis := global_transform.basis
	var forward := -basis.z
	forward.y = 0.0
	forward = forward.normalized()
	var right := basis.x
	right.y = 0.0
	right = right.normalized()
	if _action_pressed(ACTION_PAN_FORWARD):
		pan += forward
	if _action_pressed(ACTION_PAN_BACK):
		pan -= forward
	if _action_pressed(ACTION_PAN_LEFT):
		pan -= right
	if _action_pressed(ACTION_PAN_RIGHT):
		pan += right
	if pan == Vector3.ZERO:
		return
	focus += pan.normalized() * PAN_SPEED * delta
	refresh_view()

func _unhandled_input(event: InputEvent) -> void:
	if InputMap.has_action(ACTION_ZOOM_IN) and event.is_action_pressed(ACTION_ZOOM_IN):
		_zoom_by(-ZOOM_STEP)
	elif InputMap.has_action(ACTION_ZOOM_OUT) and event.is_action_pressed(ACTION_ZOOM_OUT):
		_zoom_by(ZOOM_STEP)

func _action_pressed(action: String) -> bool:
	return InputMap.has_action(action) and Input.is_action_pressed(action)

func _zoom_by(amount: float) -> void:
	ortho_size = clampf(ortho_size + amount, MIN_ORTHO_SIZE, MAX_ORTHO_SIZE)
	refresh_view()

func refresh_view() -> void:
	focus.x = clampf(focus.x, 0.0, grid_width)
	focus.y = 0.0
	focus.z = clampf(focus.z, 0.0, grid_height)
	size = ortho_size
	position = focus + Vector3(1.0, 1.0, 1.0).normalized() * distance
	look_at(focus, Vector3.UP)
