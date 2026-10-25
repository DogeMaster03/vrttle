extends Node

var slot_index: int = 0
var _ipc: Node
var _zone: XRToolsSnapZone


func setup(ipc: Node, index: int) -> void:
	_ipc = ipc
	slot_index = index
	_zone = get_parent() as XRToolsSnapZone
	if _zone:
		_zone.has_picked_up.connect(_on_placed)
		_zone.has_dropped.connect(_on_removed)


func _on_placed(what: Variant) -> void:
	if _ipc == null or what == null:
		return
	var node := what as Node
	if node == null:
		return
	var path := str(node.get_meta("sky_path", ""))
	if path.is_empty():
		push_warning("Figure has no dump in user://dumps; portal IPC load skipped.")
		return
	if not _ipc.LoadFigure(slot_index, path):
		push_warning("Portal load failed: %s" % _ipc.LastError)
	else:
		print("Portal loaded slot %d: %s (%s)" % [slot_index, path, _ipc.LastResponse])


func _on_removed() -> void:
	if _ipc == null:
		return
	if not _ipc.RemoveFigure(slot_index):
		push_warning(_ipc.LastError)
