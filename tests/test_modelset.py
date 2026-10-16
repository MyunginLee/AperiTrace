import hashlib
from dataclasses import replace
from itertools import product

import numpy as np
import pytest
from pydantic import ValidationError

from qc5d.basis import SuperspaceBasis
from qc5d.identity import identity_diagnostics, lattice_tuples, lookup_hashes, stable_ids, tuple_id
from qc5d.model_config import ModelConfig, read_model_config, read_settings
from qc5d.modelset import (
    GenerationCancelled,
    compare_membership,
    evaluate_membership,
    generate_candidates,
    integer_chunks,
    membership_batch,
    project_tuples,
)
from qc5d.window import AcceptanceWindow


def config(**updates):
    settings = {
        "index_bound": 2,
        "chunk_size": 113,
        "physical_crop": {"radial_max": 3.0, "z_abs_max": 1.5},
    }
    settings.update(updates)
    return ModelConfig.model_validate(settings)


@pytest.mark.parametrize(
    "payload",
    [
        {"index_bound": -1},
        {"index_bound": True},
        {"index_bound": 1001},
        {"boundary_policy": "report_only"},
        {"chunk_size": 0},
        {"max_candidates": 0},
        {"physical_crop": {"radial_max": -1}},
        {"physical_scale": {"xy": 0}},
        {"phason_domain": {"min": [0.1, 0], "max": [0, 1]}},
        {"initial_phi": [0.3, 0]},
        {"initial_phi": [float("nan"), 0]},
        {"unknown": 3},
    ],
)
def test_model_config_rejects_invalid_settings(payload):
    with pytest.raises(ValidationError):
        ModelConfig.model_validate(payload)


@pytest.mark.parametrize(
    "text",
    [
        "index_bound: 1\nindex_bound: 2",
        "!!python/object:builtins.dict {}",
        "[1,2]",
        "[unterminated",
    ],
)
def test_yaml_rejects_duplicate_unsafe_or_nonmapping_input(tmp_path, text):
    path = tmp_path / "input.yaml"
    path.write_text(text, encoding="utf-8")
    with pytest.raises(ValueError):
        read_settings(path)


def test_yaml_and_json_configs_agree(tmp_path):
    yaml_path, json_path = tmp_path / "model.yaml", tmp_path / "model.json"
    yaml_path.write_text("index_bound: 2", encoding="utf-8")
    json_path.write_text('{"index_bound": 2}', encoding="utf-8")
    assert read_model_config(yaml_path) == read_model_config(json_path)
    with pytest.raises(ValidationError):
        read_model_config(yaml_path).index_bound = 4


@pytest.mark.parametrize("chunk_size", [1, 7, 243, 1000])
def test_integer_iterator_is_lexicographic_and_bounded(chunk_size):
    chunks = list(integer_chunks(1, chunk_size))
    assert max(len(part) for part in chunks) <= chunk_size
    expected = np.array(list(product(range(-1, 2), repeat=5)), dtype=np.int32)
    np.testing.assert_array_equal(np.concatenate(chunks), expected)


@pytest.mark.parametrize("bound,chunk", [(-1, 2), (True, 2), (1001, 2), (1, 0), (1, True)])
def test_invalid_enumeration_inputs(bound, chunk):
    with pytest.raises(ValueError):
        list(integer_chunks(bound, chunk))


@pytest.mark.parametrize("crop_kind", ["cylinder", "sphere"])
@pytest.mark.parametrize("policy", ["exclude", "include"])
def test_conservative_expansion_covers_bruteforce_at_phason_extremes(crop_kind, policy):
    cfg = config(
        index_bound=3,
        boundary_policy=policy,
        physical_crop={"kind": crop_kind, "radial_max": 3.0, "z_abs_max": 1.5},
        phason_domain={"min": [-0.4, -0.2], "max": [0.3, 0.25]},
    )
    candidates = generate_candidates(cfg)
    all_n = np.array(list(product(range(-3, 4), repeat=5)), dtype=np.int32)
    physical, internal = project_tuples(all_n, SuperspaceBasis())
    # Independent crop equations, not the production crop helper.
    radius = np.sqrt(np.sum(physical[:, : 2 if crop_kind == "cylinder" else 3] ** 2, axis=1))
    cropped = (radius <= 3.0) & (np.abs(physical[:, 2]) <= 1.5)
    window = AcceptanceWindow()
    candidate_keys = set(map(tuple, candidates.lattice_n))
    for phi in product((-0.4, 0.0, 0.3), (-0.2, 0.0, 0.25)):
        brute = window.classify(internal + phi, boundary_policy=policy).selected & cropped
        expected = set(map(tuple, all_n[brute]))
        actual = evaluate_membership(candidates, phi)
        assert expected <= candidate_keys
        assert set(map(tuple, candidates.lattice_n[actual.selected])) == expected


def test_chunk_size_does_not_change_ids_coordinates_or_membership():
    first = generate_candidates(config(chunk_size=7))
    second = generate_candidates(config(chunk_size=65536))
    for field in ("lattice_n", "physical", "internal", "ids", "hashes"):
        np.testing.assert_array_equal(getattr(first, field), getattr(second, field))
        with pytest.raises(ValueError):
            getattr(first, field).setflags(write=True)
    assert first.diagnostics["duplicate_tuple_count"] == 0
    assert first.diagnostics["hash_collision_count"] == 0
    a, b = evaluate_membership(first, (0.071, -0.031)), evaluate_membership(second, (0.071, -0.031))
    np.testing.assert_array_equal(a.selected_ids, b.selected_ids)
    np.testing.assert_array_equal(a.signed_distance, b.signed_distance)
    np.testing.assert_allclose(
        first.physical, SuperspaceBasis().project(first.lattice_n)[0], atol=1e-12, rtol=0
    )


