import tkinter as tk
import constants


def _btn(parent, text, cmd, bg=None, width=10):
    bg = bg or constants.BTN_NORMAL_BG
    b = tk.Button(parent, text=text, command=cmd,
                  bg=bg, fg=constants.TEXT_FG,
                  activebackground='#2a3a6a', activeforeground=constants.TEXT_FG,
                  relief='flat', padx=8, pady=4, font=('Segoe UI', 9),
                  cursor='hand2', width=width)
    b.pack(side='left', padx=2, pady=4)
    return b


class Toolbar(tk.Frame):

    def __init__(self, parent, callbacks):
        super().__init__(parent, bg=constants.TOOLBAR_BG)

        # callbacks dict keys:
        #   apply, identify, undo, redo, reset, save_profile, load_profile
        cb = callbacks

        _btn(self, 'Apply', cb['apply'], bg=constants.BTN_APPLY_BG, width=8)

        tk.Frame(self, bg='#334', width=1).pack(side='left', fill='y', padx=4, pady=4)

        _btn(self, 'Identify', cb['identify'], width=8)
        _btn(self, 'Reset', cb['reset'], bg=constants.BTN_DANGER_BG, width=6)

        tk.Frame(self, bg='#334', width=1).pack(side='left', fill='y', padx=4, pady=4)

        self._undo_btn = _btn(self, '↶ Undo', cb['undo'], width=7)
        self._redo_btn = _btn(self, '↷ Redo', cb['redo'], width=7)

        tk.Frame(self, bg='#334', width=1).pack(side='left', fill='y', padx=4, pady=4)

        _btn(self, '－', cb['zoom_out'], width=2)
        _btn(self, '＋', cb['zoom_in'], width=2)
        _btn(self, 'Fit', cb['fit_view'], width=4)

        tk.Frame(self, bg='#334', width=1).pack(side='left', fill='y', padx=4, pady=4)

        _btn(self, 'Save Profile', cb['save_profile'], width=11)
        _btn(self, 'Load Profile', cb['load_profile'], width=11)
        _btn(self, 'Copy Diag', cb['copy_diag'], width=9)

        tk.Frame(self, bg='#334', width=1).pack(side='left', fill='y', padx=4, pady=4)

        # Snap toggles
        self._snap_edges_var = tk.BooleanVar(value=True)
        self._snap_grid_var = tk.BooleanVar(value=False)

        tk.Checkbutton(self, text='Snap Edges',
                       variable=self._snap_edges_var,
                       command=cb.get('toggle_snap_edges'),
                       bg=constants.TOOLBAR_BG, fg=constants.TEXT_FG,
                       selectcolor=constants.ENTRY_BG,
                       activebackground=constants.TOOLBAR_BG,
                       activeforeground=constants.TEXT_FG,
                       font=('Segoe UI', 9)).pack(side='left', padx=4)

        tk.Checkbutton(self, text='Grid',
                       variable=self._snap_grid_var,
                       command=cb.get('toggle_snap_grid'),
                       bg=constants.TOOLBAR_BG, fg=constants.TEXT_FG,
                       selectcolor=constants.ENTRY_BG,
                       activebackground=constants.TOOLBAR_BG,
                       activeforeground=constants.TEXT_FG,
                       font=('Segoe UI', 9)).pack(side='left', padx=(4, 0))

        self._grid_size_var = tk.StringVar(value=str(constants.DEFAULT_GRID))
        grid_entry = tk.Entry(self, textvariable=self._grid_size_var,
                              bg=constants.ENTRY_BG, fg=constants.TEXT_FG,
                              insertbackground=constants.TEXT_FG,
                              relief='flat', font=('Segoe UI', 9), width=5)
        grid_entry.pack(side='left', padx=4, pady=4)
        grid_entry.bind('<Return>', lambda _: cb.get('set_grid_size', lambda: None)())
        grid_entry.bind('<FocusOut>', lambda _: cb.get('set_grid_size', lambda: None)())

        tk.Label(self, text='px', bg=constants.TOOLBAR_BG,
                 fg=constants.MUTED_FG, font=('Segoe UI', 9)).pack(side='left')

    # ----------------------------------------------------------- property access

    @property
    def snap_edges(self):
        return self._snap_edges_var.get()

    @property
    def snap_grid(self):
        return self._snap_grid_var.get()

    @property
    def grid_size_str(self):
        return self._grid_size_var.get()

    def set_undo_enabled(self, enabled):
        self._undo_btn.config(state='normal' if enabled else 'disabled')

    def set_redo_enabled(self, enabled):
        self._redo_btn.config(state='normal' if enabled else 'disabled')
