"""pygame_dialogs.py — Pure-pygame replacements for tkinter dialogs.

Replaces:
  filedialog.askdirectory        -> ask_directory(screen, title)
  filedialog.askopenfilename     -> ask_open_filename(screen, title, filetypes)
  filedialog.asksaveasfilename   -> ask_save_filename(screen, title, defaultextension, filetypes)
  simpledialog.askstring         -> ask_string(screen, title, prompt)
  custom multiline text input    -> ask_text(screen, title, prompt)
  simpledialog.askinteger        -> ask_integer(screen, title, prompt, initialvalue)
  messagebox.askyesno            -> ask_yes_no(screen, title, message)

All functions block until the user confirms/cancels and return the same
types as their tkinter equivalents (str/None, bool).
"""

import os
import pygame
import pygame.freetype

# ── colour palette (matches the editor's constants.py) ──────────────────────
_BG        = (8,  11,  16)
_PANEL     = (20, 26,  38)
_BORDER    = (78, 106, 140)
_ACCENT    = (0,  212, 255)
_TEXT      = (200, 210, 220)
_TEXT_DIM  = (110, 120, 135)
_SEL_BG    = (0,  60,  90)
_SEL_TEXT  = (255, 255, 255)
_BTN_BG    = (30, 40,  56)
_BTN_HOV   = (42, 62,  88)
_BTN_PRESS = (0,  96,  132)
_INPUT_BG  = (14, 18,  28)
_INPUT_BOR = (50, 65,  90)
_SCROLLBAR = (40, 52,  72)
_SCROLLHOV = (60, 78, 108)

_FONT      = None
_FONT_SM   = None

def _get_font(size=16):
    global _FONT, _FONT_SM
    pygame.freetype.init()
    if size <= 13:
        if _FONT_SM is None:
            _FONT_SM = pygame.freetype.SysFont("monospace", size)
        return _FONT_SM
    if _FONT is None:
        _FONT = pygame.freetype.SysFont("monospace", size)
    return _FONT


def _render_text(surf, text, x, y, color=_TEXT, size=15):
    font = _get_font(size)
    font.render_to(surf, (x, y), text, color)


def _text_size(text, size=15):
    font = _get_font(size)
    r, _ = font.render(text, _TEXT)
    return r.get_size()


