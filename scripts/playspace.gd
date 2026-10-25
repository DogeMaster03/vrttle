extends Node3D

@export var dumps_dir: String = "user://dumps"
@export var models_dir: String = "user://models"
@export var start_rpcs3_on_ready: bool = true

@onready var _launcher: Node = $Rpcs3Launcher
@onready var _ipc: Node = $SkylanderPortalIpc
@onready var _status: Label3D = $StatusLabel
@onready var _start_xr: XRToolsStartXR = $StartXR
@onready var _desktop_camera: Camera3D = $DesktopCamera

func _ready() -> void:
	Engine.max_fps = 0
	OS.low_processor_usage_mode = false
	_load_user_config()
	_ensure_user_dirs()
	_build_room()
	_build_portal()
	_spawn_figures()
	_start_xr.xr_failed_to_initialize.connect(_on_xr_failed)
	_start_xr.xr_started.connect(_on_xr_started)
	if start_rpcs3_on_ready:
		_launcher.StartEmulator()
	_refresh_status()


func _on_xr_started() -> void:
	_desktop_camera.current = false
	$DualShockVrMapper.Enabled = true


func _on_xr_failed() -> void:
	_desktop_camera.current = true
	$DualShockVrMapper.Enabled = false
	_status.text = "OpenXR unavailable — desktop camera active.\n" + _launcher.Status


func _load_user_config() -> void:
	var cfg := ConfigFile.new()
	if cfg.load("res://vrttle_config.cfg") != OK:
		return
	var exe := str(cfg.get_value("rpcs3", "exe", _launcher.Rpcs3Exe))
	var game := str(cfg.get_value("rpcs3", "game_path", _launcher.GamePath))
	_launcher.Rpcs3Exe = exe
	_launcher.GamePath = game
	_launcher.NoGui = bool(cfg.get_value("rpcs3", "no_gui", false))
	_launcher.IpcPort = int(cfg.get_value("portal", "port", 28013))
	_ipc.Host = str(cfg.get_value("portal", "host", "127.0.0.1"))
	_ipc.Port = _launcher.IpcPort
	dumps_dir = str(cfg.get_value("portal", "dumps_dir", cfg.get_value("portal", "figures_dir", dumps_dir)))
	models_dir = str(cfg.get_value("portal", "models_dir", models_dir))


func _build_room() -> void:
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.02, 0.03, 0.05)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.18, 0.2, 0.26)
	env.ambient_light_energy = 0.55
	env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	env.glow_enabled = false
	env.ssao_enabled = false
	env.ssil_enabled = false
	env.ssr_enabled = false
	env.fog_enabled = false
	env.volumetric_fog_enabled = false
	env.sdfgi_enabled = false
	var world_env := WorldEnvironment.new()
	world_env.name = "WorldEnvironment"
	world_env.environment = env
	add_child(world_env)

	var floor_body := StaticBody3D.new()
	floor_body.name = "Floor"
	var floor_col := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(8, 0.1, 8)
	floor_col.shape = box
	floor_col.position = Vector3(0, -0.05, 0)
	floor_body.add_child(floor_col)
	add_child(floor_body)

	var floor_mesh := MeshInstance3D.new()
	var plane := BoxMesh.new()
	plane.size = Vector3(8, 0.1, 8)
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.12, 0.14, 0.22)
	plane.material = mat
	floor_mesh.mesh = plane
	floor_mesh.position = Vector3(0, -0.05, 0)
	add_child(floor_mesh)


