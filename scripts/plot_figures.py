"""Generate scientific figures from retained numerical inputs and recorded panels.

Callers supply the input/output directories. Export PDF, SVG, 300-dpi PNG and
publication-size previews to the requested output directories.
Recorded screen pixels are only cropped and placed, never retouched or recolored.
"""

import argparse
import hashlib
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
from matplotlib.patches import FancyArrowPatch, FancyBboxPatch
from matplotlib.ticker import NullFormatter, FixedLocator
import numpy as np
from PIL import Image

from scientific_figure_style import (
    COLORS as C, METHODS, METHOD_NAMES, MARKERS, LINESTYLES,
    WORKFLOW_STYLE, apply_style, light_grid, panel,
)

ROOT = Path(__file__).resolve().parents[1]
FIGURES = ("figure1_workflow", "figure2_application", "figure3_diffraction",
           "figure4_matched", "supplement_uncertain")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Exporter:
    def __init__(self, paper, previews, formats):
        self.paper, self.previews, self.formats = paper, previews, formats
        paper.mkdir(parents=True, exist_ok=True)
        previews.mkdir(parents=True, exist_ok=True)
        self.records = {}

    def save(self, fig, name):
        fig.canvas.draw()
        renderer = fig.canvas.get_renderer()
        bounds = fig.bbox
        outside = []
        unused_tick_labels = set()
        for ax in fig.axes:
            for axis, limits in ((ax.xaxis, ax.get_xlim()), (ax.yaxis, ax.get_ylim())):
                low, high = sorted(limits)
                for tick in axis.majorTicks + axis.minorTicks:
                    if not low <= tick.get_loc() <= high:
                        unused_tick_labels.update((id(tick.label1), id(tick.label2)))
        for artist in fig.findobj(match=matplotlib.text.Text):
            if not artist.get_visible() or not artist.get_text() or id(artist) in unused_tick_labels:
                continue
            box = artist.get_window_extent(renderer)
            # Unused automatic ticks can exist outside their visible view limits.
            if (box.x0 < -2 or box.y0 < -2
                    or box.x1 > bounds.width + 2 or box.y1 > bounds.height + 2):
                outside.append(artist.get_text())
        if outside:
            raise AssertionError((name, "text outside figure", outside))
        for suffix in self.formats:
            directory = self.paper if suffix == "pdf" else self.previews
            fig.savefig(directory / f"{name}.{suffix}", dpi=300, bbox_inches=None)
        fig.savefig(self.previews / f"{name}_publication_size.png", dpi=110)
        self.records[name] = {"inches": list(fig.get_size_inches()),
                              "outside_text": outside, "formats": list(self.formats)}
        plt.close(fig)


