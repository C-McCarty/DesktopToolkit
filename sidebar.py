import tkinter as tk
from tkinter import ttk
import constants


class Sidebar(tk.Frame):

    def __init__(self, parent, on_coord_change_cb):
        super().__init__(parent, bg=constants.SIDEBAR_BG, width=230)
        self.pack_propagate(False)

        self._on_coord_change_cb = on_coord_change_cb  # (index, 'x'|'y', int_value)
        self._entries = {}   # index -> {'x': StringVar, 'y': StringVar}
        self._updating = False

        self._build_scroll()

    def _build_scroll(self):
        vsb = tk.Scrollbar(self, orient='vertical')
        vsb.pack(side='right', fill='y')

        self._inner_canvas = tk.Canvas(self,
                                       bg=constants.SIDEBAR_BG,
                                       yscrollcommand=vsb.set,
                                       highlightthickness=0)
        self._inner_canvas.pack(side='left', fill='both', expand=True)
        vsb.config(command=self._inner_canvas.yview)

        self._frame = tk.Frame(self._inner_canvas, bg=constants.SIDEBAR_BG)
        self._frame_id = self._inner_canvas.create_window(
            (0, 0), window=self._frame, anchor='nw')

        self._frame.bind('<Configure>', self._on_frame_resize)
        self._inner_canvas.bind('<Configure>', self._on_canvas_resize)
        self._inner_canvas.bind('<MouseWheel>', self._on_mousewheel)
        self._frame.bind('<MouseWheel>', self._on_mousewheel)

    def _on_frame_resize(self, _e):
        self._inner_canvas.configure(
            scrollregion=self._inner_canvas.bbox('all'))

    def _on_canvas_resize(self, e):
        self._inner_canvas.itemconfig(self._frame_id, width=e.width)

    def _on_mousewheel(self, e):
        self._inner_canvas.yview_scroll(int(-1 * (e.delta / 120)), 'units')

    # ------------------------------------------------------------------ public

    def load_monitors(self, monitors):
        for w in self._frame.winfo_children():
            w.destroy()
        self._entries.clear()

        tk.Label(self._frame, text='Monitors',
                 bg=constants.SIDEBAR_BG, fg=constants.MUTED_FG,
                 font=('Segoe UI', 9, 'bold')).pack(anchor='w', padx=10, pady=(8, 4))

        for i, m in enumerate(monitors):
            self._build_section(i, m)

    def sync_from_model(self, index, monitor):
        """Update X/Y fields from model without triggering the change callback."""
        if index not in self._entries:
            return
        self._updating = True
        self._entries[index]['x'].set(str(monitor.x))
        self._entries[index]['y'].set(str(monitor.y))
        self._updating = False

    # --------------------------------------------------------- section builder

    def _build_section(self, index, m):
        color = constants.MONITOR_COLORS[index % len(constants.MONITOR_COLORS)]

        outer = tk.Frame(self._frame, bg=constants.SIDEBAR_BG)
        outer.pack(fill='x', padx=8, pady=4)

        # Colored title bar
        header = tk.Frame(outer, bg=constants.SIDEBAR_BG)
        header.pack(fill='x')
        tk.Label(header, bg=color, width=3, text='').pack(side='left')
        title = f'  {m.index}  {m.friendly_name}'
        tk.Label(header, text=title, bg=constants.SIDEBAR_BG,
                 fg=constants.TEXT_FG, font=('Segoe UI', 9, 'bold'),
                 anchor='w').pack(side='left', fill='x', expand=True)

        # Resolution / refresh info
        info = f'{m.width}×{m.height} @ {m.refresh_rate} Hz'
        if m.is_primary:
            info += '  ★ Primary'
        tk.Label(outer, text=info, bg=constants.SIDEBAR_BG,
                 fg=constants.MUTED_FG, font=('Segoe UI', 8),
                 anchor='w').pack(fill='x', padx=4)

        # X / Y entry rows
        var_x = tk.StringVar(value=str(m.x))
        var_y = tk.StringVar(value=str(m.y))
        self._entries[index] = {'x': var_x, 'y': var_y}

        for label, var, coord in (('X', var_x, 'x'), ('Y', var_y, 'y')):
            row = tk.Frame(outer, bg=constants.SIDEBAR_BG)
            row.pack(fill='x', pady=1)
            tk.Label(row, text=label, bg=constants.SIDEBAR_BG,
                     fg=constants.MUTED_FG, font=('Segoe UI', 9),
                     width=2).pack(side='left', padx=(4, 0))
            entry = tk.Entry(row, textvariable=var,
                             bg=constants.ENTRY_BG, fg=constants.TEXT_FG,
                             insertbackground=constants.TEXT_FG,
                             relief='flat', font=('Segoe UI', 9),
                             width=10)
            entry.pack(side='left', padx=4, pady=1)

            var.trace_add('write', self._make_cb(index, coord, var))

        # Separator
        tk.Frame(self._frame, bg=constants.SEPARATOR_COLOR,
                 height=1).pack(fill='x', padx=6, pady=2)

    def _make_cb(self, index, coord, var):
        def _cb(*_):
            if self._updating:
                return
            try:
                val = int(var.get())
            except ValueError:
                return
            self._on_coord_change_cb(index, coord, val)
        return _cb
