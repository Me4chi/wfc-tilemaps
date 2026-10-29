@tool
class_name WfcTileMap
extends Node
## Learns from a hand-painted sample TileMapLayer and fills the target layer with a new map in the same
## style. Empty cells in the sample count as a tile too, so holes and open space are reproduced.
## Tick "Generate In Editor" in the inspector to run it in the editor.

## Emitted after a successful generation with the WfcSolver.Result.
signal generated(result)

enum Model {
	## Copies N×N fragments of the sample: reproduces shapes. The usual choice.
	OVERLAPPING,
	## Only pairs of neighbouring tiles: looser, for tile sets designed to snap together.
	ADJACENT,
}

## A small example map painted by hand. It can be hidden.
@export var sample: TileMapLayer
## Where the generated map is written. It is cleared first.
@export var target: TileMapLayer
@export var model := Model.OVERLAPPING
## Overlapping model: fragment size. 2 is loose, 3 is the usual choice, 4+ copies the sample more literally.
@export_range(2, 6) var pattern_size := 3
## Overlapping model: also learn rotated and mirrored fragments. Use only when your tiles look the same rotated.
@export var symmetry := false
@export var width := 48
@export var height := 32
@export var rng_seed := 12345
## Pick a new random seed every time generate() is called.
@export var randomize_seed := false
## Make the output tile seamlessly: the right edge must fit the left edge, the top the bottom.
@export var periodic := false
## Treat the sample as wrapping, so its right edge also touches its left edge.
@export var periodic_sample := false
## How many fresh starts to try when the solver paints itself into a corner.
@export var max_attempts := 20
## Generate automatically when the scene starts (not in the editor).
@export var generate_on_ready := true
## Inspector button: tick to generate in the editor.
@export var generate_in_editor := false:
	set(value):
		if value and is_inside_tree():
			generate()

## The last solver result, or null before the first run.
var result: WfcSolver.Result


func _ready() -> void:
	if generate_on_ready and not Engine.is_editor_hint():
		generate()


## Generates and writes the map. Returns false (and leaves target untouched) if no map was found.
func generate() -> bool:
	if sample == null or target == null:
		push_error("WfcTileMap: assign both sample and target.")
		return false
	if randomize_seed:
		rng_seed = randi()

	var palette: Array[Vector4i] = []
	var rect := sample.get_used_rect()
	var cells := read_layer(sample, rect, palette)
	if cells.is_empty():
		push_error("WfcTileMap: the sample layer is empty.")
		return false

	if model == Model.OVERLAPPING:
		var patterns := WfcPatternModel.learn(cells, rect.size.x, rect.size.y, pattern_size, periodic_sample, symmetry)
		if patterns == null:
			return false
		result = patterns.solve(width, height, rng_seed, periodic, max_attempts)
	else:
		var rules := WfcRules.learn(cells, rect.size.x, rect.size.y, periodic_sample)
		result = WfcSolver.solve(rules, width, height, rng_seed, periodic, max_attempts)
	if not result.success:
		push_warning("WfcTileMap: no map found in %d attempts. Try another seed, more attempts or a richer sample." % result.attempts)
		return false

	write_layer(target, result, palette)
	generated.emit(result)
	return true


## Converts the cells of a layer inside rect into tile ids. palette receives each id's tile as
## Vector4i(source_id, atlas_x, atlas_y, alternative); empty cells are Vector4i(-1, -1, -1, -1).
static func read_layer(layer: TileMapLayer, rect: Rect2i, palette: Array[Vector4i]) -> PackedInt32Array:
	var cells := PackedInt32Array()
	if rect.size.x <= 0 or rect.size.y <= 0:
		return cells
	cells.resize(rect.size.x * rect.size.y)
	var ids := {}
	for y in rect.size.y:
		for x in rect.size.x:
			var pos := rect.position + Vector2i(x, y)
			var source := layer.get_cell_source_id(pos)
			var key := Vector4i(-1, -1, -1, -1)
			if source != -1:
				var coords := layer.get_cell_atlas_coords(pos)
				key = Vector4i(source, coords.x, coords.y, layer.get_cell_alternative_tile(pos))
			if not ids.has(key):
				ids[key] = palette.size()
				palette.append(key)
			cells[x + y * rect.size.x] = ids[key]
	return cells


## Writes a result into a layer starting at cell (0, 0), after clearing it.
static func write_layer(layer: TileMapLayer, solved: WfcSolver.Result, palette: Array[Vector4i]) -> void:
	layer.clear()
	for y in solved.height:
		for x in solved.width:
			var key := palette[solved.get_tile(x, y)]
			if key.x != -1:
				layer.set_cell(Vector2i(x, y), key.x, Vector2i(key.y, key.z), key.w)