def workflow(examples, export):
    """Literal architecture with frozen, same-state scientific exemplars.

    All scientific panels are derived from the included canonical state export.
    """
    state = read_json(examples / "example_b_inside.json")
    data = np.load(examples / "example_b_arrays.npz", allow_pickle=False)
    tuples = np.asarray(state["candidate_tuples"], dtype=int)
    focus = np.asarray(state["focus"], dtype=int)
    row = np.flatnonzero(np.all(tuples == focus, axis=1))
    assert len(row) == 1
    row = int(row[0])
    selected = np.isin(state["candidate_ids"], state["selected_ids"])
    assert selected.sum() == state["N"] == 126 and selected[row]
    assert data["times"][1] == state["parameter"]
    np.testing.assert_allclose(data["maps"][1],state["I_normalized"],rtol=0,atol=1e-15)
    q = np.asarray(state["q"])
    qx,qy = np.meshgrid(np.linspace(-8,8,11),np.linspace(-8,8,11))
    np.testing.assert_allclose(q,np.c_[qx.ravel(),qy.ravel(),np.zeros(121)],rtol=0,atol=1e-14)
    physical = np.asarray(state["physical"])
    internal = np.asarray(state["internal"]) + state["phi"]
    window = np.asarray(state["window"])
    window = np.r_[window, window[:1]]
    S = WORKFLOW_STYLE
    width, height = 7.2, 4.1
    fig = plt.figure(figsize=(width, height))
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set(xlim=(0, width), ylim=(0, height)); ax.axis("off")

    def text(x, y, label, size=S["detail"], bold=False, **kwargs):
        return ax.text(x, y, label, fontsize=size,
                       weight="bold" if bold else "normal", va="center", **kwargs)

    def frame(x, y, w, h, dotted=False):
        ax.add_patch(FancyBboxPatch((x,y), w,h,
            boxstyle="round,pad=0,rounding_size=.04",
            linewidth=S["group_width"] if dotted else S["outline_width"],
            linestyle=S["group_dash"] if dotted else "-",
            edgecolor=S["outline"], facecolor="none" if dotted else "white"))

    def heading(x, y, letter, title):
        text(x,y,letter,size=11,bold=True)
        text(x+.23,y,title,size=S["heading"],bold=True)

    def arrow(a, b, size=10):
        ax.add_patch(FancyArrowPatch(a,b,arrowstyle="-|>",mutation_scale=size,
            linewidth=1.15,color=S["flow"],shrinkA=0,shrinkB=0))

    def inset(x,y,w,h):
        return fig.add_axes([x/width,y/height,w/width,h/height])

    # Authority is a boundary label, not an extra processing stage.
    text(2.63,4.01,"Python scientific core",size=10.1,bold=True,ha="center")
    ax.plot([.07,5.21],[3.94,3.94],color=S["outline"],lw=.8)
    frame(.06,3.08,3.25,.78,dotted=True)
    frame(.06,.06,3.25,2.84,dotted=True)
    frame(3.53,.06,1.68,3.80,dotted=True)
    frame(5.46,.06,1.67,3.80,dotted=True)
    heading(.17,3.64,"A","Model definition")
    heading(.17,2.73,"B","Linked inspection")
    heading(3.65,3.64,"C","Finite output")
    heading(5.57,3.64,"D","Output consumers")
    for x,w,label in ((.18,1.39,"Basis + window"),(1.74,1.45,"Crop + offset path")):
        frame(x,3.17,w,.30)
        text(x+w/2,3.32,label,size=8.9,bold=True,ha="center")
    arrow((1.685,3.08),(1.685,2.90),size=11)
    frame(.30,2.41,2.77,.25)
    tuple_label = r"$\mathbf{n}= (" + ",".join(map(str,state["focus"])) + r")$"
    text(1.685,2.535,"Shared tuple  " + tuple_label,size=9.2,ha="center")
    text(.97,2.29,"Physical xy",size=9,bold=True,ha="center")
    text(2.55,2.29,"Internal window",size=9,bold=True,ha="center")
    p = inset(.48,1.28,1.03,.93)
    p.scatter(*physical[selected,:2].T,s=7,c=C["window"],alpha=.65,edgecolors="none")
    p.scatter(*physical[row,:2],s=36,marker="D",c=C["inside"],edgecolors="white",lw=.5,zorder=5)
    p.set(xlim=(-2.1,2.1),ylim=(-2.1,2.1),xticks=[-2,0,2],yticks=[-2,0,2],
          xlabel=r"$x_0$",ylabel=r"$x_1$")
    p.set_aspect("equal",adjustable="box"); p.tick_params(labelsize=7.5,pad=1.5)
    p.xaxis.labelpad=0; p.yaxis.labelpad=0
    u = inset(2.04,1.28,1.03,.93)
    u.fill(*window.T,color=C["accent_pale"],zorder=0)
    u.scatter(*internal[~selected].T,s=3,c=C["grid"],edgecolors="none",zorder=1)
    u.scatter(*internal[selected].T,s=7,c=C["window"],alpha=.65,edgecolors="none",zorder=2)
    u.plot(*window.T,c=S["outline"],lw=1,zorder=3)
    u.scatter(*internal[row],s=36,marker="D",c=C["inside"],edgecolors="white",lw=.5,zorder=5)
    u.set(xlim=(-1.32,1.32),ylim=(-1.32,1.32),xticks=[-1,0,1],yticks=[-1,0,1],
          xlabel=r"$u_0+\phi_0$",ylabel=r"$u_1+\phi_1$")
    u.set_aspect("equal",adjustable="box"); u.tick_params(labelsize=7.5,pad=1.5)
    u.xaxis.labelpad=0; u.yaxis.labelpad=0
    text(.35,.80,"Membership history",size=9,bold=True)
    h = inset(.48,.38,2.49,.30)
    lower,upper = float(data["lower"]),float(data["upper"])
    h.axvspan(lower,upper,color=C["accent_pale"],zorder=0)
    h.step(data["trace_times"],data["focus_selected"].astype(int),where="post",c=S["outline"],lw=1.25)
    uncertain = data["status"] == "uncertain"
    h.scatter(data["trace_times"][uncertain],data["focus_selected"][uncertain].astype(int),
              s=20,facecolors="white",edgecolors=S["outline"],lw=.9,zorder=5)
    for time,value,key in zip(data["times"],(0,1,0),("before","inside","after"),strict=True):
        h.scatter(time,value,s=21,c=C[key],edgecolors="white",lw=.4,zorder=6)
    h.set(xlim=(.06,.10),ylim=(-.17,1.2),xticks=[.06,.08,.10],yticks=[0,1],
          yticklabels=["Out","In"],xlabel=r"Path parameter $\lambda$")
    h.tick_params(labelsize=7.5,pad=1.5); h.xaxis.labelpad=1
    # This arrow links the common selected state to its finite diffraction.
    arrow((3.31,2.44),(3.53,2.44),size=12)
    text(4.37,3.40,"Finite diffraction",size=9,bold=True,ha="center")
    m = inset(3.92,2.26,1.00,1.00)
    image = m.pcolormesh(np.linspace(-8.8,8.8,12),np.linspace(-8.8,8.8,12),
            np.log10(np.maximum(data["maps"][1].reshape(11,11),1e-8)),
            vmin=-8,vmax=0,cmap="magma",shading="flat",rasterized=False)
    m.set(xlim=(-8.8,8.8),ylim=(-8.8,8.8),xticks=[-8,0,8],yticks=[-8,0,8],
          xlabel=r"$q_x$",ylabel=r"$q_y$")
    m.set_aspect("equal"); m.tick_params(labelsize=7.5,pad=1.5)
    m.xaxis.labelpad=0; m.yaxis.labelpad=0
    cbax = inset(3.82,1.79,1.10,.075)
    cb = fig.colorbar(image,cax=cbax,orientation="horizontal",ticks=[-8,0])
    cb.solids.set_rasterized(False); cb.ax.tick_params(labelsize=7.5,pad=1,length=2)
    text(4.37,1.49,r"$\log_{10}(|F|^2/N^2)$",size=8.3,ha="center")
    arrow((4.37,1.40),(4.37,1.30),size=9)
    frame(3.65,.20,1.44,1.10)
    text(4.37,1.10,"Scientific state",size=S["card_title"],bold=True,ha="center")
    for y,label in ((.85,"Tuples + coordinates"),(.66,"Window + offset + q"),
                    (.47,"F + normalization"),(.28,"Numerical status")):
        text(4.37,y,label,size=8.25,ha="center")
    # Independent leaves, both fed by scientific export. No Unity dependency.
    frame(5.59,2.30,1.41,1.06)
    text(6.295,3.13,"Headless\nrecomputation",size=S["card_title"],bold=True,ha="center")
    for y,label in ((2.85,"Reconstruction"),(2.66,"Fresh finite sums"),(2.47,"State comparison")):
        text(6.295,y,label,ha="center")
    frame(5.59,.27,1.41,1.34)
    text(6.295,1.37,"Optional Unity",size=S["card_title"],bold=True,ha="center")
    text(6.295,1.12,"C# double-precision\nchecks",size=8.1,ha="center")
    ax.plot([5.70,6.89],[.96,.96],color=C["grid"],lw=.8)
    text(6.295,.75,"GPU single-precision\npreview",size=8.1,ha="center")
    text(6.295,.43,"Visualization only",size=8.1,color=C["muted"],ha="center")
    ax.plot([5.09,5.33,5.33],[.75,.75,2.83],color=S["flow"],lw=1.15)
    arrow((5.33,2.83),(5.59,2.83),size=10)
    arrow((5.33,.94),(5.59,.94),size=10)
    export.save(fig,"figure1_workflow")
    return {"focus_tuple":state["focus"],"selected_count":state["N"],
            "candidate_count":len(tuples),"parameter":state["parameter"],
            "status":state["status"],"trace_queries":len(data["trace_times"]),
            "uncertain_queries":int(uncertain.sum()),"reciprocal_samples":len(q),
            "map_matches_exported_intensity":True,"q_grid_verified":True,
            "unity_leaf":"architecture only; not a capture of this state",
            "source":"included canonical state export"}


