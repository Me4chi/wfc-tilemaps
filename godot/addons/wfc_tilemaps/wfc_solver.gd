class_name WfcSolver
extends RefCounted
## Wave Function Collapse, simple tiled model. A line-by-line port of the C# solver in unity/.
## Every cell starts as "any tile". The solver repeatedly picks the most constrained cell
## (lowest entropy), collapses it to one tile chosen by weight, and propagates what that rules out
## to the neighbours. If a cell runs out of options it restarts, up to max_attempts times.
## The same rules, seed and fixed tiles always produce the same map.


class Result:
	var success := false
	## Row-major tile ids, index = x + y * width. Empty when success is false.
	var tiles := PackedInt32Array()
	var width := 0
	var height := 0
	## Attempts used, including the successful one.
	var attempts := 0

	func get_tile(x: int, y: int) -> int:
		return tiles[x + y * width]


## fixed_tiles maps Vector2i cells to tile ids the output must contain there.
static func solve(rules: WfcRules, width: int, height: int, rng_seed := 0, periodic := false,
		max_attempts := 20, fixed_tiles := {}) -> Result:
	var result := Result.new()
	result.width = width
	result.height = height
	if rules == null or width < 1 or height < 1 or max_attempts < 1:
		push_error("WfcSolver: rules are required, width, height and max_attempts must be at least 1.")
		return result
	for cell in fixed_tiles:
		var tile: int = fixed_tiles[cell]
		if not (cell is Vector2i) or cell.x < 0 or cell.y < 0 or cell.x >= width or cell.y >= height \
				or tile < 0 or tile >= rules.tile_count:
			push_error("WfcSolver: fixed tile %s = %d is outside the map or the tile set." % [cell, tile])
			return result

	var rng := RandomNumberGenerator.new()
	rng.seed = rng_seed
	var state := _State.new(rules, width, height, periodic, rng)
	for attempt in range(1, max_attempts + 1):
		result.attempts = attempt
		if not state.reset() or not state.apply_fixed(fixed_tiles):
			return result # the rules or fixed tiles contradict: retrying cannot help
		if state.run():
			result.success = true
			result.tiles = state.collapse()
			return result
	return result


