import tkinter as tk
import constants


class CanvasView(tk.Canvas):

    def __init__(self, parent, on_move_cb, on_drag_start_cb):
        super().__init__(parent, bg=constants.CANVAS_BG, highlightthickness=0)
        self._monitors = []
        self._scale = 1.0
        self._offset_x = 0.0
        self._offset_y = 0.0
        self._fit_scale = 1.0       # scale produced by the last auto-fit

        # Zoom / pan: once the user zooms or pans, stop auto-fitting until reset.
        self._user_zoomed = False
        self._pan_anchor = None

        self._dragging = None
        self._drag_anchor_virt = (0, 0)
        self._drag_anchor_mouse = (0, 0)

        self.snap_edges = True
        self.snap_grid = False
        self.grid_size = constants.DEFAULT_GRID

        self._on_move_cb = on_move_cb            # (index, x, y)
        self._on_drag_start_cb = on_drag_start_cb  # (index) — for undo snapshot

        self.bind('<Configure>', self._on_resize)
        self.bind('<ButtonPress-1>', self._on_press)
        self.bind('<B1-Motion>', self._on_drag)
        self.bind('<ButtonRelease-1>', self._on_release)
        # Wheel to zoom about the cursor; right-drag to pan; double-click to fit.
        self.bind('<MouseWheel>', self._on_wheel)
        self.bind('<ButtonPress-3>', self._on_pan_start)
        self.bind('<B3-Motion>', self._on_pan_move)
        self.bind('<Double-Button-1>', lambda _e: self.fit_view())

    # ------------------------------------------------------------------ public

    def load_monitors(self, monitors):
        self._monitors = monitors
        self._user_zoomed = False
        self._fit()
        self._draw()

    def refresh(self):
        """Refit viewport and redraw — call after external model changes."""
        self._user_zoomed = False
        self._fit()
        self._draw()

    def redraw(self):
        """Redraw without refitting — used during drag to keep viewport stable."""
        self._draw()

    def fit_view(self):
        """Reset zoom/pan and fit the whole layout to the canvas."""
        self._user_zoomed = False
        self._fit()
        self._draw()

    def zoom_in(self):
        self._zoom_at(1.25, self.winfo_width() / 2, self.winfo_height() / 2)

    def zoom_out(self):
        self._zoom_at(0.8, self.winfo_width() / 2, self.winfo_height() / 2)

    # --------------------------------------------------------------- transform

    def _fit(self):
        if not self._monitors:
            return
        w = self.winfo_width() or 800
        h = self.winfo_height() or 500

        min_x = min(m.x for m in self._monitors)
        min_y = min(m.y for m in self._monitors)
        max_x = max(m.x + m.width for m in self._monitors)
        max_y = max(m.y + m.height for m in self._monitors)

        virt_w = max_x - min_x or 1
        virt_h = max_y - min_y or 1

        avail_w = w - 2 * constants.CANVAS_PADDING
        avail_h = h - 2 * constants.CANVAS_PADDING
        self._scale = min(avail_w / virt_w, avail_h / virt_h)

        self._offset_x = (constants.CANVAS_PADDING
                          + (avail_w - virt_w * self._scale) / 2
                          - min_x * self._scale)
        self._offset_y = (constants.CANVAS_PADDING
                          + (avail_h - virt_h * self._scale) / 2
                          - min_y * self._scale)
        self._fit_scale = self._scale

    def _zoom_at(self, factor, px, py):
        """Zoom by `factor` keeping the virtual point under (px, py) fixed."""
        if not self._monitors:
            return
        new_scale = self._scale * factor
        lo, hi = self._fit_scale * 0.2, self._fit_scale * 25
        new_scale = max(lo, min(hi, new_scale))
        factor = new_scale / self._scale
        if abs(factor - 1.0) < 1e-9:
            return
        self._offset_x = px - (px - self._offset_x) * factor
        self._offset_y = py - (py - self._offset_y) * factor
        self._scale = new_scale
        self._user_zoomed = True
        self._draw()

    def _vc(self, x, y):
        """Virtual → canvas coords."""
        return (x * self._scale + self._offset_x,
                y * self._scale + self._offset_y)

    def _cv(self, cx, cy):
        """Canvas → virtual coords."""
        return ((cx - self._offset_x) / self._scale,
                (cy - self._offset_y) / self._scale)

    # -------------------------------------------------------------------- draw

    def _draw(self):
        self.delete('all')
        if not self._monitors:
            return

        # Virtual desktop bounding box
        min_x = min(m.x for m in self._monitors)
        min_y = min(m.y for m in self._monitors)
        max_x = max(m.x + m.width for m in self._monitors)
        max_y = max(m.y + m.height for m in self._monitors)
        cx0, cy0 = self._vc(min_x, min_y)
        cx1, cy1 = self._vc(max_x, max_y)
        self.create_rectangle(cx0, cy0, cx1, cy1,
                              outline='#334', width=1, dash=(4, 4))

        # Grid overlay
        if self.snap_grid and self.grid_size > 0:
            self._draw_grid(min_x, min_y, max_x, max_y)

        # Monitor rectangles (back to front so index 0 is on top if overlap)
        for i, m in enumerate(self._monitors):
            color = constants.MONITOR_COLORS[i % len(constants.MONITOR_COLORS)]
            x0, y0 = self._vc(m.x, m.y)
            x1, y1 = self._vc(m.x + m.width, m.y + m.height)

            outline_w = 3 if i == self._dragging else 2
            self.create_rectangle(x0, y0, x1, y1,
                                  fill=color, outline='white',
                                  width=outline_w, tags=f'mon{i}')

            rect_w = x1 - x0
            rect_h = y1 - y0
            if rect_w >= 40 and rect_h >= 24:
                lines = [f'{m.index}: {m.friendly_name}',
                         f'{m.width}×{m.height}']
                if m.is_primary:
                    lines.append('[Primary]')
                label = '\n'.join(lines)
                font_size = max(7, min(11, int(rect_h / 7)))
                self.create_text((x0 + x1) / 2, (y0 + y1) / 2,
                                 text=label, fill='white',
                                 justify='center',
                                 font=('Segoe UI', font_size, 'bold'))

    def _draw_grid(self, min_x, min_y, max_x, max_y):
        gs = self.grid_size
        # snap grid_x start to multiple of gs
        gx = (min_x // gs) * gs
        while gx <= max_x:
            cx, _ = self._vc(gx, 0)
            _, cy0 = self._vc(0, min_y)
            _, cy1 = self._vc(0, max_y)
            self.create_line(cx, cy0, cx, cy1, fill='#2a2a4a', width=1)
            gx += gs
        gy = (min_y // gs) * gs
        while gy <= max_y:
            _, cy = self._vc(0, gy)
            cx0, _ = self._vc(min_x, 0)
            cx1, _ = self._vc(max_x, 0)
            self.create_line(cx0, cy, cx1, cy, fill='#2a2a4a', width=1)
            gy += gs

    # ---------------------------------------------------------------- events

    def _monitor_at(self, cx, cy):
        for i in reversed(range(len(self._monitors))):
            m = self._monitors[i]
            x0, y0 = self._vc(m.x, m.y)
            x1, y1 = self._vc(m.x + m.width, m.y + m.height)
            if x0 <= cx <= x1 and y0 <= cy <= y1:
                return i
        return None

    def _on_resize(self, _event):
        if not self._user_zoomed:
            self._fit()
        self._draw()

    def _on_wheel(self, event):
        factor = 1.1 if event.delta > 0 else 0.9
        self._zoom_at(factor, event.x, event.y)

    def _on_pan_start(self, event):
        self._pan_anchor = (event.x, event.y)

    def _on_pan_move(self, event):
        if self._pan_anchor is None:
            return
        self._offset_x += event.x - self._pan_anchor[0]
        self._offset_y += event.y - self._pan_anchor[1]
        self._pan_anchor = (event.x, event.y)
        self._user_zoomed = True
        self._draw()

    def _on_press(self, event):
        i = self._monitor_at(event.x, event.y)
        if i is None:
            return
        self._on_drag_start_cb(i)
        self._dragging = i
        m = self._monitors[i]
        self._drag_anchor_virt = (m.x, m.y)
        self._drag_anchor_mouse = (event.x, event.y)

    def _on_drag(self, event):
        if self._dragging is None:
            return
        m = self._monitors[self._dragging]
        threshold = constants.SNAP_THRESHOLD_PX / self._scale

        dx = (event.x - self._drag_anchor_mouse[0]) / self._scale
        dy = (event.y - self._drag_anchor_mouse[1]) / self._scale

        new_x = int(self._drag_anchor_virt[0] + dx)
        new_y = int(self._drag_anchor_virt[1] + dy)

        if self.snap_grid and self.grid_size > 0:
            gs = self.grid_size
            new_x = round(new_x / gs) * gs
            new_y = round(new_y / gs) * gs

        if self.snap_edges:
            new_x, new_y = self._snap(self._dragging, new_x, new_y, threshold)

        m.x = new_x
        m.y = new_y
        self._draw()
        self._on_move_cb(self._dragging, new_x, new_y)

    def _on_release(self, _event):
        self._dragging = None
        if not self._user_zoomed:
            self._fit()
        self._draw()

    def _snap(self, idx, x, y, threshold):
        m = self._monitors[idx]
        best_x, best_y = x, y
        min_dx = min_dy = threshold + 1

        for i, o in enumerate(self._monitors):
            if i == idx:
                continue
            # x-axis snap candidates: align left/right edges of m to left/right edges of o
            for sx in (o.x, o.x + o.width,
                       o.x - m.width, o.x + o.width - m.width):
                if abs(x - sx) < min_dx:
                    best_x, min_dx = sx, abs(x - sx)
            # y-axis snap candidates
            for sy in (o.y, o.y + o.height,
                       o.y - m.height, o.y + o.height - m.height):
                if abs(y - sy) < min_dy:
                    best_y, min_dy = sy, abs(y - sy)

        return best_x, best_y