def application_capture(examples, export):
    """Vector replot of fixed Python state; pixel-exact compact Unity crop."""
    state = read_json(examples / "example_a_after.json")
    tuples = np.asarray(state["candidate_tuples"], dtype=int)
    physical = np.asarray(state["physical"])
    internal = np.asarray(state["internal"]) + state["phi"]
    selected = np.isin(state["candidate_ids"], state["selected_ids"])
    row = int(np.flatnonzero(np.all(tuples == state["focus"], axis=1))[0])
    q = np.asarray(state["q"])
    intensity = np.asarray(state["I_normalized"])
    assert len(tuples) == 31 and selected.sum() == state["N"] == 15
    assert len(q) == len(intensity) == 381
    assert np.max(np.abs(q[:, 2])) < 1e-12
    colors = np.where(selected, C["inside"], "#9BA5AE")
    fig = plt.figure(figsize=(7.2, 5.2))
    ax = fig.add_axes([.01, .595, .27, .33], projection="3d")
    physical_points = ax.scatter(*physical.T, s=20, c=colors, depthshade=False,
                                 edgecolors="white", linewidths=.3)
    ax.scatter(*physical[row], s=48, c="#C43B21", marker="x", linewidths=1.6)
    ax.view_init(elev=26, azim=-60)
    for axis in (ax.xaxis, ax.yaxis, ax.zaxis):
        axis.set_major_locator(FixedLocator([-1, 1]))
        axis.pane.set_facecolor((.98, .985, .99, 1))
        axis._axinfo["grid"].update(color=C["grid"], linewidth=.55)
    ax.set(xlim=(-1, 1), ylim=(-1, 1), zlim=(-1, 1),
           xlabel="x", ylabel="y", zlabel="z")
    ax.tick_params(labelsize=8, pad=0)
    ax.xaxis.labelpad = ax.yaxis.labelpad = ax.zaxis.labelpad = 1
    physical_axis = ax
    ax = fig.add_axes([.415, .595, .23, .33])
    internal_points = ax.scatter(*internal.T, s=20, c=colors,
                                 edgecolors="white", linewidths=.3)
    ax.scatter(*internal[row], s=48, c="#C43B21", marker="x", linewidths=1.6)
    vertices = np.asarray(state["window"])
    vertices = np.r_[vertices, vertices[:1]]
    ax.plot(*vertices.T, color=C["window"], lw=1)
    ax.set(xlim=(-1.4, 1.4), ylim=(-1.4, 1.4), xticks=[-1, 0, 1], yticks=[-1, 0, 1],
           xlabel=r"$u_0+\phi_0$", ylabel=r"$u_1+\phi_1$")
    ax.set_aspect("equal"); ax.tick_params(labelsize=8, pad=2)
    internal_axis = ax
    ax = fig.add_axes([.775, .595, .205, .33])
    reciprocal_points = ax.scatter(q[:, 0], q[:, 1],
                                   c=np.log10(np.maximum(intensity, 1e-8)),
                                   s=11, cmap="magma", vmin=-8, vmax=0,
                                   edgecolors=C["muted"], linewidths=.15)
    ax.set(xticks=[-20, 0, 20], yticks=[-20, 0, 20],
           xlabel="qx (angular)", ylabel="qy")
    ax.set_aspect("equal"); ax.tick_params(labelsize=8, pad=2)
    fig.text(.02, .982, "A", fontsize=11, weight="bold", va="top")
    fig.text(.105, .982, "Python state replot", fontsize=9.5, va="top")
    for x, title, size in ((.145, "Physical space", 9),
                           (.53, "Internal window", 9),
                           (.875, r"Finite diffraction: $\log_{10}I_{\mathrm{norm}}$", 8)):
        fig.text(x, .935, title, fontsize=size, ha="center", va="top")
    # Only a uniform, empty band is omitted. All retained camera pixels are
    # uint8, in source order, with their original RGB values and aspect ratio.
    unity = np.asarray(Image.open(examples / "unity_after_panels.png"))
    assert sha(examples / "unity_after_panels.png") == read_json(examples / "unity_after_sidecar.json")["image_sha256"]
    blank = unity[30:80, :]
    assert np.all(blank == blank[0, 0]), "Do not remove nonblank camera evidence"
    compact = np.concatenate([unity[:30, :], unity[80:, :]])
    ax = fig.add_axes([.02, .035, .96, .46])
    ax.imshow(compact, interpolation="none"); ax.axis("off")
    panel(ax, "B", "Recorded Unity desktop", y=1.025)
    fig.canvas.draw()
    renderer = fig.canvas.get_renderer()
    z_box = physical_axis.zaxis.label.get_window_extent(renderer)
    y_box = internal_axis.yaxis.label.get_window_extent(renderer)
    assert not z_box.overlaps(y_box), "Physical/internal axis labels overlap"
    assert y_box.x0 - z_box.x1 > 8, "Provide a clear physical/internal label gutter"
    np.testing.assert_array_equal(np.column_stack(physical_points._offsets3d), physical)
    np.testing.assert_array_equal(internal_points.get_offsets(), internal)
    np.testing.assert_array_equal(reciprocal_points.get_offsets(), q[:, :2])
    np.testing.assert_array_equal(reciprocal_points.get_array(), np.log10(np.maximum(intensity, 1e-8)))
    export.save(fig, "figure2_application")
    return {"python_panel": "vector replot from unchanged validated export",
            "candidate_count": len(tuples), "selected_count": int(selected.sum()),
            "q_samples": len(q), "focus": state["focus"],
            "unity_crops": [[500, 430, 1400, 460], [500, 510, 1400, 790]],
            "omitted_blank_band": [500, 460, 1400, 510],
            "omitted_band_uniform": True, "retained_unity_pixels_unmodified": True,
            "physical_internal_labels_overlap": False,
            "plotted_coordinates_and_intensities_match_export": True}