class _State:
	var w: int
	var h: int
	var n: int
	var t: int
	var periodic: bool
	var weights: PackedFloat64Array
	var weight_log_weights := PackedFloat64Array()
	# propagator[d][a]: tiles that may stand one step in direction d from tile a.
	var propagator := []

	var wave := PackedByteArray()        # [cell * t + tile]
	var supports := PackedInt32Array()   # [(cell * t + tile) * 4 + d]: tiles in the neighbour opposite d that still allow this tile
	var count := PackedInt32Array()      # remaining options per cell
	var sum_w := PackedFloat64Array()
	var sum_w_log_w := PackedFloat64Array()
	var banned: Array[int] = []          # stack of cell * t + tile
	var rng: RandomNumberGenerator
	var contradiction := false

	# Min-heap of (entropy, cell, count at push). Stale entries are skipped on pop,
	# so finding the most constrained cell costs O(log n) instead of a full scan.
	var heap_key := PackedFloat64Array()
	var heap_cell := PackedInt32Array()
	var heap_count := PackedInt32Array()
	var heap_size := 0

	func _init(rules: WfcRules, width: int, height: int, is_periodic: bool,
			random: RandomNumberGenerator) -> void:
		rng = random
		w = width
		h = height
		n = w * h
		t = rules.tile_count
		periodic = is_periodic
		weights = rules.weights.duplicate()
		weight_log_weights.resize(t)
		for a in t:
			weight_log_weights[a] = weights[a] * log(weights[a]) if weights[a] > 0.0 else 0.0
		for d in 4:
			var per_tile := []
			for a in t:
				var list := PackedInt32Array()
				for b in t:
					if rules.is_allowed(a, d, b):
						list.append(b)
				per_tile.append(list)
			propagator.append(per_tile)
		wave.resize(n * t)
		supports.resize(n * t * 4)
		count.resize(n)
		sum_w.resize(n)
		sum_w_log_w.resize(n)

	## Returns false if the rules alone already leave some cell without options.
	func reset() -> bool:
		contradiction = false
		banned.clear()
		heap_size = 0
		var start_w := 0.0
		var start_w_log_w := 0.0
		for a in t:
			start_w += weights[a]
			start_w_log_w += weight_log_weights[a]
		wave.fill(1)
		count.fill(t)
		sum_w.fill(start_w)
		sum_w_log_w.fill(start_w_log_w)
		for cell in n:
			for a in t:
				for d in 4:
					supports[(cell * t + a) * 4 + d] = propagator[WfcRules.opposite(d)][a].size()
			if t > 1:
				_push(cell)

		for cell in n:
			for a in t:
				# Weight 0 means "never".
				if weights[a] <= 0.0:
					_ban(cell, a)
					continue
				# A tile with no allowed neighbour on a side cannot stand where that side has a cell.
				for d in 4:
					var o := WfcRules.opposite(d)
					if propagator[o][a].size() == 0 and _has_neighbour(cell, o):
						_ban(cell, a)
						break
		_propagate()
		return not contradiction

	@warning_ignore("integer_division")
	func _has_neighbour(cell: int, d: int) -> bool:
		if periodic:
			return true
		var nx: int = cell % w + WfcRules.DX[d]
		var ny: int = cell / w + WfcRules.DY[d]
		return nx >= 0 and ny >= 0 and nx < w and ny < h

	func apply_fixed(fixed_tiles: Dictionary) -> bool:
		for cell_pos in fixed_tiles:
			var tile: int = fixed_tiles[cell_pos]
			var cell: int = cell_pos.x + cell_pos.y * w
			if wave[cell * t + tile] == 0:
				return false
			for a in t:
				if a != tile and wave[cell * t + a] == 1:
					_ban(cell, a)
			_propagate()
			if contradiction:
				return false
		return not contradiction

	func run() -> bool:
		while not contradiction:
			var cell := _lowest_entropy_cell()
			if cell < 0:
				return true # everything collapsed
			_observe(cell)
			_propagate()
		return false

	func collapse() -> PackedInt32Array:
		var out := PackedInt32Array()
		out.resize(n)
		for cell in n:
			for a in t:
				if wave[cell * t + a] == 1:
					out[cell] = a
					break
		return out

	func _lowest_entropy_cell() -> int:
		while heap_size > 0:
			var cell := heap_cell[0]
			var pushed_count := heap_count[0]
			_pop_root()
			if pushed_count == count[cell] and count[cell] > 1:
				return cell
		return -1

	@warning_ignore("integer_division")
	func _push(cell: int) -> void:
		if heap_size == heap_key.size():
			var grown := maxi(16, heap_size * 2)
			heap_key.resize(grown)
			heap_cell.resize(grown)
			heap_count.resize(grown)
		# Tiny noise breaks ties without a directional bias.
		var key := log(sum_w[cell]) - sum_w_log_w[cell] / sum_w[cell] + 1e-6 * rng.randf()
		var i := heap_size
		heap_size += 1
		while i > 0:
			var parent := (i - 1) / 2
			if heap_key[parent] <= key:
				break
			heap_key[i] = heap_key[parent]
			heap_cell[i] = heap_cell[parent]
			heap_count[i] = heap_count[parent]
			i = parent
		heap_key[i] = key
		heap_cell[i] = cell
		heap_count[i] = count[cell]

	func _pop_root() -> void:
		heap_size -= 1
		var last := heap_size
		if last == 0:
			return
		var key := heap_key[last]
		var i := 0
		while true:
			var child := i * 2 + 1
			if child >= last:
				break
			if child + 1 < last and heap_key[child + 1] < heap_key[child]:
				child += 1
			if key <= heap_key[child]:
				break
			heap_key[i] = heap_key[child]
			heap_cell[i] = heap_cell[child]
			heap_count[i] = heap_count[child]
			i = child
		heap_key[i] = key
		heap_cell[i] = heap_cell[last]
		heap_count[i] = heap_count[last]

	func _observe(cell: int) -> void:
		var roll := rng.randf() * sum_w[cell]
		var chosen := -1
		for a in t:
			if wave[cell * t + a] == 0:
				continue
			chosen = a
			roll -= weights[a]
			if roll < 0.0:
				break
		for a in t:
			if a != chosen and wave[cell * t + a] == 1:
				_ban(cell, a)

	func _ban(cell: int, tile: int) -> void:
		var i := cell * t + tile
		if wave[i] == 0:
			return
		wave[i] = 0
		for d in 4:
			supports[i * 4 + d] = 0
		banned.push_back(i)
		count[cell] -= 1
		sum_w[cell] -= weights[tile]
		sum_w_log_w[cell] -= weight_log_weights[tile]
		if count[cell] == 0:
			contradiction = true
		elif count[cell] > 1:
			_push(cell)

	@warning_ignore("integer_division")
	func _propagate() -> void:
		while not banned.is_empty():
			var i: int = banned.pop_back()
			var cell := i / t
			var tile := i % t
			var x := cell % w
			var y := cell / w
			for d in 4:
				var nx: int = x + WfcRules.DX[d]
				var ny: int = y + WfcRules.DY[d]
				if periodic:
					nx = (nx + w) % w
					ny = (ny + h) % h
				elif nx < 0 or ny < 0 or nx >= w or ny >= h:
					continue
				var neighbour := nx + ny * w
				var list: PackedInt32Array = propagator[d][tile]
				for b in list:
					var s := (neighbour * t + b) * 4 + d
					if supports[s] == 0:
						continue # already banned
					supports[s] -= 1
					if supports[s] == 0:
						_ban(neighbour, b)
