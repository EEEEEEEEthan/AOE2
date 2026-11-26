extends Camera3D

@export var focus := Vector3(16.0, 0.0, 16.0)
@export var distance := 40.0
@export var ortho_size := 40.0

func _ready() -> void:
	projection = Camera3D.PROJECTION_ORTHOGONAL
	size = ortho_size
	position = focus + Vector3(1.0, 1.0, 1.0).normalized() * distance
	look_at(focus, Vector3.UP)