def captures(examples, export):
    capture_layout = application_capture(examples, export)
    status = np.asarray(Image.open(examples / "uncertain_status.png"))
    panels = np.asarray(Image.open(examples / "uncertain_panels.png"))
    fig = plt.figure(figsize=(7.2, 3.5))
    ax = fig.add_axes([.02, .80, .96, .095])
    ax.imshow(status, interpolation="none"); ax.axis("off")
    panel(ax, "A", "Boundary status", y=1.3)
    ax = fig.add_axes([.02, .055, .96, .64])
    ax.imshow(panels, interpolation="none"); ax.axis("off")
    panel(ax, "B", "Linked inspector", y=1.025)
    export.save(fig, "supplement_uncertain")
    return capture_layout


def diffraction(examples, export):
    with matplotlib.rc_context({"font.size":10,"axes.labelsize":10,
                                "xtick.labelsize":9,"ytick.labelsize":9,"legend.fontsize":9}):
        return _diffraction(examples, export)


def _diffraction(examples, export):
    data = np.load(examples / "example_b_arrays.npz", allow_pickle=False)
    payloads = [read_json(examples / f"example_b_{n}.json") for n in ("before", "inside", "after")]
    fig = plt.figure(figsize=(7.2, 4.7))
    # Reserve the finite negative tick-label width on Linux font renderers too.
    xpositions = [.115, .39, .68]
    axes = [fig.add_axes([x, .625, .225, .26]) for x in xpositions]
    ax = axes[0]; row = payloads[0]["candidate_tuples"].index(payloads[0]["focus"])
    verts = np.asarray(payloads[0]["window"]); verts = np.r_[verts, verts[:1]]
    points = np.asarray([np.asarray(p["internal"][row]) + p["phi"] for p in payloads])
    ax.plot(*verts.T, color=C["window"], lw=1.1)
    ax.plot(*points.T, color=C["muted"], lw=1)
    for point, state, offset in zip(points, ("before", "inside", "after"), ((-3, 8), (4, 10), (-24, -12)), strict=True):
        ax.scatter(*point, s=26, c=C[state], edgecolors="white", linewidths=.5, zorder=4)
        ax.annotate("Interior" if state=="inside" else state.capitalize(), point,
                    xytext=offset, textcoords="offset points", fontsize=9, color=C[state])
    xlo, xhi = points[:, 0].min()-.009, points[:, 0].max()+.013
    ymid = (points[:, 1].min()+points[:, 1].max())/2
    yspan = (xhi-xlo)*(.26*4.7)/(.225*7.2)
    ax.set(xlim=(xlo,xhi), ylim=(ymid-yspan/2,ymid+yspan/2),
           xlabel=r"$u_0+\phi_0$", ylabel=r"$u_1+\phi_1$")
    ax.set_aspect("equal", adjustable="box"); ax.ticklabel_format(useOffset=False)
    ax.xaxis.set_major_locator(FixedLocator([-.35, -.33, -.31])); ax.yaxis.set_major_locator(FixedLocator([-.99, -.97, -.95]))
    panel(ax, "A", "Window crossing")
    t=data["trace_times"]; states=("before", "inside", "after")
    ax=axes[1]
    ax.axvspan(float(data["lower"]), float(data["upper"]), color=C["accent_pale"], zorder=0)
    ax.step(t, data["focus_selected"].astype(int), where="post", color=C["ink"])
    for time, val, state in zip(data["times"], (0,1,0), states, strict=True):
        ax.scatter(time,val,s=25,c=C[state],edgecolors="white",lw=.5,zorder=4)
    ax.set(xlim=(.06,.10),ylim=(-.15,1.2),xlabel=r"Path parameter $\lambda$",
           ylabel="Membership",yticks=[0,1],yticklabels=["Out","In"])
    ax.set_xticks([.06,.08,.10]); panel(ax,"B","Selection history")
    ax=axes[2]
    ax.axvspan(float(data["lower"]),float(data["upper"]),color=C["accent_pale"],zorder=0)
    indices=[int(np.argmin(abs(t-time))) for time in data["times"]]
    for j,style in enumerate(("-","--")):
        color=C[f"q{j}"]
        ax.step(t,data["intensity"][:,j],where="post",c=color,ls=style,label=rf"$q_z={j}$")
        for i,state in zip(indices,states,strict=True):
            ax.scatter(t[i],data["intensity"][i,j],s=20,c=C[state],edgecolors=color,lw=.55,zorder=4)
        flags=data["status"]=="uncertain"
        ax.scatter(t[flags],data["intensity"][flags,j],s=27,facecolors="white",edgecolors=color,lw=.9,zorder=5)
    ax.set(xlim=(.06,.10),ylim=(0,.026),xlabel=r"Path parameter $\lambda$",ylabel=r"$|F(\mathbf{q})|^2/N^2$")
    ax.set_xticks([.06,.08,.10]); ax.set_yticks([0,.01,.02])
    ax.legend(loc="lower left",ncols=2,handlelength=1.6,columnspacing=.9,borderaxespad=.2)
    panel(ax,"C","Finite intensity")
    for j,(p,state) in enumerate(zip(payloads,states,strict=True)):
        ax=fig.add_axes([xpositions[j],.12,.225,.30])
        q=np.asarray(p["q"]).reshape(11,11,3)
        values=np.log10(np.maximum(np.asarray(p["I_normalized"]).reshape(11,11),1e-8))
        np.testing.assert_array_equal(np.asarray(p["I_normalized"]),data["maps"][j])
        image=ax.pcolormesh(q[:,:,0],q[:,:,1],values,shading="nearest",vmin=-8,vmax=0,cmap="magma",rasterized=False)
        corners=image.get_coordinates(); centers=(corners[:-1,:-1]+corners[1:,1:])/2
        np.testing.assert_allclose(centers,q[:,:,:2],atol=1e-12,rtol=0)
        ax.set_aspect("equal"); ax.set(xticks=[-8,0,8],yticks=[-8,0,8],xlabel=r"$q_x$ (angular)")
        if j==0: ax.set_ylabel(r"$q_y$ (angular)")
        else: ax.tick_params(labelleft=False)
        panel(ax,chr(68+j),"Interior" if state=="inside" else state.capitalize(),y=1.16)
        ax.text(.5,1.025,rf"$\lambda={p['parameter']:.5f}$",transform=ax.transAxes,ha="center",fontsize=9,color=C[state])
    cb=fig.colorbar(image,cax=fig.add_axes([.925,.13,.012,.28]),ticks=[-8,-4,0])
    cb.ax.tick_params(labelsize=9,pad=2); cb.outline.set_visible(False)
    cb.set_label(r"$\log_{10}(I_{\mathrm{norm}})$",fontsize=9,labelpad=2)
    cb.solids.set_rasterized(False)
    export.save(fig,"figure3_diffraction")