func _build_portal() -> void:
	var portal := Node3D.new()
	portal.name = "PortalOfPower"
	portal.position = Vector3(0.0, 0.72, -0.55)
	add_child(portal)

	var disc := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = 0.28
	cyl.bottom_radius = 0.28
	cyl.height = 0.06
	var glow := StandardMaterial3D.new()
	glow.albedo_color = Color(0.15, 0.45, 1.0)
	glow.emission_enabled = true
	glow.emission = Color(0.2, 0.5, 1.0)
	glow.emission_energy_multiplier = 2.4
	cyl.material = glow
	disc.mesh = cyl
	portal.add_child(disc)

	var base := MeshInstance3D.new()
	var base_mesh := CylinderMesh.new()
	base_mesh.top_radius = 0.18
	base_mesh.bottom_radius = 0.22
	base_mesh.height = 0.7
	var plastic := StandardMaterial3D.new()
	plastic.albedo_color = Color(0.85, 0.88, 0.95)
	base_mesh.material = plastic
	base.mesh = base_mesh
	base.position = Vector3(0, -0.38, 0)
	portal.add_child(base)

	for i in 8:
		var angle := TAU * float(i) / 8.0
		var slot := Area3D.new()
		slot.name = "Slot%d" % i
		slot.collision_layer = 4
		slot.collision_mask = 65540
		slot.set_script(load("res://addons/godot-xr-tools/objects/snap_zone.gd"))
		slot.set("grab_distance", 0.08)
		slot.set("snap_require", "skylander")
		slot.position = Vector3(cos(angle) * 0.16, 0.05, sin(angle) * 0.16)
		var col := CollisionShape3D.new()
		var sphere := SphereShape3D.new()
		sphere.radius = 0.08
		col.shape = sphere
		slot.add_child(col)
		portal.add_child(slot)
		var glue := Node.new()
		glue.set_script(load("res://scripts/portal_slot.gd"))
		slot.add_child(glue)
		glue.call_deferred("setup", _ipc, i)


func _ensure_user_dirs() -> void:
	for path in [dumps_dir, models_dir]:
		var abs_path := ProjectSettings.globalize_path(path)
		if DirAccess.make_dir_recursive_absolute(abs_path) != OK and not DirAccess.dir_exists_absolute(abs_path):
			push_warning("Could not create %s" % abs_path)


func _spawn_figures() -> void:
	_ensure_user_dirs()
	var dumps := _index_files(dumps_dir, [".sky", ".bin", ".dump"])
	var models := _index_files(models_dir, [".glb", ".gltf"])
	var names: Array[String] = []
	var seen := {}
	for key in dumps.keys():
		seen[key] = true
		names.append(key)
	for key in models.keys():
		if not seen.has(key):
			names.append(key)

	if names.is_empty():
		for i in 4:
			_make_figure("Demo Figure %d" % (i + 1), "", "", Vector3(-0.9 + i * 0.22, 0.95, -0.15), _element_color(i))
		return

	names.sort()
	for i in names.size():
		var key: String = names[i]
		var dump_path := str(dumps.get(key, ""))
		var model_path := str(models.get(key, ""))
		var display := key
		if not dump_path.is_empty():
			display = dump_path.get_file().get_basename()
		elif not model_path.is_empty():
			display = model_path.get_file().get_basename()
		var sky_abs := ""
		if not dump_path.is_empty():
			sky_abs = ProjectSettings.globalize_path(dump_path)
		_make_figure(display, sky_abs, model_path, Vector3(-0.9 + (i % 6) * 0.22, 0.95, -0.15 + floor(i / 6.0) * 0.2), _element_color(i))


func _index_files(folder: String, suffixes: Array) -> Dictionary:
	var result := {}
	var dir := DirAccess.open(folder)
	if dir == null:
		return result
	dir.list_dir_begin()
	var file := dir.get_next()
	while file != "":
		if not dir.current_is_dir():
			for suffix in suffixes:
				if file.to_lower().ends_with(suffix):
					result[file.get_basename().to_lower()] = folder.path_join(file)
					break
		file = dir.get_next()
	return result


