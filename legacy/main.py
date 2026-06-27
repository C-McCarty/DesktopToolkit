import copy
import ctypes
import sys
import tkinter as tk
from tkinter import messagebox, filedialog, simpledialog

import constants
from models import MonitorInfo
from monitors import get_monitors, apply_monitors, find_stranded
from profiles import save_profile, load_profile, apply_profile_to_monitors
from canvas_view import CanvasView
from sidebar import Sidebar
from toolbar import Toolbar


def _set_dpi_awareness():
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)  # PROCESS_PER_MONITOR_DPI_AWARE
    except Exception:
        try:
            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:
            pass


class App(tk.Tk):

    def __init__(self):
        _set_dpi_awareness()
        super().__init__()

        self.title('Monitor Arrangement')
        self.geometry('1100x640')
        self.minsize(700, 400)
        self.configure(bg=constants.WINDOW_BG)

        self._monitors: list[MonitorInfo] = []
        self._undo_stack: list[list[MonitorInfo]] = []
        self._redo_stack: list[list[MonitorInfo]] = []

        self._build_ui()
        self._load_current()

    # ----------------------------------------------------------------- UI build

    def _build_ui(self):
        callbacks = {
            'apply': self._apply,
            'identify': self._identify,
            'undo': self._undo,
            'redo': self._redo,
            'reset': self._reset,
            'save_profile': self._save_profile,
            'load_profile': self._load_profile,
            'toggle_snap_edges': self._toggle_snap_edges,
            'toggle_snap_grid': self._toggle_snap_grid,
            'set_grid_size': self._set_grid_size,
            'zoom_in': lambda: self._canvas.zoom_in(),
            'zoom_out': lambda: self._canvas.zoom_out(),
            'fit_view': lambda: self._canvas.fit_view(),
            'copy_diag': self._copy_diag,
        }

        self._toolbar = Toolbar(self, callbacks)
        self._toolbar.pack(side='top', fill='x')

        main = tk.Frame(self, bg=constants.WINDOW_BG)
        main.pack(fill='both', expand=True)

        self._sidebar = Sidebar(main, on_coord_change_cb=self._on_coord_change)
        self._sidebar.pack(side='right', fill='y')

        self._canvas = CanvasView(
            main,
            on_move_cb=self._on_monitor_moved,
            on_drag_start_cb=self._on_drag_start,
        )
        self._canvas.pack(side='left', fill='both', expand=True)

        self._toolbar.set_undo_enabled(False)
        self._toolbar.set_redo_enabled(False)

    # --------------------------------------------------------- monitor loading

    def _load_current(self):
        try:
            self._monitors = get_monitors()
        except Exception as e:
            messagebox.showerror('Error', f'Failed to read monitor config:\n{e}')
            return
        self._canvas.load_monitors(self._monitors)
        self._sidebar.load_monitors(self._monitors)

    # -------------------------------------------------------------- undo/redo

    def _snapshot(self):
        return copy.deepcopy(self._monitors)

    def _push_undo(self):
        self._undo_stack.append(self._snapshot())
        if len(self._undo_stack) > constants.UNDO_MAX:
            self._undo_stack.pop(0)
        self._redo_stack.clear()
        self._update_undo_buttons()

    def _undo(self):
        if not self._undo_stack:
            return
        self._redo_stack.append(self._snapshot())
        self._monitors = self._undo_stack.pop()
        self._canvas.load_monitors(self._monitors)
        self._sidebar.load_monitors(self._monitors)
        self._update_undo_buttons()

    def _redo(self):
        if not self._redo_stack:
            return
        self._undo_stack.append(self._snapshot())
        self._monitors = self._redo_stack.pop()
        self._canvas.load_monitors(self._monitors)
        self._sidebar.load_monitors(self._monitors)
        self._update_undo_buttons()

    def _update_undo_buttons(self):
        self._toolbar.set_undo_enabled(bool(self._undo_stack))
        self._toolbar.set_redo_enabled(bool(self._redo_stack))

    # ---------------------------------------------------------- event handlers

    def _on_drag_start(self, _index):
        self._push_undo()

    def _on_monitor_moved(self, index, x, y):
        """Canvas drag updated the model — sync the sidebar field."""
        self._sidebar.sync_from_model(index, self._monitors[index])

    def _on_coord_change(self, index, coord, value):
        """Sidebar entry changed a coordinate — update the model and redraw."""
        setattr(self._monitors[index], coord, value)
        self._canvas.redraw()

    # ----------------------------------------------------------------- actions

    def _apply(self):
        stranded = find_stranded(self._monitors)
        if stranded:
            names = ', '.join(f'{m.index} ({m.friendly_name})' for m in stranded)
            if not messagebox.askyesno(
                    'Gap detected',
                    f'These monitors have a gap on all sides and don\'t touch '
                    f'another monitor:\n\n{names}\n\n'
                    'Windows requires a contiguous layout and will likely reject '
                    'this arrangement. Apply anyway?'):
                return

        if not messagebox.askyesno(
                'Apply', 'Apply the new monitor arrangement?\n\n'
                'The screen may flicker briefly.'):
            return
        self._push_undo()
        ok, err = apply_monitors(self._monitors)
        if ok:
            messagebox.showinfo('Done', 'Monitor arrangement applied.')
            self._load_current()
        else:
            messagebox.showerror('Error', f'Apply failed:\n{err}')

    def _reset(self):
        if not messagebox.askyesno('Reset', 'Reload current Windows arrangement?'):
            return
        self._push_undo()
        self._load_current()

    def _copy_diag(self):
        """Dump the live monitor list to the clipboard for troubleshooting."""
        lines = ['Monitor Arrangement — diagnostics']
        positions = {}
        for m in self._monitors:
            positions.setdefault((m.x, m.y, m.width, m.height), []).append(m.index)
            lines.append(
                f'  [{m.index}] {m.friendly_name} ({m.device_name})\n'
                f'        pos=({m.x},{m.y})  size={m.width}x{m.height}  '
                f'{m.refresh_rate}Hz  {"PRIMARY" if m.is_primary else "secondary"}'
            )

        # Monitors sharing the exact same rectangle are mirrored/duplicated —
        # Windows cannot position those independently.
        mirrored = [idxs for idxs in positions.values() if len(idxs) > 1]
        if mirrored:
            groups = '; '.join('+'.join(str(i) for i in g) for g in mirrored)
            lines.append(f'  ** MIRRORED/DUPLICATE groups: {groups} **')

        text = '\n'.join(lines)
        self.clipboard_clear()
        self.clipboard_append(text)
        messagebox.showinfo('Diagnostics copied',
                            'Monitor diagnostics copied to clipboard.\n\n'
                            'Paste it into a message to share.')

    def _identify(self):
        overlays = []
        for i, m in enumerate(self._monitors):
            w = tk.Toplevel(self)
            w.overrideredirect(True)
            w.attributes('-topmost', True)
            w.attributes('-alpha', 0.75)
            w.configure(bg='black')
            # Position at physical monitor coords (works when DPI-aware)
            w.geometry(f'{m.width}x{m.height}+{m.x}+{m.y}')
            font_size = max(40, min(200, m.height // 5))
            tk.Label(w, text=str(m.index),
                     font=('Segoe UI', font_size, 'bold'),
                     bg='black', fg='white').pack(expand=True, fill='both')
            overlays.append(w)

        def _close():
            for ov in overlays:
                try:
                    ov.destroy()
                except Exception:
                    pass

        self.after(3000, _close)

    def _save_profile(self):
        path = filedialog.asksaveasfilename(
            defaultextension='.json',
            filetypes=[('Monitor profiles', '*.json'), ('All files', '*.*')],
            title='Save Profile',
        )
        if not path:
            return
        name = simpledialog.askstring('Profile name', 'Enter a name for this profile:',
                                      parent=self) or ''
        try:
            save_profile(self._monitors, path, name)
        except Exception as e:
            messagebox.showerror('Error', f'Could not save profile:\n{e}')

    def _load_profile(self):
        path = filedialog.askopenfilename(
            filetypes=[('Monitor profiles', '*.json'), ('All files', '*.*')],
            title='Load Profile',
        )
        if not path:
            return
        try:
            entries, name = load_profile(path)
        except Exception as e:
            messagebox.showerror('Error', f'Could not read profile:\n{e}')
            return

        matched = apply_profile_to_monitors(entries, self._monitors)
        if not matched:
            messagebox.showwarning('No match',
                                   'No monitors in this profile matched the current display setup.')
            return

        self._push_undo()
        for m, new_x, new_y in matched:
            m.x = new_x
            m.y = new_y

        self._canvas.refresh()
        self._sidebar.load_monitors(self._monitors)
        if name:
            self.title(f'Monitor Arrangement — {name}')

    # ------------------------------------------------------- snap/grid toggles

    def _toggle_snap_edges(self):
        self._canvas.snap_edges = self._toolbar.snap_edges

    def _toggle_snap_grid(self):
        self._canvas.snap_grid = self._toolbar.snap_grid
        self._canvas.redraw()

    def _set_grid_size(self):
        try:
            gs = int(self._toolbar.grid_size_str)
            if gs > 0:
                self._canvas.grid_size = gs
                if self._canvas.snap_grid:
                    self._canvas.redraw()
        except ValueError:
            pass


def main():
    app = App()
    app.mainloop()


if __name__ == '__main__':
    main()
