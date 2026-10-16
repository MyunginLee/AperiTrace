"""Shared publication style; scientific colors are semantic, not decorative."""

import matplotlib as mpl
from matplotlib import font_manager

COLORS = {
    "ink": "#202B36",
    "muted": "#667482",
    "grid": "#E4E8EC",
    "window": "#5C6874",
    "before": "#64748B",
    "inside": "#007D83",
    "after": "#C66B29",
    "q0": "#202B36",
    "q1": "#456DA9",
    "B0": "#25313C",
    "B1": "#8A949E",
    "B2": "#456DA9",
    "B3": "#007D83",
    "pale": "#F3F6F8",
    "accent_pale": "#EAF4F3",
}
METHODS = ("B0", "B1", "B2", "B3")
METHOD_NAMES = ("Direct", "Cached reference", "Half-spaces", "Event replay")
MARKERS = ("o", "s", "^", "D")
LINESTYLES = ("-", "--", "-.", ":")

# Figure 1 borrows the author's reference diagram's white cards, navy outlines,
# dotted stage boundaries and strong headings, not its scientific content.
WORKFLOW_STYLE = {
    "outline": "#092F40", "flow": "#393D40", "heading": 9.5,
    "card_title": 9.0, "detail": 8.3, "outline_width": 1.15,
    "group_width": 1.25, "group_dash": (0, (1.2, 1.5)),
}


def apply_style():
    available = {entry.name for entry in font_manager.fontManager.ttflist}
    family = next(name for name in ("Arial", "Liberation Sans", "DejaVu Sans") if name in available)
    mpl.rcParams.update({
        "font.family": "sans-serif", "font.sans-serif": [family],
        "font.size": 9, "axes.labelsize": 9, "axes.titlesize": 10,
        "xtick.labelsize": 8, "ytick.labelsize": 8, "legend.fontsize": 8,
        "text.color": COLORS["ink"], "axes.labelcolor": COLORS["ink"],
        "xtick.color": COLORS["muted"], "ytick.color": COLORS["muted"],
        "axes.edgecolor": COLORS["muted"], "axes.linewidth": .65,
        "axes.spines.top": False, "axes.spines.right": False,
        "xtick.major.size": 3, "ytick.major.size": 3,
        "xtick.major.width": .65, "ytick.major.width": .65,
        "lines.linewidth": 1.4, "lines.markersize": 4.2,
        "legend.frameon": False, "legend.handlelength": 2.2,
        "pdf.fonttype": 42, "ps.fonttype": 42, "svg.fonttype": "none",
        "mathtext.fontset": "dejavusans", "savefig.facecolor": "white",
        "figure.facecolor": "white", "axes.facecolor": "white",
    })
    return family


def panel(ax, letter, title, y=1.075):
    ax.text(0, y, letter, transform=ax.transAxes, fontsize=11, fontweight="bold",
            ha="left", va="bottom", clip_on=False)
    ax.text(.105, y, title, transform=ax.transAxes, fontsize=9.5,
            ha="left", va="bottom", clip_on=False)


def light_grid(ax, axis="y"):
    ax.set_axisbelow(True)
    ax.grid(axis=axis, which="major", color=COLORS["grid"], linewidth=.55)