def _draw_button(surf, rect, label, hovered=False, pressed=False):
    bg = _BTN_PRESS if pressed else (_BTN_HOV if hovered else _BTN_BG)
    pygame.draw.rect(surf, bg, rect, border_radius=4)
    pygame.draw.rect(surf, _BORDER, rect, 1, border_radius=4)
    tw, th = _text_size(label)
    _render_text(surf, label, rect.x + (rect.w - tw) // 2,
                 rect.y + (rect.h - th) // 2, _TEXT)


def _wrap_dialog_text(text, max_w, size=14):
    """Wrap dialog copy while preserving blank lines and breaking long paths."""
    wrapped = []
    for raw_line in str(text).splitlines():
        if raw_line == "":
            wrapped.append("")
            continue

        current = ""
        for word in raw_line.split(" "):
            candidate = word if not current else f"{current} {word}"
            if _text_size(candidate, size)[0] <= max_w:
                current = candidate
                continue

            if current:
                wrapped.append(current)
                current = ""

            chunk = ""
            for ch in word:
                candidate = chunk + ch
                if chunk and _text_size(candidate, size)[0] > max_w:
                    wrapped.append(chunk)
                    chunk = ch
                else:
                    chunk = candidate
            current = chunk

        if current:
            wrapped.append(current)

    return wrapped or [""]


def _overlay(screen):
    """Return a darkened copy of the current screen as background."""
    bg = screen.copy()
    dark = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
    dark.fill((0, 0, 0, 160))
    bg.blit(dark, (0, 0))
    return bg


def _windows_drives():
    """Return available Windows drive roots like C:\\, D:\\."""
    if os.name != "nt":
        return []
    drives = []
    for code in range(ord("A"), ord("Z") + 1):
        drive = f"{chr(code)}:\\"
        if os.path.exists(drive):
            drives.append(drive)
    return drives


def _drive_root(path):
    drive, _tail = os.path.splitdrive(os.path.realpath(path))
    return f"{drive}\\" if drive else ""


# ── YES / NO ─────────────────────────────────────────────────────────────────

def ask_yes_no(screen, title, message):
    """Returns True/False. Esc or window-close = False."""
    bg = _overlay(screen)
    W, H = screen.get_size()
    max_text_w = max(280, min(700, W - 84))
    lines = _wrap_dialog_text(message, max_text_w, 14)
    text_w = max((_text_size(line, 14)[0] for line in lines if line), default=0)
    dw = min(max(460, text_w + 44), W - 40)
    dh = min(H - 40, max(194, 118 + len(lines) * 20))
    dx, dy = (W - dw) // 2, (H - dh) // 2

    btn_w, btn_h = 118, 36
    gap = 20
    btn_y = dy + dh - 56
    yes_r = pygame.Rect(dx + dw // 2 - btn_w - gap // 2, btn_y, btn_w, btn_h)
    no_r  = pygame.Rect(dx + dw // 2 + gap // 2,         btn_y, btn_w, btn_h)

    clock = pygame.time.Clock()
    while True:
        mx, my = pygame.mouse.get_pos()
        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:
                return False
            if ev.type == pygame.KEYDOWN:
                if ev.key == pygame.K_RETURN or ev.key == pygame.K_y:
                    return True
                if ev.key == pygame.K_ESCAPE or ev.key == pygame.K_n:
                    return False
            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                if yes_r.collidepoint(mx, my): return True
                if no_r.collidepoint(mx, my):  return False

        screen.blit(bg, (0, 0))
        pygame.draw.rect(screen, _PANEL,  (dx, dy, dw, dh), border_radius=6)
        pygame.draw.rect(screen, _BORDER, (dx, dy, dw, dh), 1, border_radius=6)
        _render_text(screen, title,   dx + 16, dy + 14, _ACCENT, 15)
        pygame.draw.line(screen, _BORDER, (dx + 16, dy + 38), (dx + dw - 16, dy + 38), 1)
        for i, ln in enumerate(lines):
            _render_text(screen, ln, dx + 16, dy + 52 + i * 20, _TEXT if ln else _TEXT_DIM, 14)
        _draw_button(screen, yes_r, "Yes", yes_r.collidepoint(mx, my))
        _draw_button(screen, no_r,  "No",  no_r.collidepoint(mx, my))
        pygame.display.flip()
        clock.tick(60)


# ── STRING INPUT ─────────────────────────────────────────────────────────────

def ask_string(screen, title, prompt, initialvalue=""):
    """Returns entered string or None on cancel."""
    bg = _overlay(screen)
    W, H = screen.get_size()
    dw, dh = min(500, W - 40), 170
    dx, dy = (W - dw) // 2, (H - dh) // 2

    text = initialvalue or ""
    cursor_vis = True
    cursor_timer = 0

    inp_r  = pygame.Rect(dx + 16, dy + 80, dw - 32, 34)
    ok_r   = pygame.Rect(dx + dw - 230, dy + dh - 50, 100, 32)
    can_r  = pygame.Rect(dx + dw - 120, dy + dh - 50, 100, 32)

    clock = pygame.time.Clock()
    while True:
        dt = clock.tick(60)
        cursor_timer += dt
        if cursor_timer >= 500:
            cursor_vis = not cursor_vis
            cursor_timer = 0

        mx, my = pygame.mouse.get_pos()
        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:       return None
            if ev.type == pygame.KEYDOWN:
                if ev.key == pygame.K_ESCAPE:    return None
                if ev.key == pygame.K_RETURN:    return text or None
                if ev.key == pygame.K_BACKSPACE: text = text[:-1]
                elif ev.unicode and ev.unicode.isprintable():
                    text += ev.unicode
            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                if ok_r.collidepoint(mx, my):  return text or None
                if can_r.collidepoint(mx, my): return None

        screen.blit(bg, (0, 0))
        pygame.draw.rect(screen, _PANEL,  (dx, dy, dw, dh), border_radius=6)
        pygame.draw.rect(screen, _BORDER, (dx, dy, dw, dh), 1, border_radius=6)
        _render_text(screen, title,  dx + 16, dy + 14, _ACCENT, 15)
        _render_text(screen, prompt, dx + 16, dy + 50, _TEXT,   14)
        pygame.draw.rect(screen, _INPUT_BG,  inp_r, border_radius=3)
        pygame.draw.rect(screen, _INPUT_BOR, inp_r, 1, border_radius=3)
        display = text + ("|" if cursor_vis else " ")
        _render_text(screen, display, inp_r.x + 8, inp_r.y + 8, _SEL_TEXT, 15)
        _draw_button(screen, ok_r,  "OK",     ok_r.collidepoint(mx, my))
        _draw_button(screen, can_r, "Cancel", can_r.collidepoint(mx, my))
        pygame.display.flip()


# ── INTEGER INPUT ─────────────────────────────────────────────────────────────

def ask_integer(screen, title, prompt, initialvalue=0):
    """Returns int or None on cancel."""
    result = ask_string(screen, title, prompt,
                        initialvalue=str(initialvalue) if initialvalue is not None else "")
    if result is None:
        return None
    try:
        return int(result)
    except ValueError:
        return ask_integer(screen, title, f"Enter a whole number:\n({prompt})", initialvalue)


# ── FILE / FOLDER BROWSER ─────────────────────────────────────────────────────

def ask_text(screen, title, prompt, initialvalue=""):
    """Returns entered multiline string or None on cancel."""
    bg = _overlay(screen)
    W, H = screen.get_size()
    dw, dh = min(820, W - 40), min(700, H - 40)
    dx, dy = (W - dw) // 2, (H - dh) // 2

    text = initialvalue or ""
    cursor = len(text)
    scroll = 0
    cursor_vis = True
    cursor_timer = 0
    line_h = 18
    char_w = max(8, _text_size("M", 14)[0])

    inp_r = pygame.Rect(dx + 16, dy + 84, dw - 32, dh - 150)
    ok_r = pygame.Rect(dx + dw - 230, dy + dh - 46, 100, 32)
    can_r = pygame.Rect(dx + dw - 120, dy + dh - 46, 100, 32)

    def _line_col_for_index(value, idx):
        idx = max(0, min(len(value), idx))
        before = value[:idx]
        line = before.count("\n")
        last_nl = before.rfind("\n")
        col = len(before) if last_nl < 0 else len(before) - last_nl - 1
        return line, col

    def _index_for_line_col(value, line, col):
        lines = value.split("\n")
        if not lines:
            return 0
        line = max(0, min(line, len(lines) - 1))
        col = max(0, col)
        idx = 0
        for i in range(line):
            idx += len(lines[i]) + 1
        return idx + min(col, len(lines[line]))

    def _ensure_visible():
        nonlocal scroll
        lines = text.split("\n")
        visible_lines = max(1, inp_r.h // line_h)
        line, _col = _line_col_for_index(text, cursor)
        max_scroll = max(0, len(lines) - visible_lines)
        if line < scroll:
            scroll = line
        elif line >= scroll + visible_lines:
            scroll = line - visible_lines + 1
        scroll = max(0, min(max_scroll, scroll))

    clock = pygame.time.Clock()
    while True:
        dt = clock.tick(60)
        cursor_timer += dt
        if cursor_timer >= 500:
            cursor_vis = not cursor_vis
            cursor_timer = 0

        _ensure_visible()
        mx, my = pygame.mouse.get_pos()

        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:
                return None
            if ev.type == pygame.KEYDOWN:
                mods = pygame.key.get_mods()
                ctrl = bool(mods & pygame.KMOD_CTRL)
                if ev.key == pygame.K_ESCAPE:
                    return None
                if (ctrl and ev.key == pygame.K_RETURN) or ev.key == pygame.K_F10:
                    return text
                if ev.key == pygame.K_BACKSPACE:
                    if cursor > 0:
                        text = text[:cursor - 1] + text[cursor:]
                        cursor -= 1
                elif ev.key == pygame.K_DELETE:
                    if cursor < len(text):
                        text = text[:cursor] + text[cursor + 1:]
                elif ev.key == pygame.K_RETURN:
                    text = text[:cursor] + "\n" + text[cursor:]
                    cursor += 1
                elif ev.key == pygame.K_TAB:
                    text = text[:cursor] + "    " + text[cursor:]
                    cursor += 4
                elif ev.key == pygame.K_LEFT:
                    cursor = max(0, cursor - 1)
                elif ev.key == pygame.K_RIGHT:
                    cursor = min(len(text), cursor + 1)
                elif ev.key == pygame.K_HOME:
                    line, _col = _line_col_for_index(text, cursor)
                    cursor = _index_for_line_col(text, line, 0)
                elif ev.key == pygame.K_END:
                    line, _col = _line_col_for_index(text, cursor)
                    lines = text.split("\n")
                    end_col = len(lines[line]) if lines else 0
                    cursor = _index_for_line_col(text, line, end_col)
                elif ev.key == pygame.K_UP:
                    line, col = _line_col_for_index(text, cursor)
                    cursor = _index_for_line_col(text, line - 1, col)
                elif ev.key == pygame.K_DOWN:
                    line, col = _line_col_for_index(text, cursor)
                    cursor = _index_for_line_col(text, line + 1, col)
                elif ev.key == pygame.K_PAGEUP:
                    visible_lines = max(1, inp_r.h // line_h)
                    line, col = _line_col_for_index(text, cursor)
                    cursor = _index_for_line_col(text, line - visible_lines, col)
                elif ev.key == pygame.K_PAGEDOWN:
                    visible_lines = max(1, inp_r.h // line_h)
                    line, col = _line_col_for_index(text, cursor)
                    cursor = _index_for_line_col(text, line + visible_lines, col)
                elif ev.unicode and ev.unicode.isprintable() and not ctrl:
                    text = text[:cursor] + ev.unicode + text[cursor:]
                    cursor += len(ev.unicode)
            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                if ok_r.collidepoint(mx, my):
                    return text
                if can_r.collidepoint(mx, my):
                    return None
                if inp_r.collidepoint(mx, my):
                    rel_y = max(0, my - inp_r.y - 6)
                    rel_x = max(0, mx - inp_r.x - 8)
                    line = scroll + min(max(0, rel_y // line_h), max(0, inp_r.h // line_h))
                    lines = text.split("\n")
                    if lines:
                        line = max(0, min(line, len(lines) - 1))
                        col = int(rel_x // char_w)
                        cursor = _index_for_line_col(text, line, col)
            if ev.type == pygame.MOUSEWHEEL and inp_r.collidepoint(mx, my):
                lines = text.split("\n")
                visible_lines = max(1, inp_r.h // line_h)
                max_scroll = max(0, len(lines) - visible_lines)
                scroll = max(0, min(max_scroll, scroll - ev.y))

        screen.blit(bg, (0, 0))
        pygame.draw.rect(screen, _PANEL, (dx, dy, dw, dh), border_radius=6)
        pygame.draw.rect(screen, _BORDER, (dx, dy, dw, dh), 1, border_radius=6)
        _render_text(screen, title, dx + 16, dy + 14, _ACCENT, 15)
        _render_text(screen, prompt, dx + 16, dy + 42, _TEXT, 14)
        _render_text(screen, "Enter = newline   Ctrl+Enter/F10 = OK   Esc = Cancel",
                     dx + 16, dy + 62, _TEXT_DIM, 12)

        pygame.draw.rect(screen, _INPUT_BG, inp_r, border_radius=3)
        pygame.draw.rect(screen, _INPUT_BOR, inp_r, 1, border_radius=3)

        lines = text.split("\n")
        visible_lines = max(1, inp_r.h // line_h)
        for i in range(visible_lines):
            idx = scroll + i
            if idx >= len(lines):
                break
            _render_text(screen, lines[idx], inp_r.x + 8, inp_r.y + 6 + i * line_h,
                         _SEL_TEXT, 14)

        line, col = _line_col_for_index(text, cursor)
        if scroll <= line < scroll + visible_lines and cursor_vis:
            cx = inp_r.x + 8 + col * char_w
            cy = inp_r.y + 6 + (line - scroll) * line_h
            pygame.draw.line(screen, _ACCENT, (cx, cy), (cx, cy + line_h - 2), 1)

        _draw_button(screen, ok_r, "OK", ok_r.collidepoint(mx, my))
        _draw_button(screen, can_r, "Cancel", can_r.collidepoint(mx, my))
        pygame.display.flip()


def _resolve_initial_dir(initial_dir):
    if not initial_dir:
        return os.path.expanduser("~")

    probe = os.path.expanduser(os.fspath(initial_dir))
    if os.path.isfile(probe):
        probe = os.path.dirname(probe)

    while probe and not os.path.isdir(probe):
        parent = os.path.dirname(probe)
        if parent == probe:
            return os.path.expanduser("~")
        probe = parent

    return os.path.realpath(probe) if probe else os.path.expanduser("~")


def _file_browser(screen, title, mode, filetypes=None, defaultextension="", initial_dir=None):
    """
    mode: 'open_file' | 'open_dir' | 'save_file'
    Returns selected path string or None.
    filetypes: list of (label, "*.ext") tuples — used only in save/open file mode.
    """
    bg = _overlay(screen)
    W, H = screen.get_size()
    dw = min(860, W - 40)
    dh = min(600, H - 40)
    dx, dy = (W - dw) // 2, (H - dh) // 2

    cwd       = _resolve_initial_dir(initial_dir)
    entries   = []
    scroll    = 0
    selected  = None          # filename string (not full path)
    save_name = ""            # used in save mode
    hovered   = None
    scroll_drag = False
    drag_start_y = 0
    drag_start_scroll = 0
    drives    = _windows_drives()
    show_drive_bar = os.name == "nt" and len(drives) > 1

    ROW_H   = 26
    DRIVE_H = 30 if show_drive_bar else 0
    LIST_Y  = dy + 60 + DRIVE_H
    LIST_H  = dh - 160 - DRIVE_H
    LIST_W  = dw - 32
    ROWS    = LIST_H // ROW_H
    SB_W    = 12          # scrollbar width

    ok_r    = pygame.Rect(dx + dw - 230, dy + dh - 46, 100, 32)
    can_r   = pygame.Rect(dx + dw - 120, dy + dh - 46, 100, 32)
    up_r    = pygame.Rect(dx + 16,       dy + dh - 46,  80, 32)
    inp_r   = pygame.Rect(dx + 16, dy + dh - 90, dw - 32, 28)   # save filename input
    path_r  = pygame.Rect(dx + 16, dy + 34, dw - 32, 20)

    def load_dir(path):
        nonlocal cwd, entries, scroll, selected
        try:
            cwd = os.path.realpath(path)
            raw = os.listdir(cwd)
        except PermissionError:
            return
        dirs  = sorted([e for e in raw if os.path.isdir(os.path.join(cwd, e))],
                       key=str.lower)
        files = sorted([e for e in raw if os.path.isfile(os.path.join(cwd, e))],
                       key=str.lower)
        if mode == 'open_dir':
            entries = dirs
        else:
            # filter by filetypes if given
            if filetypes:
                exts = set()
                for _, pat in filetypes:
                    if pat.startswith("*.") and pat != "*.*":
                        exts.add(pat[1:].lower())
                if exts:
                    files = [f for f in files
                             if os.path.splitext(f)[1].lower() in exts]
            entries = dirs + files
        scroll  = 0
        selected = None

    load_dir(cwd)

    _dbl_click_name = None   # name of last-clicked entry
    _dbl_click_time = 0      # pygame.time.get_ticks() of last click
    _DBL_CLICK_MS   = 400    # ms window for double-click

    clock = pygame.time.Clock()
    while True:
        dt = clock.tick(60)
        mx, my = pygame.mouse.get_pos()
        current_drive = _drive_root(cwd)
        drive_rects = []
        if show_drive_bar:
            drive_x = dx + 68
            drive_y = dy + 58
            drive_w = 56
            drive_h = 22
            for drive in drives:
                rect = pygame.Rect(drive_x, drive_y, drive_w, drive_h)
                drive_rects.append((rect, drive))
                drive_x += drive_w + 6

        # scrollbar geometry
        total = len(entries)
        max_scroll = max(0, total - ROWS)
        sb_h = max(20, int(LIST_H * ROWS / max(total, 1)))
        sb_h = min(sb_h, LIST_H)
        sb_y = LIST_Y + (int((LIST_H - sb_h) * scroll / max(max_scroll, 1))
                         if max_scroll > 0 else 0)
        sb_r = pygame.Rect(dx + dw - 16 - SB_W, LIST_Y, SB_W, LIST_H)
        sb_thumb = pygame.Rect(dx + dw - 16 - SB_W, sb_y, SB_W, sb_h)

        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:
                return None
            if ev.type == pygame.KEYDOWN:
                if ev.key == pygame.K_ESCAPE:
                    return None
                if ev.key == pygame.K_RETURN:
                    if selected:
                        full = os.path.join(cwd, selected)
                        if os.path.isdir(full):
                            load_dir(full); continue
                    # confirm
                    if mode == 'open_dir':
                        target = os.path.join(cwd, selected) if selected else cwd
                        if os.path.isdir(target): return target
                    elif mode == 'open_file':
                        if selected:
                            full = os.path.join(cwd, selected)
                            if os.path.isfile(full): return full
                    elif mode == 'save_file':
                        name = save_name.strip()
                        if name:
                            if defaultextension and not os.path.splitext(name)[1]:
                                name += defaultextension
                            return os.path.join(cwd, name)
                if mode == 'save_file' and ev.key == pygame.K_BACKSPACE:
                    save_name = save_name[:-1]
                elif mode == 'save_file' and ev.unicode and ev.unicode.isprintable():
                    save_name += ev.unicode
                if ev.key == pygame.K_UP:
                    scroll = max(0, scroll - 1)
                if ev.key == pygame.K_DOWN:
                    scroll = min(max_scroll, scroll + 1)

            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                # scrollbar drag start
                if sb_thumb.collidepoint(mx, my):
                    scroll_drag = True
                    drag_start_y = my
                    drag_start_scroll = scroll
                    continue
                # up button
                if up_r.collidepoint(mx, my):
                    parent = os.path.dirname(cwd)
                    if parent != cwd:
                        load_dir(parent)
                    elif show_drive_bar:
                        drive = _drive_root(cwd)
                        if drive:
                            load_dir(drive)
                    continue
                for drive_rect, drive_path in drive_rects:
                    if drive_rect.collidepoint(mx, my):
                        load_dir(drive_path)
                        break
                else:
                    drive_rect = None
                if drive_rect is not None:
                    continue
                # ok / cancel
                if ok_r.collidepoint(mx, my):
                    if mode == 'open_dir':
                        target = os.path.join(cwd, selected) if selected else cwd
                        if os.path.isdir(target): return target
                        return cwd
                    elif mode == 'open_file':
                        if selected:
                            full = os.path.join(cwd, selected)
                            if os.path.isfile(full): return full
                    elif mode == 'save_file':
                        name = save_name.strip()
                        if name:
                            if defaultextension and not os.path.splitext(name)[1]:
                                name += defaultextension
                            return os.path.join(cwd, name)
                    continue
                if can_r.collidepoint(mx, my):
                    return None
                # list rows
                for i in range(ROWS):
                    idx = scroll + i
                    if idx >= len(entries): break
                    row_r = pygame.Rect(dx + 16, LIST_Y + i * ROW_H,
                                       LIST_W - SB_W - 4, ROW_H)
                    if row_r.collidepoint(mx, my):
                        name = entries[idx]
                        full = os.path.join(cwd, name)
                        now = pygame.time.get_ticks()
                        is_double = (name == _dbl_click_name and
                                     now - _dbl_click_time < _DBL_CLICK_MS)
                        if is_double and os.path.isdir(full):
                            load_dir(full)
                            _dbl_click_name = None
                            _dbl_click_time = 0
                            break
                        selected = name
                        _dbl_click_name = name
                        _dbl_click_time = now
                        if mode == 'save_file':
                            if os.path.isfile(full):
                                save_name = name
                        break

            if ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
                scroll_drag = False

            if ev.type == pygame.MOUSEMOTION and scroll_drag:
                if max_scroll > 0:
                    dy_drag = my - drag_start_y
                    scroll = drag_start_scroll + int(dy_drag * max_scroll / (LIST_H - sb_h + 1))
                    scroll = max(0, min(max_scroll, scroll))

            if ev.type == pygame.MOUSEWHEEL:
                if pygame.Rect(dx, LIST_Y, dw, LIST_H).collidepoint(mx, my):
                    scroll = max(0, min(max_scroll, scroll - ev.y))

        # ── DRAW ──
        screen.blit(bg, (0, 0))
        pygame.draw.rect(screen, _PANEL,  (dx, dy, dw, dh), border_radius=6)
        pygame.draw.rect(screen, _BORDER, (dx, dy, dw, dh), 1, border_radius=6)

        # title
        _render_text(screen, title, dx + 16, dy + 10, _ACCENT, 15)

        # current path (truncated from left)
        path_str = cwd
        max_pw = dw - 32
        while _text_size(path_str)[0] > max_pw and len(path_str) > 4:
            path_str = "…" + path_str[4:]
        _render_text(screen, path_str, dx + 16, dy + 34, _TEXT_DIM, 12)

        # separator
        pygame.draw.line(screen, _BORDER,
                         (dx + 8, LIST_Y - 4), (dx + dw - 8, LIST_Y - 4))

        if show_drive_bar:
            _render_text(screen, "Drives:", dx + 16, dy + 62, _TEXT_DIM, 12)
            for drive_rect, drive_path in drive_rects:
                is_active = drive_path == current_drive
                is_hover = drive_rect.collidepoint(mx, my)
                fill = _SEL_BG if is_active else (_BTN_HOV if is_hover else _BTN_BG)
                border = _ACCENT if is_active else _BORDER
                text_col = _SEL_TEXT if is_active else _TEXT
                pygame.draw.rect(screen, fill, drive_rect, border_radius=4)
                pygame.draw.rect(screen, border, drive_rect, 1, border_radius=4)
                _render_text(screen, drive_path, drive_rect.x + 10, drive_rect.y + 4, text_col, 13)

        # list background
        pygame.draw.rect(screen, _INPUT_BG,
                         (dx + 16, LIST_Y, LIST_W - SB_W - 4, LIST_H), border_radius=3)

        # entries
        for i in range(ROWS):
            idx = scroll + i
            if idx >= len(entries): break
            name = entries[idx]
            full = os.path.join(cwd, name)
            is_dir = os.path.isdir(full)
            row_r = pygame.Rect(dx + 16, LIST_Y + i * ROW_H,
                                LIST_W - SB_W - 4, ROW_H)
            is_sel = (name == selected)
            is_hov = row_r.collidepoint(mx, my)
            if is_sel:
                pygame.draw.rect(screen, _SEL_BG, row_r, border_radius=2)
            elif is_hov:
                pygame.draw.rect(screen, _BTN_HOV, row_r, border_radius=2)
            prefix = "📁 " if is_dir else "   "
            col = _ACCENT if is_dir else (_SEL_TEXT if is_sel else _TEXT)
            label = prefix + name
            # truncate if too wide
            while _text_size(label)[0] > LIST_W - SB_W - 24 and len(label) > 4:
                label = label[:-2] + "…"
            _render_text(screen, label, row_r.x + 6, row_r.y + 5, col, 14)

        # scrollbar
        pygame.draw.rect(screen, _SCROLLBAR, sb_r, border_radius=3)
        if max_scroll > 0:
            hov_sb = sb_thumb.collidepoint(mx, my) or scroll_drag
            pygame.draw.rect(screen, _SCROLLHOV if hov_sb else _ACCENT,
                             sb_thumb, border_radius=3)

        # separator
        pygame.draw.line(screen, _BORDER,
                         (dx + 8, dy + dh - 100), (dx + dw - 8, dy + dh - 100))

        # save filename input
        if mode == 'save_file':
            _render_text(screen, "Filename:", dx + 16, dy + dh - 106, _TEXT_DIM, 12)
            pygame.draw.rect(screen, _INPUT_BG,  inp_r, border_radius=3)
            pygame.draw.rect(screen, _INPUT_BOR, inp_r, 1, border_radius=3)
            _render_text(screen, save_name + "|", inp_r.x + 6, inp_r.y + 6,
                         _SEL_TEXT, 14)

        # buttons
        _draw_button(screen, up_r,  "↑ Up",   up_r.collidepoint(mx, my))
        ok_label = "Select" if mode == 'open_dir' else "Open" if mode == 'open_file' else "Save"
        _draw_button(screen, ok_r,  ok_label, ok_r.collidepoint(mx, my))
        _draw_button(screen, can_r, "Cancel", can_r.collidepoint(mx, my))

        pygame.display.flip()


# ── Public API ────────────────────────────────────────────────────────────────

def ask_directory(screen, title="Select Folder", initial_dir=None):
    return _file_browser(screen, title, mode='open_dir', initial_dir=initial_dir)

def ask_open_filename(screen, title="Open File", filetypes=None, initial_dir=None):
    return _file_browser(screen, title, mode='open_file', filetypes=filetypes,
                         initial_dir=initial_dir)

def ask_save_filename(screen, title="Save File", defaultextension="", filetypes=None,
                      initial_dir=None):
    return _file_browser(screen, title, mode='save_file',
                         filetypes=filetypes, defaultextension=defaultextension,
                         initial_dir=initial_dir)


def ask_choice_list(screen, title, items, prompt="", initial_filter=""):
    """Return a selected string from a searchable list, or None on cancel."""
    bg = _overlay(screen)
    W, H = screen.get_size()
    dw, dh = min(900, W - 40), min(720, H - 40)
    dx, dy = (W - dw) // 2, (H - dh) // 2

    filter_text = initial_filter or ""
    selected = None
    scroll = 0
    cursor_vis = True
    cursor_timer = 0
    line_h = 22

    filter_r = pygame.Rect(dx + 16, dy + 72, dw - 32, 30)
    list_r = pygame.Rect(dx + 16, dy + 116, dw - 32, dh - 176)
    ok_r = pygame.Rect(dx + dw - 230, dy + dh - 46, 100, 32)
    can_r = pygame.Rect(dx + dw - 120, dy + dh - 46, 100, 32)

    def _filtered():
        query = filter_text.strip().lower()
        if not query:
            return list(items)
        parts = [part for part in query.replace("\\", "/").split() if part]
        return [item for item in items if all(part in item.lower() for part in parts)]

    clock = pygame.time.Clock()
    while True:
        dt = clock.tick(60)
        cursor_timer += dt
        if cursor_timer >= 500:
            cursor_vis = not cursor_vis
            cursor_timer = 0

        matches = _filtered()
        visible_rows = max(1, list_r.h // line_h)
        max_scroll = max(0, len(matches) - visible_rows)
        scroll = max(0, min(scroll, max_scroll))
        if selected not in matches:
            selected = matches[0] if matches else None

        mx, my = pygame.mouse.get_pos()
        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:
                return None
            if ev.type == pygame.KEYDOWN:
                if ev.key == pygame.K_ESCAPE:
                    return None
                if ev.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    return selected
                if ev.key == pygame.K_BACKSPACE:
                    filter_text = filter_text[:-1]
                    scroll = 0
                elif ev.key == pygame.K_UP:
                    if matches:
                        idx = matches.index(selected) if selected in matches else 0
                        idx = max(0, idx - 1)
                        selected = matches[idx]
                        if idx < scroll:
                            scroll = idx
                elif ev.key == pygame.K_DOWN:
                    if matches:
                        idx = matches.index(selected) if selected in matches else 0
                        idx = min(len(matches) - 1, idx + 1)
                        selected = matches[idx]
                        if idx >= scroll + visible_rows:
                            scroll = idx - visible_rows + 1
                elif ev.unicode and ev.unicode.isprintable():
                    filter_text += ev.unicode
                    scroll = 0
            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                if ok_r.collidepoint(mx, my):
                    return selected
                if can_r.collidepoint(mx, my):
                    return None
                if list_r.collidepoint(mx, my):
                    idx = scroll + max(0, (my - list_r.y) // line_h)
                    if 0 <= idx < len(matches):
                        selected = matches[idx]
            if ev.type == pygame.MOUSEWHEEL and list_r.collidepoint(mx, my):
                scroll = max(0, min(max_scroll, scroll - ev.y))

        screen.blit(bg, (0, 0))
        pygame.draw.rect(screen, _PANEL, (dx, dy, dw, dh), border_radius=6)
        pygame.draw.rect(screen, _BORDER, (dx, dy, dw, dh), 1, border_radius=6)
        _render_text(screen, title, dx + 16, dy + 14, _ACCENT, 15)
        if prompt:
            _render_text(screen, prompt, dx + 16, dy + 42, _TEXT, 14)

        pygame.draw.rect(screen, _INPUT_BG, filter_r, border_radius=4)
        pygame.draw.rect(screen, _INPUT_BOR, filter_r, 1, border_radius=4)
        display = filter_text + ("|" if cursor_vis else " ")
        _render_text(screen, display or "type to filter", filter_r.x + 8, filter_r.y + 8,
                     _SEL_TEXT if filter_text else _TEXT_DIM, 14)

        pygame.draw.rect(screen, _INPUT_BG, list_r, border_radius=4)
        pygame.draw.rect(screen, _INPUT_BOR, list_r, 1, border_radius=4)
        if matches:
            for row_idx, item in enumerate(matches[scroll:scroll + visible_rows]):
                row_y = list_r.y + row_idx * line_h
                row_rect = pygame.Rect(list_r.x + 4, row_y + 2, list_r.w - 8, line_h - 3)
                is_sel = item == selected
                if is_sel:
                    pygame.draw.rect(screen, _SEL_BG, row_rect, border_radius=3)
                    pygame.draw.rect(screen, _ACCENT, row_rect, 1, border_radius=3)
                elif row_rect.collidepoint(mx, my):
                    pygame.draw.rect(screen, _BTN_HOV, row_rect, border_radius=3)
                _render_text(screen, item, row_rect.x + 6, row_rect.y + 3,
                             _SEL_TEXT if is_sel else _TEXT, 14)
        else:
            _render_text(screen, "No matches", list_r.x + 8, list_r.y + 8, _TEXT_DIM, 14)

        _render_text(screen, f"{len(matches)} match(es)", dx + 16, dy + dh - 38, _TEXT_DIM, 12)
        _draw_button(screen, ok_r, "OK", ok_r.collidepoint(mx, my))
        _draw_button(screen, can_r, "Cancel", can_r.collidepoint(mx, my))
        pygame.display.flip()