def timing_cases(summary, case, query=None, q=None):
    return [c for c in summary["cases"] if c["case"]==case
            and (query is None or c["query_count"]==query) and (q is None or c["q_count"]==q)]


def raw_component(summary,case,method,component):
    rows=[r for r in summary["raw_repetitions"] if r["case"]==case["case"]
          and r["q_count"]==case["q_count"] and r["query_count"]==case["query_count"] and r["method"]==method]
    assert len(rows)==5
    raw=np.asarray([r[component] for r in rows])*1000
    stats=case["methods"][method][component]
    np.testing.assert_allclose(np.quantile(raw,[.25,.5,.75]),
                               np.asarray([stats[k] for k in ("q25","median","q75")])*1000,rtol=1e-14,atol=1e-12)
    return raw,stats


def total_lines(ax,summary,cases,xkey):
    for method,marker,style in zip(METHODS,MARKERS,LINESTYLES,strict=True):
        x=[];y=[];low=[];high=[]
        for case in cases:
            raw,stats=raw_component(summary,case,method,"total_seconds")
            xx=case[xkey]; x.append(xx); y.append(stats["median"]*1000)
            low.append((stats["median"]-stats["q25"])*1000); high.append((stats["q75"]-stats["median"])*1000)
            ax.scatter(xx*np.exp(np.linspace(-.06,.06,5)),raw,s=7,c=C[method],alpha=.38,edgecolors="none",zorder=4)
        ax.errorbar(x,y,yerr=[low,high],c=C[method],marker=marker,ls=style,capsize=3,
                    elinewidth=1,markeredgewidth=.7,label=method,zorder=3)
    ax.set(xscale="log",yscale="log",ylabel="Total time (ms)"); light_grid(ax)


