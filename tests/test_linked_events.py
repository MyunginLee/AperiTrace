"""Explicit endpoint, concurrency, export and numerical-status contract fixtures."""

import copy
import importlib.util
import sys
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest

from qc5d.events import LinearPath, PhaseCache, clip_intervals
from qc5d.exploration import Exploration, bundled_example, recompute_state

SQUARE = [[-1.0, -1.0], [1.0, -1.0], [1.0, 1.0], [-1.0, 1.0]]


@pytest.mark.parametrize(
    "start,end,expected",
    [
        ([0, 0], [0.5, 0], [True, True]),
        ([0, 0], [2, 0], [True, False]),
        ([-2, 0], [0, 0], [False, True]),
        ([-1, 0], [0, 0], [False, True]),
        ([0, 0], [1, 0], [True, False]),
        ([0, 0], [0, 0], [True, True]),
        ([0, 1], [0, 1], [False, False]),
        ([0, 2], [0, 2], [False, False]),
        ([0, 2], [2, 0], [False, False]),
    ],
)
def test_domain_endpoints_and_tangency(start, end, expected):
    intervals = clip_intervals(SQUARE, [[0, 0]], start, end, 0.0)
    assert [bool(intervals.selected(t)[0]) for t in (0.0, 1.0)] == expected


def test_atomic_same_root_entry_exit_and_repeated_queries():
    path = LinearPath(SQUARE, [[1, 0], [-1, 0]], [-1, 0], [1, 0], 0.0)
    cache = PhaseCache.build([[0, 0, 0], [1, 0, 0]], [[0, 0, 0]])
    result = path.query(cache, [0.0, 0.25, 0.5, 0.5, 0.75, 1.0])
    assert result.counts.tolist() == [1, 1, 0, 0, 1, 1]
    assert result.status[2] == "uncertain"
    assert result.amplitudes[2, 0] == 0
    for i, t in enumerate(result.parameters):
        assert result.counts[i] == path.reference_mask(t).sum()


@pytest.mark.parametrize("times", [[], [1, 0.5], [0.1, 0.3, 0.2], [-1], [2], [np.nan]])
def test_query_order_range_rejection(times):
    path = LinearPath(SQUARE, [[0, 0]], [0, 0], [0, 0], 0.0)
    cache = PhaseCache.build([[0, 0, 0]], [[0, 0, 0]])
    with pytest.raises(ValueError):
        path.query(cache, times)


def test_empty_set_and_empty_q_explicit():
    path = LinearPath(SQUARE, np.empty((0, 2)), [0, 0], [0, 0], 0.0)
    cache = PhaseCache.build(np.empty((0, 3)), np.empty((0, 3)))
    result = path.query(cache, [0.0, 1.0])
    assert result.amplitudes.shape == (2, 0) and result.counts.tolist() == [0, 0]
    assert result.normalized_intensity.shape == (2, 0)


@pytest.fixture(scope="module")
def app():
    return bundled_example()


def test_stable_clip_preserves_original_arithmetic(app):
    source = Path(__file__).resolve().parents[1] / "experiments/event_audit/model.py"
    spec = importlib.util.spec_from_file_location("old_event_contract", source)
    old = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = old
    spec.loader.exec_module(old)
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    expected = old.clip_intervals(
        app.window.vertices, app.candidates.internal, path.start, path.end, app.epsilon
    )
    for key in ("lower", "upper", "possible"):
        np.testing.assert_array_equal(getattr(path.intervals, key), getattr(expected, key))


def test_export_recomputes_and_rejects_status_loss(app):
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    payload = app.export_state(path, 0.075, (0, -1, 1, 0, 1))
    assert recompute_state(payload)["count_normalized_amplitude_error"] < 1e-12
    bad = copy.deepcopy(payload)
    del bad["status"]
    with pytest.raises(ValueError, match="status"):
        recompute_state(bad)
    bad = copy.deepcopy(payload)
    bad["candidate_tuples"][0] = bad["candidate_tuples"][1]
    with pytest.raises(ValueError, match="duplicate"):
        recompute_state(bad)


def test_q_and_path_invalidation(app):
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    newpath = app.path((0, 0), (0.1, 0.2))
    assert not np.array_equal(path.intervals.lower, newpath.intervals.lower)
    assert app.cache is app.cache
    with pytest.raises(ValueError):
        app.path((-1, 0), (0, 0))
    for q in ([[np.nan, 0, 0]], [[1, 2]], [[1 + 1j, 0, 0]]):
        with pytest.raises(ValueError):
            Exploration(app.candidates, q)
    with pytest.raises(ValueError):
        app.cache.q.flags.writeable = True


def test_sensitive_query_uses_reference_and_does_not_contaminate_replay(app):
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    roots = path.intervals.critical_parameters()
    times = np.unique(np.r_[roots, np.linspace(0, 1, 33)])
    result = path.query(app.cache, times)
    assert np.count_nonzero(result.status == "uncertain") > 0
    for i, t in enumerate(times):
        ref = path.reference_mask(t)
        assert result.counts[i] == ref.sum()
        expected = app.cache.values[ref].sum(axis=0)
        assert np.max(np.abs(result.amplitudes[i] - expected)) / max(1, ref.sum()) < 1e-12


@pytest.mark.parametrize("field", ["physical", "internal", "F_real", "I_normalized"])
def test_export_rejects_changed_scientific_values(app, field):
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    payload = app.export_state(path, 0.075, (0, -1, 1, 0, 1))
    if field in ("physical", "internal"):
        payload[field][0][0] += 0.01
    else:
        payload[field][0] += 0.01
    with pytest.raises(ValueError):
        recompute_state(payload)


def test_exploration_rejects_coordinates_not_generated_from_tuples(app):
    physical = app.candidates.physical.copy()
    physical[0, 0] += 0.01
    broken = replace(app.candidates, physical=physical)
    with pytest.raises(ValueError, match="canonical tuple"):
        Exploration(broken, app.cache.q)


@pytest.mark.parametrize("axes_name", ["physical", "internal"])
def test_viewer_linked_pick_keys_status_and_non_overwriting_export(app, tmp_path, axes_name):
    import matplotlib.pyplot as plt

    from qc5d.linked_viewer import LinkedViewer

    path = app.path((-0.61, 0.43), (0.67, -0.31))
    viewer = LinkedViewer(app, path, export=tmp_path / "state.json")
    try:
        axes = getattr(viewer, axes_name)
        viewer.on_pick(SimpleNamespace(artist=axes.collections[0], ind=[5]))
        assert viewer.focus == tuple(app.candidates.lattice_n[5])
        viewer.on_key(SimpleNamespace(key="right"))
        assert viewer.parameter == pytest.approx(0.08)
        viewer.parameter = float(path.intervals.critical_parameters()[1])
        viewer.draw()
        assert "UNCERTAIN" in viewer.figure._suptitle.get_text()
        viewer.on_key(SimpleNamespace(key="e"))
        import json

        check = recompute_state(json.loads(viewer.export.read_text(encoding="utf-8")))
        assert check["status"] == "uncertain"
        original = viewer.export.read_bytes()
        with pytest.raises(FileExistsError):
            viewer.on_key(SimpleNamespace(key="e"))
        assert viewer.export.read_bytes() == original
    finally:
        plt.close(viewer.figure)
