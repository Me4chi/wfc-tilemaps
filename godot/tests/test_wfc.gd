extends SceneTree
## Headless tests for the Godot port, mirroring the NUnit tests in unity/Tests.
## Run: godot --headless --path godot --import && godot --headless --path godot -s res://tests/test_wfc.gd

const ISLAND := [
	"~~~~~~~~~~~~",
	"~~~....~~~~~",
	"~~..\"\"\"..~~~",
	"~~.\"\"TT\"\".~~",
	"~.\"\"TTTT\"\".~",
	"~.\"\"TTT\"\"\".~",
	"~~.\"\"\"\"\"..~~",
	"~~~......~~~",
	"~~~~~~~~~~~~",
]

const ROOMS := [
	"#########",
	"#...#...#",
	"#...+...#",
	"#...#...#",
	"##+######",
	"#...#####",
	"#########",
]

var failures := 0
var checks := 0


func _init() -> void:
	test_learn_records_neighbours_and_counts()
	test_output_only_uses_learned_neighbours()
	test_periodic_output_wraps()
	test_same_seed_same_map()
	test_fixed_tiles_are_kept()
	test_contradictory_fixed_tiles_fail()
	test_zero_weight_removes_tile()
	test_checkerboard()
	test_impossible_rules_fail()
	test_pattern_windows_come_from_sample()
	test_pattern_symmetry()
	test_tile_map_node()
	print("%d checks, %d failures" % [checks, failures])
	quit(1 if failures > 0 else 0)


