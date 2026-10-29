class_name WfcPatternModel
extends RefCounted
## Overlapping model: learns every N×N fragment of the sample and builds maps in which every N×N
## window is one of those fragments. Unlike WfcRules.learn(), which only knows pairs of neighbours,
## this reproduces shapes: coastlines, rooms, corridors, roads. A port of PatternModel.cs in unity/.

## Fragment size N.
var n: int
## One "tile" per distinct fragment; neighbours are fragments that overlap consistently.
var rules: WfcRules
var patterns: Array[PackedInt32Array] = []


## Returns null for invalid input.
## size: 2 is loose, 3 is the usual choice, 4+ copies the sample more literally.
## symmetry: also learn every rotation and mirror image of each fragment. Use only when your tiles
## look the same rotated.
static func learn(sample: PackedInt32Array, width: int, height: int, size := 3,
		periodic_sample := false, symmetry := false) -> WfcPatternModel:
	if width < 1 or height < 1 or sample.size() != width * height:
		push_error("WfcPatternModel.learn: sample size must equal width * height.")
		return null
	if size < 2:
		push_error("WfcPatternModel.learn: fragment size must be at least 2.")
		return null
	if not periodic_sample and (size > width or size > height):
		push_error("WfcPatternModel.learn: the sample must be at least N×N, or set periodic_sample.")
		return null
	for t in sample:
		if t < 0:
			push_error("WfcPatternModel.learn: tile ids must be 0 or greater.")
			return null

	var model := WfcPatternModel.new()
	model.n = size
	var index := {}
	var counts := PackedFloat64Array()
	var max_x := width if periodic_sample else width - size + 1
	var max_y := height if periodic_sample else height - size + 1
	for y in max_y:
		for x in max_x:
			var p := PackedInt32Array()
			p.resize(size * size)
			for j in size:
				for i in size:
					p[i + j * size] = sample[(x + i) % width + (y + j) % height * width]

			var variant := p
			for v in (8 if symmetry else 1):
				if v == 4:
					variant = _mirror(p, size)
				elif v > 0:
					variant = _rotate(variant, size)
				var key := str(variant)
				if index.has(key):
					counts[index[key]] += 1.0
				else:
					index[key] = model.patterns.size()
					model.patterns.append(variant)
					counts.append(1.0)

	model.rules = WfcRules.new(model.patterns.size())
	for p in model.patterns.size():
		model.rules.set_weight(p, counts[p])
		for d in 2: # +X and +Y; allow() records the opposite pair too
			for q in model.patterns.size():
				if model._overlaps(model.patterns[p], model.patterns[q], WfcRules.DX[d], WfcRules.DY[d]):
					model.rules.allow(p, d, q)
	return model


## Generates a map of width × height sample tile ids. Same arguments as WfcSolver.solve().
func solve(width: int, height: int, rng_seed := 0, periodic := false,
		max_attempts := 20) -> WfcSolver.Result:
	var result := WfcSolver.Result.new()
	result.width = width
	result.height = height
	if not periodic and (width < n or height < n):
		push_error("WfcPatternModel: width and height must be at least N (%d) unless periodic is set." % n)
		return result

	# A non-periodic map of W×H tiles holds (W-N+1)×(H-N+1) whole fragments.
	var grid_w := width if periodic else width - n + 1
	var grid_h := height if periodic else height - n + 1
	var solved := WfcSolver.solve(rules, grid_w, grid_h, rng_seed, periodic, max_attempts)
	result.attempts = solved.attempts
	if not solved.success:
		return result

	var tiles := PackedInt32Array()
	tiles.resize(width * height)
	for y in height:
		for x in width:
			# Each cell takes the top-left tile of its fragment; the last row and column read further into the edge fragments.
			var px := mini(x, grid_w - 1)
			var py := mini(y, grid_h - 1)
			tiles[x + y * width] = patterns[solved.get_tile(px, py)][(x - px) + (y - py) * n]
	result.tiles = tiles
	result.success = true
	return result


## True if q, shifted by (dx, dy) from p, agrees with p on every overlapping cell.
func _overlaps(p: PackedInt32Array, q: PackedInt32Array, dx: int, dy: int) -> bool:
	for y in range(maxi(0, dy), mini(n, n + dy)):
		for x in range(maxi(0, dx), mini(n, n + dx)):
			if p[x + y * n] != q[(x - dx) + (y - dy) * n]:
				return false
	return true


static func _rotate(p: PackedInt32Array, size: int) -> PackedInt32Array:
	var r := PackedInt32Array()
	r.resize(size * size)
	for y in size:
		for x in size:
			r[x + y * size] = p[(size - 1 - y) + x * size]
	return r


static func _mirror(p: PackedInt32Array, size: int) -> PackedInt32Array:
	var r := PackedInt32Array()
	r.resize(size * size)
	for y in size:
		for x in size:
			r[x + y * size] = p[(size - 1 - x) + y * size]
	return r