def benchmarks(bench,boundary,export):
    with matplotlib.rc_context({"font.size":10,"axes.labelsize":10,
                                "xtick.labelsize":9,"ytick.labelsize":9,"legend.fontsize":9}):
        return _benchmarks(bench,boundary,export)


def _benchmarks(bench,boundary,export):
    fig,axes=plt.subplots(2,2,figsize=(7.2,4.9))
    fig.subplots_adjust(left=.105,right=.965,bottom=.105,top=.855,wspace=.43,hspace=.66)
    legend=[Line2D([],[],color=C[m],marker=k,ls=s,label=m+"  "+n)
            for m,n,k,s in zip(METHODS,METHOD_NAMES,MARKERS,LINESTYLES,strict=True)]
    fig.legend(handles=legend,loc="upper center",bbox_to_anchor=(.52,.993),ncols=4,
               columnspacing=1.2,handletextpad=.45,fontsize=9)
    focused=sorted(timing_cases(bench,"r2_oblique",q=121),key=lambda c:c["query_count"])
    ax=axes[0,0]; total_lines(ax,bench,focused,"query_count")
    ax.set_xlabel("Query count"); ax.set_xticks([2,9,33,129,257],labels=["2","9","33","129","257"])
    ax.xaxis.set_minor_formatter(NullFormatter()); panel(ax,"A","Repeated queries")
    case=next(c for c in focused if c["query_count"]==33)
    ax=axes[0,1]
    for j,m in enumerate(METHODS):
        for component,offset,filled in (("setup_seconds",-.14,False),("query_seconds",.14,True)):
            raw,stats=raw_component(bench,case,m,component)
            color=C[m] if filled else C["muted"]; mid=stats["median"]*1000
            ax.scatter(raw,j+offset+np.linspace(-.09,.09,5),s=8,color=color,alpha=.38,zorder=4)
            ax.errorbar(mid,j+offset,xerr=[[(stats["median"]-stats["q25"])*1000],[(stats["q75"]-stats["median"])*1000]],
                        fmt="o",mfc=color if filled else "white",mec=color,color=color,capsize=3,ms=4,zorder=3)
    ax.set(yticks=range(4),yticklabels=METHODS,ylim=(3.6,-.6),xlim=(-1.2,36),xlabel="Component time (ms)")
    ax.legend(handles=[Line2D([],[],marker="o",ls="",mfc="white",mec=C["muted"],label="Setup"),
                       Line2D([],[],marker="o",ls="",color=C["ink"],label="Query")],loc="lower right",fontsize=9)
    light_grid(ax,"x"); panel(ax,"B","Setup and query")
    scaling=sorted(timing_cases(bench,"r2_oblique",query=33),key=lambda c:c["q_count"])
    ax=axes[1,0]; total_lines(ax,bench,scaling,"q_count")
    ax.set_xlabel("Wavevector count"); ax.set_xticks([121,1089,4225],labels=["121","1089","4225"])
    ax.xaxis.set_minor_formatter(NullFormatter()); panel(ax,"C","Reciprocal-grid scaling")
    ax=axes[1,1]; case=boundary["cases"][0]; assert case["query_count"]==566
    xmax=0
    for j,m in enumerate(METHODS):
        raw,stats=raw_component(boundary,case,m,"total_seconds"); mid=stats["median"]*1000
        ax.scatter(raw,j+np.linspace(-.16,.16,5),s=11,c=C[m],alpha=.38,edgecolors="none",zorder=4)
        ax.errorbar(mid,j,xerr=[[(stats["median"]-stats["q25"])*1000],[(stats["q75"]-stats["median"])*1000]],
                    fmt=MARKERS[j],color=C[m],ms=4.5,capsize=3,zorder=3)
        xmax=max(xmax,float(raw.max()))
    ax.set(yticks=range(4),yticklabels=METHODS,ylim=(3.6,-.6),xlim=(0,xmax*1.10),xlabel="Total time (ms)")
    light_grid(ax,"x"); panel(ax,"D","Boundary-sensitive workload")
    export.save(fig,"figure4_matched")