def test_tuple_identity_and_documented_hash_byte_order():
    lattice = np.array([[-2147483648, 2147483647, -3, 0, 1], [0, 0, 0, 0, 0]], dtype=np.int32)
    ids = stable_ids(lattice)
    assert ids[0] == "-2147483648,2147483647,-3,0,1"
    assert ids[1] == "0,0,0,0,0"
    assert tuple_id(lattice[0]) == ids[0]
    independent_bytes = b"".join(int(v).to_bytes(4, "little", signed=True) for v in lattice[0])
    wanted = int.from_bytes(hashlib.blake2b(independent_bytes, digest_size=8).digest(), "little")
    assert int(lookup_hashes(lattice)[0]) == wanted
    np.testing.assert_array_equal(lookup_hashes(lattice), lookup_hashes(lattice.astype(">i4")))
    assert (
        identity_diagnostics(lattice, np.array([9, 9], dtype=np.uint64))["hash_collision_count"]
        == 1
    )


@pytest.mark.parametrize(
    "lattice",
    [
        np.zeros((2, 5)),
        np.zeros((2, 4), dtype=int),
        np.array([[2**31, 0, 0, 0, 0]]),
        np.array([[-(2**31) - 1, 0, 0, 0, 0]]),
    ],
)
def test_invalid_lattice_id_inputs(lattice):
    with pytest.raises(ValueError):
        lattice_tuples(lattice)


def test_stable_ids_survive_larger_patch_and_display_scale():
    small = generate_candidates(config(index_bound=1))
    large = generate_candidates(config(index_bound=2, physical_scale={"xy": 200.0, "z": 5.0}))
    mapping = dict(zip(map(tuple, large.lattice_n), large.ids, strict=True))
    for n, key in zip(small.lattice_n, small.ids, strict=True):
        assert mapping[tuple(n)] == key


def test_membership_sign_boundary_masks_selected_coordinates_and_batch():
    cfg = config(phason_domain={"min": [-2.0, -2.0], "max": [2.0, 2.0]})
    candidates = generate_candidates(cfg)
    vertex = AcceptanceWindow().vertices[0]
    state = evaluate_membership(candidates, vertex)
    zero = np.all(candidates.lattice_n == 0, axis=1)
    assert state.boundary[zero].item() and not state.selected[zero].item()
    included = evaluate_membership(
        replace(
            candidates,
            config=config(
                boundary_policy="include", phason_domain={"min": [-2, -2], "max": [2, 2]}
            ),
        ),
        vertex,
    )
    assert included.selected[zero].item()
    state = evaluate_membership(candidates, (0.19, -0.11))
    direct = AcceptanceWindow().classify(candidates.internal + state.phi)
    np.testing.assert_array_equal(state.selected, direct.selected)
    np.testing.assert_array_equal(
        state.selected_internal, (candidates.internal + state.phi)[state.selected]
    )
    np.testing.assert_array_equal(state.selected_physical, candidates.physical[state.selected])
    negative = AcceptanceWindow().classify(candidates.internal - state.phi)
    assert np.any(state.selected != negative.selected)
    states = list(membership_batch(candidates, [(0.0, 0.0), (0.19, -0.11)]))
    changes = compare_membership(candidates, states[0], states[1])
    reverse = compare_membership(candidates, states[1], states[0])
    assert changes["entered_ids"] == reverse["exited_ids"]
    assert changes["exited_ids"] == reverse["entered_ids"]
    assert len(state.selected_ids) == np.count_nonzero(state.selected)


@pytest.mark.parametrize("phi", [(0.251, 0), (float("inf"), 0), [0, 0, 0], [[0, 0]]])
def test_membership_rejects_outside_domain_and_invalid_shapes(phi):
    with pytest.raises(ValueError):
        evaluate_membership(generate_candidates(config(index_bound=0)), phi)


def test_progress_cancellation_and_memory_limit():
    updates = []
    patch = generate_candidates(config(), progress=updates.append)
    assert updates[-1]["processed"] == 5**5
    assert updates[-1]["candidate_count"] == len(patch.ids)
    assert patch.diagnostics["max_chunk_rows"] <= 113
    assert patch.diagnostics["retained_array_bytes"] == sum(
        getattr(patch, field).nbytes
        for field in ("lattice_n", "physical", "internal", "ids", "hashes")
    )
    with pytest.raises(GenerationCancelled):
        generate_candidates(config(), cancelled=lambda: True)
    with pytest.raises(ValueError, match="memory limit"):
        generate_candidates(config(max_candidates=1))


def test_empty_candidates_and_membership():
    cfg = config(
        index_bound=0, initial_phi=(10, 10), phason_domain={"min": [10, 10], "max": [10, 10]}
    )
    # This domain permits no membership, but the conservative disk may retain origin.
    patch = generate_candidates(cfg)
    state = evaluate_membership(patch, (10, 10))
    assert state.selected_ids.shape == (0,)
    assert state.selected_physical.shape == (0, 3)
    empty = replace(
        patch,
        lattice_n=patch.lattice_n[:0],
        physical=patch.physical[:0],
        internal=patch.internal[:0],
        ids=patch.ids[:0],
        hashes=patch.hashes[:0],
    )
    assert evaluate_membership(empty, (10, 10)).selected.shape == (0,)