func check(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures += 1
		printerr("FAIL: " + message)


func parse(rows: Array, symbols: String) -> PackedInt32Array:
	var cells := PackedInt32Array()
	for row in rows:
		for c in row:
			cells.append(symbols.find(c))
	return cells


func island_rules(periodic := false) -> WfcRules:
	return WfcRules.learn(parse(ISLAND, "~.\"T"), 12, 9, periodic)


func respects_rules(rules: WfcRules, r: WfcSolver.Result, periodic: bool) -> bool:
	if not r.success:
		return false
	for y in r.height:
		for x in r.width:
			for d in 2:
				var nx: int = x + WfcRules.DX[d]
				var ny: int = y + WfcRules.DY[d]
				if periodic:
					nx %= r.width
					ny %= r.height
				elif nx >= r.width or ny >= r.height:
					continue
				if not rules.is_allowed(r.get_tile(x, y), d, r.get_tile(nx, ny)):
					return false
	return true


func windows(cells: PackedInt32Array, width: int, height: int, n: int, periodic: bool) -> Dictionary:
	var found := {}
	var max_x := width if periodic else width - n + 1
	var max_y := height if periodic else height - n + 1
	for y in max_y:
		for x in max_x:
			var p := PackedInt32Array()
			for j in n:
				for i in n:
					p.append(cells[(x + i) % width + (y + j) % height * width])
			found[str(p)] = true
	return found


func test_learn_records_neighbours_and_counts() -> void:
	var rules := island_rules()
	check(rules.tile_count == 4, "learn: tile count")
	check(rules.is_allowed(0, WfcRules.Direction.PLUS_X, 1), "learn: water then sand")
	check(rules.is_allowed(1, WfcRules.Direction.MINUS_X, 0), "learn: opposite pair")
	check(not rules.is_allowed(0, WfcRules.Direction.PLUS_X, 2), "learn: water never touches grass")
	check(rules.weights[3] == parse(ISLAND, "~.\"T").count(3), "learn: weight is the count")


func test_output_only_uses_learned_neighbours() -> void:
	var rules := island_rules()
	for seed_value in [1, 2, 42]:
		var r := WfcSolver.solve(rules, 30, 20, seed_value)
		check(respects_rules(rules, r, false), "adjacent model respects rules, seed %d" % seed_value)


func test_periodic_output_wraps() -> void:
	var rules := island_rules(true)
	var r := WfcSolver.solve(rules, 16, 16, 9, true)
	check(respects_rules(rules, r, true), "periodic output wraps")


func test_same_seed_same_map() -> void:
	var rules := island_rules()
	var a := WfcSolver.solve(rules, 20, 12, 5)
	var b := WfcSolver.solve(rules, 20, 12, 5)
	var c := WfcSolver.solve(rules, 20, 12, 6)
	check(a.tiles == b.tiles, "same seed, same map")
	check(a.tiles != c.tiles, "different seed, different map")


func test_fixed_tiles_are_kept() -> void:
	var rules := island_rules()
	var fixed := {Vector2i(0, 0): 0, Vector2i(10, 8): 3, Vector2i(29, 19): 0}
	var r := WfcSolver.solve(rules, 30, 20, 3, false, 20, fixed)
	check(respects_rules(rules, r, false), "fixed tiles: rules respected")
	for cell in fixed:
		check(r.success and r.get_tile(cell.x, cell.y) == fixed[cell], "fixed tile kept at %s" % cell)


func test_contradictory_fixed_tiles_fail() -> void:
	var rules := island_rules()
	var r := WfcSolver.solve(rules, 10, 10, 0, false, 20, {Vector2i(4, 4): 0, Vector2i(5, 4): 3})
	check(not r.success and r.attempts == 1, "contradictory fixed tiles fail without retrying")


func test_zero_weight_removes_tile() -> void:
	var rules := island_rules()
	rules.set_weight(3, 0.0)
	var r := WfcSolver.solve(rules, 24, 16, 11)
	check(respects_rules(rules, r, false) and not r.tiles.has(3), "weight 0 removes the tile")


func test_checkerboard() -> void:
	var rules := WfcRules.new(2)
	rules.allow_x(0, 1)
	rules.allow_x(1, 0)
	rules.allow_y(0, 1)
	rules.allow_y(1, 0)
	var r := WfcSolver.solve(rules, 9, 7, 1)
	var ok := r.success
	if ok:
		var first := r.get_tile(0, 0)
		for y in 7:
			for x in 9:
				ok = ok and r.get_tile(x, y) == (first + x + y) % 2
	check(ok, "hand-written rules make a checkerboard")


func test_impossible_rules_fail() -> void:
	var rules := WfcRules.new(2)
	rules.allow_x(0, 0)
	var r := WfcSolver.solve(rules, 3, 3, 0, false, 4)
	check(not r.success and r.attempts == 1, "impossible rules fail immediately")


func test_pattern_windows_come_from_sample() -> void:
	var sample := parse(ROOMS, "#.+")
	var model := WfcPatternModel.learn(sample, 9, 7)
	var r := model.solve(24, 16, 7)
	check(r.success and r.tiles.size() == 24 * 16, "overlapping model succeeds")
	if r.success:
		var allowed := windows(sample, 9, 7, 3, false)
		var ok := true
		for key in windows(r.tiles, 24, 16, 3, false):
			ok = ok and allowed.has(key)
		check(ok, "every 3x3 window of the output comes from the sample")


func test_pattern_symmetry() -> void:
	var sample := parse(ROOMS, "#.+")
	var plain := WfcPatternModel.learn(sample, 9, 7)
	var symmetric := WfcPatternModel.learn(sample, 9, 7, 3, false, true)
	check(symmetric.patterns.size() > plain.patterns.size(), "symmetry adds fragments")
	check(symmetric.solve(20, 20, 2).success, "symmetric model succeeds")


func test_tile_map_node() -> void:
	var image := Image.create(48, 16, false, Image.FORMAT_RGBA8)
	var source := TileSetAtlasSource.new()
	source.texture = ImageTexture.create_from_image(image)
	source.texture_region_size = Vector2i(16, 16)
	for i in 3:
		source.create_tile(Vector2i(i, 0))
	var tile_set := TileSet.new()
	var source_id := tile_set.add_source(source)

	var sample_layer := TileMapLayer.new()
	sample_layer.tile_set = tile_set
	var target_layer := TileMapLayer.new()
	target_layer.tile_set = tile_set
	var cells := parse(ROOMS, "#.+")
	for y in 7:
		for x in 9:
			# Floor is left empty on purpose: empty cells must be learned as a tile too.
			if cells[x + y * 9] != 1:
				sample_layer.set_cell(Vector2i(x, y), source_id, Vector2i(cells[x + y * 9], 0))

	var node := WfcTileMap.new()
	node.sample = sample_layer
	node.target = target_layer
	node.width = 20
	node.height = 14
	node.rng_seed = 3
	check(node.generate(), "WfcTileMap.generate succeeds")
	var used := target_layer.get_used_rect()
	check(used.position.x >= 0 and used.position.y >= 0 and used.end.x <= 20 and used.end.y <= 14,
			"generated cells stay inside width x height")
	var has_empty := false
	for y in 14:
		for x in 20:
			if target_layer.get_cell_source_id(Vector2i(x, y)) == -1:
				has_empty = true
			else:
				check(target_layer.get_cell_atlas_coords(Vector2i(x, y)) != Vector2i(1, 0),
						"only tiles from the sample are used")
	check(has_empty, "empty cells from the sample are reproduced")
	node.free()
	sample_layer.free()
	target_layer.free()
