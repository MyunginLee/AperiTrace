"""Small linked Matplotlib application, also runnable with --capture headlessly.

Select points in either projection; arrow keys change lambda. E exports the
current linked scientific state. A saved canvas is an application rendering,
not a Unity screenshot. Uncertainty is visible and never suppressed.
"""

import argparse
import json
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np

from qc5d.exploration import bundled_example


class LinkedViewer:
    def __init__(
        self, app, path, parameter=0.075, focus=(0, -1, 1, 0, 1), export=Path("linked_state.json")
    ):
        self.app, self.path, self.parameter, self.focus, self.export = (
            app,
            path,
            parameter,
            tuple(focus),
            export,
        )
        self.figure = plt.figure(figsize=(12, 5), layout="constrained")
        grid = self.figure.add_gridspec(1, 3)
        self.physical = self.figure.add_subplot(grid[0], projection="3d")
        self.internal = self.figure.add_subplot(grid[1])
        self.reciprocal = self.figure.add_subplot(grid[2])
        self.figure.canvas.mpl_connect("key_press_event", self.on_key)
        self.figure.canvas.mpl_connect("pick_event", self.on_pick)
        self.draw()

    def draw(self):
        for axes in (self.physical, self.internal, self.reciprocal):
            axes.clear()
        data = self.app.candidates
        mask, status = self.path.state(self.parameter)
        result = self.path.query(self.app.cache, [self.parameter])
        row = int(np.flatnonzero(np.all(data.lattice_n == self.focus, axis=1))[0])
        colors = np.where(mask, "#087f8c", "#d0d0d0")
        self.physical.scatter(*data.physical.T, s=8, c=colors, picker=5)
        self.physical.scatter(*data.physical[row], s=65, c="#cc3311", marker="x")
        self.physical.set(xlabel="x", ylabel="y", zlabel="z", title="Physical space: same exact ID")
        uv = data.internal + self.path.phi(self.parameter)
        self.internal.scatter(*uv.T, s=8, c=colors, picker=5)
        self.internal.scatter(*uv[row], s=65, c="#cc3311", marker="x")
        vertices = np.r_[self.app.window.vertices, self.app.window.vertices[:1]]
        self.internal.plot(*vertices.T, color="black", lw=1)
        self.internal.set(
            xlabel="u + phi, component 0",
            ylabel="component 1",
            title="Internal space: fixed window",
        )
        self.internal.set_aspect("equal")
        self.reciprocal.scatter(
            self.app.cache.q[:, 0],
            self.app.cache.q[:, 1],
            c=np.log10(np.maximum(result.normalized_intensity[0], 1e-8)),
            s=28,
            cmap="magma",
            vmin=-8,
            vmax=0,
        )
        self.reciprocal.set(
            xlabel="qx (angular)", ylabel="qy", title="Finite diffraction: log10 I, qz = 0"
        )
        self.reciprocal.set_aspect("equal")
        self.figure.suptitle(
            f"XR Superspace — Python linked inspector\n"
            f"n={self.focus} | lambda={self.parameter:.6f} | "
            f"N={result.counts[0]} | {status.upper()}\n"
            "←/→ query · click point to focus · E export",
            fontsize=12,
        )
        self.figure.canvas.draw_idle()

    def on_key(self, event):
        if event.key in ("left", "right"):
            self.parameter = float(
                np.clip(self.parameter + (0.005 if event.key == "right" else -0.005), 0, 1)
            )
            self.draw()
        elif event.key == "e":
            payload = self.app.export_state(self.path, self.parameter, self.focus)
            with self.export.open("x", encoding="utf-8") as out:
                json.dump(payload, out, indent=2, allow_nan=False)

    def on_pick(self, event):
        if event.artist.axes in (self.physical, self.internal) and len(event.ind):
            self.focus = tuple(int(x) for x in self.app.candidates.lattice_n[event.ind[0]])
            self.draw()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--capture", type=Path)
    parser.add_argument("--export", type=Path, default=Path("linked_state.json"))
    args = parser.parse_args(argv)
    app = bundled_example()
    viewer = LinkedViewer(app, app.path((-0.61, 0.43), (0.67, -0.31)), export=args.export)
    if args.capture:
        viewer.figure.savefig(args.capture, dpi=200)
        viewer.on_key(type("Key", (), {"key": "e"})())
    else:
        plt.show()


if __name__ == "__main__":
    main()