def build_figures(examples, benchmark_dir, paper, previews, formats=("pdf","svg","png")):
    font=apply_style()
    inputs=[examples/n for n in ("example_b_arrays.npz","example_b_before.json","example_b_inside.json",
            "example_b_after.json","example_a_after.json","unity_after_panels.png",
            "unity_after_sidecar.json","uncertain_status.png","uncertain_panels.png","uncertain_capture.json")]
    inputs += [benchmark_dir/name/"summary.json" for name in ("benchmark_final","benchmark_boundary")]
    def input_key(path):
        return "examples/"+path.name if path.parent==examples else path.parent.name+"/"+path.name
    hashes={input_key(p):sha(p) for p in inputs}
    payload=read_json(examples/"example_a_after.json"); sidecar=read_json(examples/"unity_after_sidecar.json")
    assert sha(examples/"unity_after_panels.png")==sidecar["image_sha256"]
    state=sidecar["state"]; instrument=state["session"]["instrument"]
    assert payload["focus"]==list(map(int,instrument["SelectedStableId"].split(",")))
    assert payload["phi"]==instrument["Phi"] and payload["selected_ids"]==state["selected_ids"]
    assert payload["N"]==15 and len(payload["q"])==381
    np.testing.assert_array_equal(payload["q"],state["diffraction"]["q"])
    values=np.asarray(state["diffraction"]["values"])
    for field,col in (("F_real",0),("F_imag",1),("I_normalized",3)):
        np.testing.assert_allclose(payload[field],values[:,col],atol=1e-12,rtol=0)
    export=Exporter(paper,previews,formats)
    workflow_evidence=workflow(examples,export)
    capture_layout=captures(examples,export); diffraction(examples,export)
    benchmarks(read_json(benchmark_dir/"benchmark_final/summary.json"),read_json(benchmark_dir/"benchmark_boundary/summary.json"),export)
    assert hashes=={input_key(p):sha(p) for p in inputs}, "Frozen input changed"
    audit={"gate":"PASS","font":font,"input_sha256":hashes,"figures":export.records,
           "workflow_evidence":workflow_evidence,
           "capture_pixels":"Unity camera crops unmodified; Python Figure 2A replotted from export",
           "figure2_layout":capture_layout,"same_state_capture_verified":True,
           "heatmap_centers_verified":True,"plotted_timing_quantiles_checked_against_five_raw_repetitions":True,
           "new_experiments_or_timings":False,"new_confidence_intervals_or_significance_tests":False,
           "capture_scope":"recorded application panels; not new device observations"}
    (previews/"figure_data_audit.json").write_text(json.dumps(audit,indent=2),encoding="utf-8")
    return audit


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--examples-dir",type=Path,default=ROOT/"publication_data/examples")
    parser.add_argument("--benchmark-dir",type=Path,default=ROOT/"publication_data")
    parser.add_argument("--paper-dir",type=Path,default=ROOT/"reproduction-figures")
    parser.add_argument("--preview-dir",type=Path,default=ROOT/"reproduction-figure-previews")
    args=parser.parse_args()
    audit=build_figures(args.examples_dir,args.benchmark_dir,args.paper_dir,args.preview_dir)
    print(json.dumps({"gate":audit["gate"],"figures":list(audit["figures"]),"font":audit["font"]}))


if __name__=="__main__":
    main()
