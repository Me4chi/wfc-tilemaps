class_name WfcRules
extends RefCounted
## Which tiles may stand next to which, and how often each tile should appear.
## Tiles are plain integers 0..tile_count-1; WfcTileMap maps them to real tiles.
## Build rules by hand with allow() or learn them from an example map with learn().

enum Direction { PLUS_X, PLUS_Y, MINUS_X, MINUS_Y }

const DX := [1, 0, -1, 0]
const DY := [0, 1, 0, -1]

var tile_count: int
## Relative frequency of each tile. 0 removes the tile from the output entirely.
var weights := PackedFloat64Array()
# _allowed[(d * tile_count + a) * tile_count + b] == 1: tile b may sit one step in direction d from tile a.
var _allowed := PackedByteArray()


func _init(count: int = 1) -> void:
	if count < 1:
		push_error("WfcRules: at least one tile is required.")
		count = 1
	tile_count = count
	weights.resize(count)
	weights.fill(1.0)
	_allowed.resize(4 * count * count)


static func opposite(direction: int) -> int:
	return (direction + 2) % 4


func set_weight(tile: int, weight: float) -> void:
	if not _valid(tile) or not (weight >= 0.0) or is_inf(weight):
		push_error("WfcRules: weight must be a finite number >= 0 for an existing tile.")
		return
	weights[tile] = weight


## Lets b stand one step in direction from a (and a next to b the other way).
func allow(a: int, direction: int, b: int) -> void:
	if not _valid(a) or not _valid(b):
		push_error("WfcRules: tile id outside 0..%d." % (tile_count - 1))
		return
	_allowed[_index(direction, a, b)] = 1
	_allowed[_index(opposite(direction), b, a)] = 1


## b may stand at x + 1 of a.
func allow_x(a: int, b: int) -> void:
	allow(a, Direction.PLUS_X, b)


## b may stand at y + 1 of a.
func allow_y(a: int, b: int) -> void:
	allow(a, Direction.PLUS_Y, b)


func is_allowed(a: int, direction: int, b: int) -> bool:
	return _allowed[_index(direction, a, b)] == 1


## Learns rules from an example map: every pair of neighbours seen in the sample becomes allowed,
## and each tile's weight is how many times it appears. sample is row-major, index = x + y * width.
## Returns null for invalid input.
static func learn(sample: PackedInt32Array, width: int, height: int, periodic := false) -> WfcRules:
	if width < 1 or height < 1 or sample.size() != width * height:
		push_error("WfcRules.learn: sample size must equal width * height.")
		return null
	var max_id := 0
	for t in sample:
		if t < 0:
			push_error("WfcRules.learn: tile ids must be 0 or greater.")
			return null
		max_id = maxi(max_id, t)

	var rules := WfcRules.new(max_id + 1)
	var counts := PackedFloat64Array()
	counts.resize(max_id + 1)
	for y in height:
		for x in width:
			var a := sample[x + y * width]
			counts[a] += 1.0
			for d in 2: # +X and +Y; allow() records the opposite pair too
				var nx: int = x + DX[d]
				var ny: int = y + DY[d]
				if periodic:
					nx = (nx + width) % width
					ny = (ny + height) % height
				elif nx >= width or ny >= height:
					continue
				rules.allow(a, d, sample[nx + ny * width])
	rules.weights = counts
	return rules


func _index(d: int, a: int, b: int) -> int:
	return (d * tile_count + a) * tile_count + b


func _valid(tile: int) -> bool:
	return tile >= 0 and tile < tile_count