func _make_figure(figure_name: String, sky_path: String, model_path: String, pos: Vector3, color: Color) -> void:
	var body := RigidBody3D.new()
	body.collision_layer = 4
	body.collision_mask = 196615
	body.freeze_mode = RigidBody3D.FREEZE_MODE_KINEMATIC
	body.freeze = true
	body.add_to_group("skylander")
	body.set_script(load("res://addons/godot-xr-tools/objects/pickable.gd"))
	body.position = pos
	body.set_meta("sky_path", sky_path)
	body.set_meta("figure_name", figure_name)

	var imported := _try_load_model(model_path) if not model_path.is_empty() else null
	var col := CollisionShape3D.new()
	if imported:
		_normalize_model(imported)
		body.add_child(imported)
		var aabb := _compute_aabb(imported, Transform3D.IDENTITY)
		var box := BoxShape3D.new()
		box.size = aabb.size.max(Vector3(0.04, 0.08, 0.04))
		col.shape = box
		col.position = aabb.get_center()
	else:
		var mesh := MeshInstance3D.new()
		var cap := CapsuleMesh.new()
		cap.radius = 0.035
		cap.height = 0.14
		var mat := StandardMaterial3D.new()
		mat.albedo_color = color
		cap.material = mat
		mesh.mesh = cap
		body.add_child(mesh)
		var shape := CapsuleShape3D.new()
		shape.radius = 0.035
		shape.height = 0.14
		col.shape = shape
	body.add_child(col)

	var label := Label3D.new()
	label.text = figure_name
	label.font_size = 16
	label.pixel_size = 0.001
	label.position = Vector3(0, 0.12, 0)
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	body.add_child(label)
	add_child(body)


func _try_load_model(model_path: String) -> Node3D:
	var abs_path := ProjectSettings.globalize_path(model_path)
	if not FileAccess.file_exists(abs_path):
		push_warning("Figure model missing: %s" % abs_path)
		return null
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_file(abs_path, state)
	if err != OK:
		push_warning("Could not import figure model %s (error %s)" % [abs_path, err])
		return null
	var scene := doc.generate_scene(state)
	if scene is Node3D:
		return scene
	var wrap := Node3D.new()
	wrap.add_child(scene)
	return wrap


func _normalize_model(model: Node3D) -> void:
	var aabb := _compute_aabb(model, Transform3D.IDENTITY)
	if aabb.size.y <= 0.0001:
		return
	var scale := 0.14 / aabb.size.y
	model.scale = Vector3.ONE * scale
	model.position = -aabb.get_center() * scale


func _compute_aabb(node: Node, parent_xf: Transform3D) -> AABB:
	var xf := parent_xf
	if node is Node3D:
		xf = parent_xf * (node as Node3D).transform
	var aabb := AABB()
	var started := false
	if node is MeshInstance3D:
		var mesh := (node as MeshInstance3D).mesh
		if mesh:
			aabb = xf * mesh.get_aabb()
			started = true
	for child in node.get_children():
		var child_aabb := _compute_aabb(child, xf)
		if child_aabb.size.length() <= 0.0:
			continue
		if started:
			aabb = aabb.merge(child_aabb)
		else:
			aabb = child_aabb
			started = true
	return aabb


func _element_color(index: int) -> Color:
	var colors := [
		Color(0.95, 0.35, 0.15),
		Color(0.2, 0.55, 1.0),
		Color(0.2, 0.8, 0.3),
		Color(0.95, 0.85, 0.2),
		Color(0.7, 0.25, 0.85),
		Color(0.9, 0.9, 0.95),
		Color(0.45, 0.3, 0.15),
		Color(0.15, 0.15, 0.18)
	]
	return colors[index % colors.size()]


var _status_timer: float = 0.0


func _process(delta: float) -> void:
	_status_timer += delta
	if _status_timer < 1.0:
		return
	_status_timer = 0.0
	_refresh_status()


func _refresh_status() -> void:
	var capture_line = str($Rpcs3WindowCapture.CaptureStatus)
	var ipc_line = str(_ipc.ConnectionStatus)
	_status.text = "%s\n%s\n%s" % [_launcher.Status, capture_line, ipc_line]


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_accept"):
		_launcher.StartEmulator()
		_refresh_status()
